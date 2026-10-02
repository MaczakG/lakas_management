using System.Security.Cryptography;
using System.Text;
using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
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
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
{
    // A bejelentkezési végpontok IP-címenkénti korlátja (ld. Program.cs AddRateLimiter).
    public const string RateLimitPolicy = "auth";

    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);
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
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null || !user.IsActive)
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

        if (!TwoFactorEnabled)
        {
            ResetFailedAttempts(user);
            await db.SaveChangesAsync(ct);
            var (token, expiresAt) = tokenService.CreateAccessToken(user);
            return Ok(new AuthResponse(token, expiresAt, ToDto(user)));
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

        var sent = await emailSender.SendAsync(user.Email, EmailTemplate.UniqueSubject("Belépési kód"), htmlBody, textBody, null, ct);
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
        var challenge = await db.TwoFactorChallenges.Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Id == request.ChallengeId, ct);

        if (challenge is null || challenge.UsedAt is not null || challenge.ExpiresAt < DateTime.UtcNow
            || challenge.AttemptCount >= MaxTwoFactorAttempts || challenge.User is not { IsActive: true })
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
        return Ok(new AuthResponse(token, expiresAt, ToDto(user)));
    }

    [EnableRateLimiting(RateLimitPolicy)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        const string genericMessage = "Ha létezik ehhez a címhez fiók, elküldtük a visszaállító linket.";
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        // Ismeretlen e-mail esetén is sikeres választ adunk — ne áruljuk el, mely címekhez tartozik fiók.
        if (user is null || !user.IsActive)
        {
            return Ok(new { message = genericMessage });
        }

        var cooldownStart = DateTime.UtcNow.Subtract(ResetEmailCooldown);
        if (await db.PasswordResetTokens.AnyAsync(t => t.UserId == user.Id && t.CreatedAt > cooldownStart, ct))
        {
            return Ok(new { message = genericMessage });
        }

        var stale = await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.UsedAt == null).ToListAsync(ct);
        db.PasswordResetTokens.RemoveRange(stale);

        var plainToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var resetToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = HashToken(plainToken),
            ExpiresAt = DateTime.UtcNow.Add(ResetTokenLifetime),
            CreatedAt = DateTime.UtcNow,
        };
        db.PasswordResetTokens.Add(resetToken);
        await db.SaveChangesAsync(ct);

        var frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "";
        var resetUrl = $"{frontendBaseUrl}/reset-password.html?token={plainToken}";

        var htmlBody = EmailTemplate.Render(
            preheader: "Jelszó visszaállítása",
            heading: "Jelszó visszaállítása",
            bodyHtml: """<p style="margin:0;">Az alábbi gombra kattintva 1 órán belül új jelszót állíthatsz be. Ha nem te kérted, hagyd figyelmen kívül ezt az e-mailt.</p>""",
            cta: ("Új jelszó beállítása", resetUrl));
        var textBody = $"Jelszó visszaállítása: {resetUrl} (1 órán belül érvényes)";

        var sent = await emailSender.SendAsync(user.Email, EmailTemplate.UniqueSubject("Jelszó visszaállítása"), htmlBody, textBody, null, ct);
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
        var tokenHash = HashToken(request.Token);
        var resetToken = await db.PasswordResetTokens.Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Token == tokenHash, ct);

        if (resetToken is null || resetToken.UsedAt is not null || resetToken.ExpiresAt < DateTime.UtcNow || resetToken.User is not { IsActive: true })
        {
            return BadRequest(new { message = "A link érvénytelen vagy lejárt. Kérj új visszaállító linket." });
        }
        if (request.NewPassword.Length < 8)
        {
            return BadRequest(new { message = "A jelszónak legalább 8 karakter hosszúnak kell lennie." });
        }

        var user = resetToken.User;
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        resetToken.UsedAt = DateTime.UtcNow;

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
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([User.GetUserId()], ct);
        if (user is null) return NotFound();
        return Ok(ToDto(user));
    }

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

        var sent = await emailSender.SendAsync(user.Email, EmailTemplate.UniqueSubject("Sikertelen belépési kísérletek"), htmlBody, textBody, null, ct);
        if (!sent)
        {
            logger.LogWarning("Lockout notice could not be sent to user {UserId}: {Error}", user.Id, emailSender.LastError);
        }
    }

    private static bool CodeMatches(string expectedHash, string code) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(HashToken(code.Trim())));

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.FullName, user.IsActive, user.CreatedAt);
}
