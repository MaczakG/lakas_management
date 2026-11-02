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

// A Platform admin külön belépése (platform-login.html) — a cégek felhasználóitól független fiókokkal
// (PlatformAdmin). Mindig kétlépcsős: jelszó + e-mailben kapott 6 jegyű kód; 5 hibás próbálkozás után
// 15 perc zárolás. Az első jelszót (és az elfelejtettet) e-mailben kapott linkkel lehet beállítani. A
// /api/auth/ alatt van, így az nginx és a backend IP-alapú korlátozása is vonatkozik rá.
[ApiController]
[Route("api/auth/platform")]
[EnableRateLimiting(AuthController.RateLimitPolicy)]
public class PlatformAuthController(
    LakaskezeloDbContext db, PasswordHasher<User> hasher, JwtTokenService tokens, SystemEmailSender email,
    IConfiguration configuration, ILogger<PlatformAuthController> logger) : ControllerBase
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);
    private const int MaxCodeAttempts = 5;
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const string InvalidCredentials = "Hibás e-mail cím vagy jelszó.";
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var normalized = request.Email?.Trim().ToLowerInvariant() ?? "";
        var admin = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.Email == normalized, ct);
        if (admin is null || !admin.IsActive || string.IsNullOrEmpty(admin.PasswordHash))
        {
            hasher.VerifyHashedPassword(new User(), DummyHash, request.Password ?? "");
            return Unauthorized(new { message = InvalidCredentials });
        }
        if (IsLockedOut(admin)) return LockedOut(admin);

        var result = hasher.VerifyHashedPassword(new User(), admin.PasswordHash, request.Password ?? "");
        if (result == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(admin, ct);
            return IsLockedOut(admin) ? LockedOut(admin) : Unauthorized(new { message = InvalidCredentials });
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded) admin.PasswordHash = hasher.HashPassword(new User(), request.Password!);

        var stale = await db.PlatformAdminTokens.Where(t => t.AdminId == admin.Id && t.Kind == PlatformAdminTokenKind.TwoFactorCode && t.UsedAt == null).ToListAsync(ct);
        db.PlatformAdminTokens.RemoveRange(stale);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = NewToken(admin, PlatformAdminTokenKind.TwoFactorCode, Hash(code), CodeLifetime);
        db.PlatformAdminTokens.Add(challenge);
        await db.SaveChangesAsync(ct);

        var html = EmailTemplate.Render("Platform admin belépési kód", "Platform admin belépési kód",
            $"""<p style="margin:0 0 14px;">A Platform admin belépéshez add meg az alábbi kódot:</p><p style="margin:0 0 14px;font-size:30px;font-weight:700;letter-spacing:6px;">{code}</p><p style="margin:0;font-size:13px;color:#64748b;">A kód 10 percig érvényes. Ha nem te próbáltál belépni, valaki ismeri a jelszavadat — változtasd meg mielőbb.</p>""");
        if (!await email.SendAsync(admin.Email, EmailTemplate.UniqueSubject("Platform admin belépési kód"), html, $"Platform admin belépési kód: {code} (10 percig érvényes).", ct))
        {
            logger.LogError("Platform admin sign-in code could not be sent: {Error}", email.LastError);
            return StatusCode(502, new { message = "Nem sikerült elküldeni a belépési kódot. Próbáld újra később." });
        }
        return Ok(new LoginChallengeResponse(challenge.Id));
    }

    [HttpPost("verify-2fa")]
    public async Task<ActionResult<PlatformAuthResponse>> Verify(VerifyTwoFactorRequest request, CancellationToken ct)
    {
        var challenge = await db.PlatformAdminTokens.Include(t => t.Admin)
            .SingleOrDefaultAsync(t => t.Id == request.ChallengeId && t.Kind == PlatformAdminTokenKind.TwoFactorCode, ct);
        const string invalid = "A kód érvénytelen vagy lejárt. Jelentkezz be újra a jelszavaddal.";
        if (challenge?.Admin is not { IsActive: true } admin || challenge.UsedAt is not null || challenge.ExpiresAt < DateTime.UtcNow
            || challenge.AttemptCount >= MaxCodeAttempts)
        {
            return BadRequest(new { message = invalid });
        }
        if (IsLockedOut(admin)) return LockedOut(admin);

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge.Hash), Encoding.ASCII.GetBytes(Hash((request.Code ?? "").Trim()))))
        {
            challenge.AttemptCount += 1;
            await RegisterFailureAsync(admin, ct);
            if (IsLockedOut(admin)) return LockedOut(admin);
            return BadRequest(new { message = challenge.AttemptCount >= MaxCodeAttempts ? invalid : "Hibás kód." });
        }

        challenge.UsedAt = DateTime.UtcNow;
        admin.FailedLoginCount = 0;
        admin.LockoutEndsAt = null;
        await db.SaveChangesAsync(ct);
        var (token, expiresAt) = tokens.CreatePlatformToken(admin);
        return Ok(new PlatformAuthResponse(token, expiresAt, new PlatformAdminDto(admin.Id, admin.Email, admin.FullName)));
    }

    // Elfelejtett jelszó és az első jelszó beállítása is ezzel megy (a válasz nem árulja el, létezik-e a fiók).
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        const string generic = "Ha létezik ehhez a címhez platform admin fiók, elküldtük a jelszó-beállító linket.";
        var normalized = request.Email?.Trim().ToLowerInvariant() ?? "";
        var admin = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.Email == normalized, ct);
        if (admin is not { IsActive: true }) return Ok(new { message = generic });

        var since = DateTime.UtcNow.AddMinutes(-1);
        if (await db.PlatformAdminTokens.AnyAsync(t => t.AdminId == admin.Id && t.Kind == PlatformAdminTokenKind.PasswordReset && t.CreatedAt > since, ct))
        {
            return Ok(new { message = generic });
        }
        var stale = await db.PlatformAdminTokens.Where(t => t.AdminId == admin.Id && t.Kind == PlatformAdminTokenKind.PasswordReset && t.UsedAt == null).ToListAsync(ct);
        db.PlatformAdminTokens.RemoveRange(stale);
        var plain = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.PlatformAdminTokens.Add(NewToken(admin, PlatformAdminTokenKind.PasswordReset, Hash(plain), ResetLifetime));
        await db.SaveChangesAsync(ct);

        var url = $"{configuration["Frontend:BaseUrl"] ?? ""}/platform-reset.html?token={plain}";
        var html = EmailTemplate.Render("Platform admin jelszó", "Platform admin jelszó beállítása",
            """<p style="margin:0;">Az alábbi gombbal 1 órán belül új jelszót állíthatsz be a Platform admin fiókodhoz. Ha nem te kérted, hagyd figyelmen kívül ezt az e-mailt.</p>""",
            ("Jelszó beállítása", url));
        if (!await email.SendAsync(admin.Email, EmailTemplate.UniqueSubject("Platform admin jelszó"), html, $"Platform admin jelszó beállítása (1 órán belül): {url}", ct))
        {
            logger.LogError("Platform admin reset e-mail could not be sent: {Error}", email.LastError);
            return StatusCode(502, new { message = "Nem sikerült elküldeni az e-mailt. Próbáld újra később." });
        }
        return Ok(new { message = generic });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var hash = Hash((request.Token ?? "").Trim());
        var token = await db.PlatformAdminTokens.Include(t => t.Admin)
            .SingleOrDefaultAsync(t => t.Hash == hash && t.Kind == PlatformAdminTokenKind.PasswordReset, ct);
        if (token?.Admin is not { IsActive: true } admin || token.UsedAt is not null || token.ExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "A link érvénytelen vagy lejárt. Kérj új linket." });
        }
        // A platform-admin jelszava legyen erősebb: legalább 12 karakter.
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 12 || request.NewPassword.Length > 200)
        {
            return BadRequest(new { message = "A jelszónak legalább 12 karakter hosszúnak kell lennie." });
        }
        admin.PasswordHash = hasher.HashPassword(new User(), request.NewPassword);
        admin.SecurityStamp = Auth.SecurityStamp.New();
        admin.FailedLoginCount = 0;
        admin.LockoutEndsAt = null;
        token.UsedAt = DateTime.UtcNow;
        var pending = await db.PlatformAdminTokens.Where(t => t.AdminId == admin.Id && t.Kind == PlatformAdminTokenKind.TwoFactorCode && t.UsedAt == null).ToListAsync(ct);
        db.PlatformAdminTokens.RemoveRange(pending);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "A jelszó beállítva. Most már bejelentkezhetsz.", email = admin.Email });
    }

    [Authorize(Roles = PlatformOperatorRole)]
    [HttpGet("me")]
    public async Task<ActionResult<PlatformAdminDto>> Me(CancellationToken ct)
    {
        var admin = await db.PlatformAdmins.FindAsync([User.GetUserId()], ct);
        return admin is null ? NotFound() : Ok(new PlatformAdminDto(admin.Id, admin.Email, admin.FullName));
    }

    public const string PlatformOperatorRole = "PlatformAdmin";

    private static PlatformAdminToken NewToken(PlatformAdmin admin, PlatformAdminTokenKind kind, string hash, TimeSpan lifetime) => new()
    {
        Id = Guid.NewGuid(), AdminId = admin.Id, Kind = kind, Hash = hash,
        ExpiresAt = DateTime.UtcNow.Add(lifetime), CreatedAt = DateTime.UtcNow,
    };

    private static bool IsLockedOut(PlatformAdmin a) => a.LockoutEndsAt is { } until && until > DateTime.UtcNow;

    private ObjectResult LockedOut(PlatformAdmin a) => StatusCode(StatusCodes.Status429TooManyRequests, new
    {
        message = $"Túl sok sikertelen próbálkozás miatt a fiók átmenetileg zárolva van. Próbáld újra {Math.Max(1, (int)Math.Ceiling((a.LockoutEndsAt!.Value - DateTime.UtcNow).TotalMinutes))} perc múlva.",
    });

    private async Task RegisterFailureAsync(PlatformAdmin admin, CancellationToken ct)
    {
        if (admin.LockoutEndsAt is not null && !IsLockedOut(admin)) { admin.FailedLoginCount = 0; admin.LockoutEndsAt = null; }
        admin.FailedLoginCount += 1;
        if (admin.FailedLoginCount >= MaxFailedLogins)
        {
            admin.LockoutEndsAt = DateTime.UtcNow.Add(LockoutDuration);
            admin.FailedLoginCount = 0;
            logger.LogWarning("Platform admin {AdminId} locked out after repeated failed sign-in attempts", admin.Id);
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Hash(string value) => UserTokenService.Hash(value);
}
