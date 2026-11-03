using System.Security.Cryptography;
using System.Text;
using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    LakaskezeloDbContext db,
    PasswordHasher<User> passwordHasher,
    JwtTokenService tokenService,
    SystemEmailSender emailSender,
    AccountEmails accountEmails,
    UserTokenService userTokens,
    IConfiguration configuration,
    ICompanyContext companyContext,
    Subscriptions.PlatformSettingsService platform,
    ILogger<AuthController> logger) : ControllerBase
{
    // A bejelentkezési végpontok IP-címenkénti korlátja (ld. Program.cs AddRateLimiter).
    public const string RateLimitPolicy = "auth";

    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan VerificationTokenLifetime = TimeSpan.FromHours(48);
    private static readonly TimeSpan VerificationEmailCooldown = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TwoFactorCodeLifetime = TimeSpan.FromMinutes(10);
    private const int MaxTwoFactorAttempts = 5;

    // Fiókzárolás: ennyi egymás utáni hibás jelszó vagy belépési kód után LockoutDuration-ig nem
    // lehet bejelentkezni — így sem a jelszó, sem a 6 jegyű kód nem találgatható végig.
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // Ugyanarra a fiókra legfeljebb ilyen gyakran megy ki jelszó-visszaállító e-mail (az elfelejtett
    // jelszó oldal bejelentkezés nélkül elérhető, ne lehessen vele valakinek a postafiókját elárasztani).
    private static readonly TimeSpan ResetEmailCooldown = TimeSpan.FromMinutes(1);

    private const string InvalidCredentialsMessage = "Hibás e-mail cím vagy jelszó.";
    private const string InvalidChallengeMessage = "A kód érvénytelen vagy lejárt. Jelentkezz be újra a jelszavaddal.";

    // Nem létező fióknál is lefut egy jelszó-ellenőrzés ezen a hash-en, hogy a válaszidőből ne
    // derüljön ki, mely e-mail címekhez tartozik fiók.
    private static readonly string DummyPasswordHash =
        new PasswordHasher<User>().HashPassword(new User(), Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    // Kétlépcsős belépés: a jelszó után egy e-mailben kiküldött 6 jegyű kód is kell. Alapból be van
    // kapcsolva; vészhelyzetre (pl. ha az e-mail küldés tartósan nem működik, és emiatt senki sem tud
    // belépni) az Auth__TwoFactorEnabled=false környezeti változóval kikapcsolható.
    private bool TwoFactorEnabled => configuration.GetValue("Auth:TwoFactorEnabled", true);

    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        // Bejelentkezés előtt még nincs cég-kontextus: a felhasználót minden cégben keressük, utána a
        // kérés az ő cégére áll (a mentés és az e-mail-beállítások ehhez kellenek).
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user is not null) companyContext.Set(user.CompanyId);

        // Meghívott, de a meghívót még el nem fogadó felhasználónak nincs jelszava.
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
        {
            passwordHasher.VerifyHashedPassword(new User(), DummyPasswordHash, request.Password);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        if (IsLockedOut(user)) return LockedOut(user);

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            await RegisterFailedAttemptAsync(user, ct);
            return IsLockedOut(user) ? LockedOut(user) : Unauthorized(new { message = InvalidCredentialsMessage });
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Régebbi (gyengébb paraméterű) hash — a helyes jelszóval most frissítjük.
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        // Helyes jelszó, de a regisztrációt még nem erősítette meg: újraküldjük a linket (percenként
        // legfeljebb egyszer). Csak helyes jelszó után derül ki, így nem árulja el, mely címek léteznek.
        if (user.EmailVerifiedAt is null)
        {
            if (!await userTokens.IssuedRecentlyAsync(user.Id, UserTokenPurpose.EmailVerification, VerificationEmailCooldown, ct))
            {
                var verifyToken = await userTokens.IssueAsync(user, UserTokenPurpose.EmailVerification, VerificationTokenLifetime, ct);
                await db.SaveChangesAsync(ct);
                var companyName = await CompanyNameAsync(user.CompanyId, ct);
                if (!await accountEmails.SendVerificationAsync(user.Email, companyName, verifyToken, ct))
                {
                    logger.LogError("Verification e-mail could not be re-sent to user {UserId}: {Error}", user.Id, accountEmails.LastError);
                }
            }
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Az e-mail címed még nincs megerősítve. Elküldtük (újra) a megerősítő linket — kattints rá, és utána jelentkezz be.",
            });
        }

        if (!TwoFactorEnabled)
        {
            ResetFailedAttempts(user);
            await db.SaveChangesAsync(ct);
            var (token, expiresAt) = tokenService.CreateAccessToken(user);
            return Ok(new AuthResponse(token, expiresAt, ToDto(user), await CompanyNameAsync(user.CompanyId, ct)));
        }

        // A jelszó rendben — de a bejelentkezés csak egy e-mailben kiküldött 6 jegyű kóddal
        // fejeződik be (ld. VerifyTwoFactor). A korábbi, még fel nem használt kódokat eldobjuk,
        // hogy egy elveszett/be nem gépelt kód ne maradjon örökre érvényes.
        var staleChallenges = await db.TwoFactorChallenges.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync(ct);
        db.TwoFactorChallenges.RemoveRange(staleChallenges);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new TwoFactorChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CodeHash = HashToken(code),
            ExpiresAt = DateTime.UtcNow.Add(TwoFactorCodeLifetime),
            CreatedAt = DateTime.UtcNow,
        };
        db.TwoFactorChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        var htmlBody = EmailTemplate.Render(
            preheader: "Belépési kód",
            heading: "Belépési kód",
            bodyHtml: $"""
                <p style="margin:0 0 14px;">A belépéshez add meg az alábbi kódot:</p>
                <p style="margin:0 0 14px;font-size:30px;font-weight:700;letter-spacing:6px;">{code}</p>
                <p style="margin:0;font-size:13px;color:#64748b;">A kód 10 percig érvényes. Ha nem te próbáltál bejelentkezni, valaki ismeri a jelszavadat — változtasd meg mielőbb az „Elfelejtett jelszó” oldalon.</p>
                """);
        var textBody = $"Belépési kódod: {code} (10 percig érvényes). Ha nem te próbáltál bejelentkezni, változtasd meg a jelszavadat.";

        var sent = await emailSender.SendAsync(user.Email, EmailTemplate.UniqueSubject("Belépési kód"), htmlBody, textBody, ct);
        if (!sent)
        {
            logger.LogError("Sign-in code e-mail could not be sent to user {UserId}: {Error}", user.Id, emailSender.LastError);
            return StatusCode(502, new { message = "Nem sikerült elküldeni a belépési kódot. Próbáld újra később." });
        }

        return Ok(new LoginChallengeResponse(challenge.Id));
    }

    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("verify-2fa")]
    public async Task<ActionResult<AuthResponse>> VerifyTwoFactor(VerifyTwoFactorRequest request, CancellationToken ct)
    {
        var challenge = await db.TwoFactorChallenges.IgnoreQueryFilters().Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Id == request.ChallengeId, ct);
        if (challenge?.User is { } challengeUser) companyContext.Set(challengeUser.CompanyId);

        if (challenge is null || challenge.UsedAt is not null || challenge.ExpiresAt < DateTime.UtcNow
            || challenge.AttemptCount >= MaxTwoFactorAttempts || challenge.User is not { IsActive: true, EmailVerifiedAt: not null })
        {
            return BadRequest(new { message = InvalidChallengeMessage });
        }

        var user = challenge.User;
        if (IsLockedOut(user)) return LockedOut(user);

        if (!CodeMatches(challenge.CodeHash, request.Code))
        {
            challenge.AttemptCount += 1;
            await RegisterFailedAttemptAsync(user, ct);
            if (IsLockedOut(user)) return LockedOut(user);
            return BadRequest(new { message = challenge.AttemptCount >= MaxTwoFactorAttempts ? InvalidChallengeMessage : "Hibás kód." });
        }

        challenge.UsedAt = DateTime.UtcNow;
        ResetFailedAttempts(user);
        await db.SaveChangesAsync(ct);

        var (token, expiresAt) = tokenService.CreateAccessToken(user);
        return Ok(new AuthResponse(token, expiresAt, ToDto(user), await CompanyNameAsync(user.CompanyId, ct)));
    }

    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        const string genericMessage = "Ha létezik ehhez a címhez fiók, elküldtük a visszaállító linket.";
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        // Bejelentkezés előtt még nincs cég-kontextus: a felhasználót minden cégben keressük, utána a
        // kérés az ő cégére áll (a mentés és az e-mail-beállítások ehhez kellenek).
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user is not null) companyContext.Set(user.CompanyId);

        // Ismeretlen e-mail esetén is sikeres választ adunk — ne áruljuk el, mely címekhez tartozik fiók.
        if (user is null || !user.IsActive)
        {
            return Ok(new { message = genericMessage });
        }

        if (await userTokens.IssuedRecentlyAsync(user.Id, UserTokenPurpose.PasswordReset, ResetEmailCooldown, ct))
        {
            return Ok(new { message = genericMessage });
        }

        var plainToken = await userTokens.IssueAsync(user, UserTokenPurpose.PasswordReset, ResetTokenLifetime, ct);
        await db.SaveChangesAsync(ct);

        var frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "";
        var resetUrl = $"{frontendBaseUrl}/reset-password.html?token={plainToken}";

        var htmlBody = EmailTemplate.Render(
            preheader: "Jelszó visszaállítása",
            heading: "Jelszó visszaállítása",
            bodyHtml: """<p style="margin:0;">Az alábbi gombra kattintva 1 órán belül új jelszót állíthatsz be. Ha nem te kérted, hagyd figyelmen kívül ezt az e-mailt.</p>""",
            cta: ("Új jelszó beállítása", resetUrl));
        var textBody = $"Jelszó visszaállítása: {resetUrl} (1 órán belül érvényes)";

        var sent = await emailSender.SendAsync(user.Email, EmailTemplate.UniqueSubject("Jelszó visszaállítása"), htmlBody, textBody, ct);
        if (!sent)
        {
            logger.LogError("Password reset e-mail could not be sent to user {UserId}: {Error}", user.Id, emailSender.LastError);
            return StatusCode(502, new { message = "Nem sikerült elküldeni a visszaállító e-mailt. Próbáld újra később." });
        }

        return Ok(new { message = genericMessage });
    }

    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var resetToken = await userTokens.FindValidAsync(request.Token, UserTokenPurpose.PasswordReset, ct);
        if (resetToken?.User is { } resetUser) companyContext.Set(resetUser.CompanyId);

        if (resetToken?.User is null)
        {
            return BadRequest(new { message = "A link érvénytelen vagy lejárt. Kérj új visszaállító linket." });
        }
        if (PasswordError(request.NewPassword) is { } passwordError) return BadRequest(new { message = passwordError });

        var user = resetToken.User;
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        resetToken.UsedAt = DateTime.UtcNow;
        // A link a postafiókba ment, így a cím birtoklását is igazolja (pl. meghívott, aki a meghívó
        // helyett az elfelejtett jelszóval állít be jelszót).
        user.EmailVerifiedAt ??= DateTime.UtcNow;

        // Az új jelszóval minden korábbi munkamenet és függő belépési kód érvényét veszti, és a
        // (postafiók birtoklásával igazolt) felhasználó zárolása is feloldódik.
        SecurityStamp.Rotate(user);
        ResetFailedAttempts(user);
        var pendingChallenges = await db.TwoFactorChallenges.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync(ct);
        db.TwoFactorChallenges.RemoveRange(pendingChallenges);
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "A jelszó megváltozott." });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.GetUserId()], ct);
        if (user is null) return NotFound();
        return Ok(new MeResponse(ToDto(user), await CompanyNameAsync(user.CompanyId, ct)));
    }

    // ---------- Regisztráció (új cég) és meghívó elfogadása ----------

    private bool RegistrationEnabled => configuration.GetValue("Registration:Enabled", false);

    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (!RegistrationEnabled) return NotFound(new { message = "A regisztráció jelenleg nem elérhető." });

        var companyName = request.CompanyName?.Trim() ?? "";
        var fullName = request.FullName?.Trim() ?? "";
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (companyName.Length is < 2 or > 200) return BadRequest(new { message = "Add meg a cég (vagy a saját) nevét (2–200 karakter)." });
        if (fullName.Length is < 2 or > 200) return BadRequest(new { message = "Add meg a neved (2–200 karakter)." });
        if (!LooksLikeEmail(email)) return BadRequest(new { message = "Érvénytelen e-mail cím." });
        if (PasswordError(request.Password) is { } passwordError) return BadRequest(new { message = passwordError });
        if (!request.AcceptTerms) return BadRequest(new { message = "A regisztrációhoz el kell fogadnod az ÁSZF-et és az adatkezelési tájékoztatót." });

        // Mindkét ágon ugyanez a válasz, és mindkét ágon megy e-mail — a válaszból nem derül ki, hogy
        // a címhez tartozik-e már fiók.
        var okMessage = new { message = $"Elküldtük a megerősítő e-mailt a(z) {email} címre. Kattints a benne lévő linkre, utána bejelentkezhetsz." };

        var existing = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == email, ct);
        if (existing is not null)
        {
            if (!await IsAbandonedRegistrationAsync(existing, ct))
            {
                if (!await accountEmails.SendAlreadyRegisteredAsync(email, ct))
                {
                    logger.LogWarning("Already-registered notice could not be sent: {Error}", accountEmails.LastError);
                }
                return Ok(okMessage);
            }

            // Korábbi, soha meg nem erősített regisztráció ugyanerre a címre — eldobjuk, és újat kezdünk
            // (különben bárki "lefoglalhatna" egy idegen címet egy meg nem erősített regisztrációval).
            await DeleteRegistrationAsync(existing, ct);
        }

        var terms = await platform.GetAsync(ct);
        var company = new Company
        {
            Id = Guid.NewGuid(), Name = companyName, CreatedAt = DateTime.UtcNow,
            TermsVersion = terms.TermsVersion, TermsAcceptedAt = DateTime.UtcNow,
            IsBusiness = request.IsBusiness,
        };
        db.Companies.Add(company);
        companyContext.Set(company.Id);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            IsActive = true,
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow,
            SecurityStamp = SecurityStamp.New(),
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var token = await userTokens.IssueAsync(user, UserTokenPurpose.EmailVerification, VerificationTokenLifetime, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("New company {CompanyId} registered", company.Id);

        if (!await accountEmails.SendVerificationAsync(email, companyName, token, ct))
        {
            logger.LogError("Verification e-mail could not be sent for company {CompanyId}: {Error}", company.Id, accountEmails.LastError);
            return StatusCode(502, new { message = "A fiók elkészült, de a megerősítő e-mailt nem sikerült elküldeni. Próbálj meg bejelentkezni — ilyenkor újraküldjük." });
        }
        return Ok(okMessage);
    }

    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(TokenRequest request, CancellationToken ct)
    {
        var token = await userTokens.FindValidAsync(request.Token, UserTokenPurpose.EmailVerification, ct);
        if (token?.User is not { } user)
        {
            return BadRequest(new { message = "A link érvénytelen vagy lejárt. Jelentkezz be a jelszavaddal — ha a cím még nincs megerősítve, új linket küldünk." });
        }
        companyContext.Set(user.CompanyId);
        token.UsedAt = DateTime.UtcNow;
        user.EmailVerifiedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Az e-mail címed megerősítve. Most már bejelentkezhetsz.", email = user.Email });
    }

    // A meghívó oldal ezzel mutatja meg, melyik cégbe és milyen címmel szól a meghívó.
    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("invite-info")]
    public async Task<ActionResult<InviteInfoResponse>> InviteInfo(TokenRequest request, CancellationToken ct)
    {
        var token = await userTokens.FindValidAsync(request.Token, UserTokenPurpose.Invitation, ct);
        if (token?.User is not { } user) return BadRequest(new { message = InvalidInviteMessage });
        return Ok(new InviteInfoResponse(user.Email, user.FullName, await CompanyNameAsync(user.CompanyId, ct)));
    }

    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("accept-invite")]
    public async Task<IActionResult> AcceptInvite(AcceptInviteRequest request, CancellationToken ct)
    {
        var token = await userTokens.FindValidAsync(request.Token, UserTokenPurpose.Invitation, ct);
        if (token?.User is not { } user) return BadRequest(new { message = InvalidInviteMessage });
        if (PasswordError(request.Password) is { } passwordError) return BadRequest(new { message = passwordError });
        var fullName = request.FullName?.Trim();
        if (fullName is { Length: > 200 }) return BadRequest(new { message = "A név legfeljebb 200 karakter lehet." });

        companyContext.Set(user.CompanyId);
        if (!string.IsNullOrEmpty(fullName)) user.FullName = fullName;
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user.EmailVerifiedAt ??= DateTime.UtcNow;
        SecurityStamp.Rotate(user);
        ResetFailedAttempts(user);
        token.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Kész! Most már bejelentkezhetsz.", email = user.Email });
    }

    private const string InvalidInviteMessage = "A meghívó érvénytelen vagy lejárt. Kérj új meghívót a cég adminisztrátorától.";

    // Soha meg nem erősített, saját cégét regisztráló fiók (a cégében egyedül van) — se adat, se belépés
    // nem tartozhat hozzá, így biztonságosan eldobható. Meghívott (más cégbe tartozó) fiókra nem igaz.
    private async Task<bool> IsAbandonedRegistrationAsync(User user, CancellationToken ct) =>
        user.EmailVerifiedAt is null && user.Role == UserRole.Admin && user.CompanyId != Company.DefaultCompanyId
        && await db.Users.IgnoreQueryFilters().CountAsync(u => u.CompanyId == user.CompanyId, ct) == 1;

    private async Task DeleteRegistrationAsync(User user, CancellationToken ct)
    {
        companyContext.Set(user.CompanyId);
        var company = await db.Companies.SingleAsync(c => c.Id == user.CompanyId, ct);
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        db.Companies.Remove(company);
        await db.SaveChangesAsync(ct);
        companyContext.Set(null);
    }

    private async Task<string> CompanyNameAsync(Guid companyId, CancellationToken ct) =>
        await db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).SingleOrDefaultAsync(ct) ?? "";

    internal static string? PasswordError(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < 8 ? "A jelszónak legalább 8 karakter hosszúnak kell lennie."
        : password.Length > 200 ? "A jelszó legfeljebb 200 karakter lehet."
        : null;

    internal static bool LooksLikeEmail(string email) =>
        email.Length is >= 3 and <= 256 && email.IndexOf('@') is > 0 and var at && at < email.Length - 1
        && !email.Any(char.IsWhiteSpace) && System.Net.Mail.MailAddress.TryCreate(email, out _);

    private static bool IsLockedOut(User user) => user.LockoutEndsAt is { } until && until > DateTime.UtcNow;

    private ObjectResult LockedOut(User user)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((user.LockoutEndsAt!.Value - DateTime.UtcNow).TotalMinutes));
        return StatusCode(StatusCodes.Status429TooManyRequests, new
        {
            message = $"Túl sok sikertelen próbálkozás miatt a fiók átmenetileg zárolva van. Próbáld újra {minutes} perc múlva.",
        });
    }

    private async Task RegisterFailedAttemptAsync(User user, CancellationToken ct)
    {
        // Lejárt zárolás után tiszta lappal indul a számlálás.
        if (user.LockoutEndsAt is not null && !IsLockedOut(user))
        {
            ResetFailedAttempts(user);
        }

        user.FailedLoginCount += 1;
        var lockedNow = user.FailedLoginCount >= MaxFailedLogins;
        if (lockedNow)
        {
            user.LockoutEndsAt = DateTime.UtcNow.Add(LockoutDuration);
            user.FailedLoginCount = 0;
            logger.LogWarning("User {UserId} locked out until {LockoutEndsAt} after repeated failed sign-in attempts", user.Id, user.LockoutEndsAt);
        }
        await db.SaveChangesAsync(ct);

        if (lockedNow)
        {
            await SendLockoutNoticeAsync(user, ct);
        }
    }

    private static void ResetFailedAttempts(User user)
    {
        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
    }

    // Értesítés a fiók tulajdonosának — ha nem ő próbálkozott, tudjon róla, és cseréljen jelszót.
    private async Task SendLockoutNoticeAsync(User user, CancellationToken ct)
    {
        var htmlBody = EmailTemplate.Render(
            preheader: "Sikertelen belépési kísérletek",
            heading: "Sikertelen belépési kísérletek",
            bodyHtml: $"""
                <p style="margin:0 0 14px;">A fiókodba többször egymás után hibás jelszóval vagy belépési kóddal próbáltak bejelentkezni, ezért {LockoutDuration.TotalMinutes:0} percre zároltuk.</p>
                <p style="margin:0;">Ha nem te voltál, változtasd meg a jelszavadat az „Elfelejtett jelszó” oldalon.</p>
                """);
        var textBody = $"A fiókodba többször hibásan próbáltak bejelentkezni, ezért {LockoutDuration.TotalMinutes:0} percre zároltuk. Ha nem te voltál, változtasd meg a jelszavadat.";

        var sent = await emailSender.SendAsync(user.Email, EmailTemplate.UniqueSubject("Sikertelen belépési kísérletek"), htmlBody, textBody, ct);
        if (!sent)
        {
            logger.LogWarning("Lockout notice could not be sent to user {UserId}: {Error}", user.Id, emailSender.LastError);
        }
    }

    private static bool CodeMatches(string expectedHash, string code) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(HashToken(code.Trim())));

    private static string HashToken(string token) => UserTokenService.Hash(token);

    internal static UserDto ToDto(User user) =>
        new(user.Id, user.Email, user.FullName, user.IsActive, user.CreatedAt, user.Role, user.EmailVerifiedAt is null);
}
