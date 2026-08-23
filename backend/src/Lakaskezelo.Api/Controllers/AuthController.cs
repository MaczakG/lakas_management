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
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    LakaskezeloDbContext db,
    PasswordHasher<User> passwordHasher,
    JwtTokenService tokenService,
    IEmailSender emailSender,
    IConfiguration configuration) : ControllerBase
{
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan TwoFactorCodeLifetime = TimeSpan.FromMinutes(10);
    private const int MaxTwoFactorAttempts = 5;

    [HttpPost("login")]
    public async Task<ActionResult<LoginChallengeResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { message = "Hibás e-mail cím vagy jelszó." });
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Hibás e-mail cím vagy jelszó." });
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
                <p style="margin:0;font-size:13px;color:#64748b;">A kód 10 percig érvényes. Ha nem te próbáltál bejelentkezni, hagyd figyelmen kívül ezt az e-mailt.</p>
                """);
        var textBody = $"Belépési kódod: {code} (10 percig érvényes)";

        var sent = await emailSender.SendAsync(user.Email, EmailTemplate.UniqueSubject("Belépési kód"), htmlBody, textBody, null, ct);
        if (!sent)
        {
            return StatusCode(502, new { message = $"Nem sikerült elküldeni a belépési kódot. {emailSender.LastError}" });
        }

        return Ok(new LoginChallengeResponse(challenge.Id));
    }

    [HttpPost("verify-2fa")]
    public async Task<ActionResult<AuthResponse>> VerifyTwoFactor(VerifyTwoFactorRequest request, CancellationToken ct)
    {
        var challenge = await db.TwoFactorChallenges.Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Id == request.ChallengeId, ct);

        if (challenge is null || challenge.UsedAt is not null || challenge.ExpiresAt < DateTime.UtcNow || challenge.AttemptCount >= MaxTwoFactorAttempts)
        {
            return BadRequest(new { message = "A kód érvénytelen vagy lejárt. Jelentkezz be újra a jelszavaddal." });
        }

        if (challenge.CodeHash != HashToken(request.Code.Trim()))
        {
            challenge.AttemptCount += 1;
            await db.SaveChangesAsync(ct);
            return BadRequest(new { message = "Hibás kód." });
        }

        challenge.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var user = challenge.User!;
        var (token, expiresAt) = tokenService.CreateAccessToken(user);
        return Ok(new AuthResponse(token, expiresAt, ToDto(user)));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        // Ismeretlen e-mail esetén is sikeres választ adunk — ne áruljuk el, mely címekhez tartozik fiók.
        if (user is null || !user.IsActive)
        {
            return Ok(new { message = "Ha létezik ehhez a címhez fiók, elküldtük a visszaállító linket." });
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
            return StatusCode(502, new { message = $"Nem sikerült elküldeni a visszaállító e-mailt. {emailSender.LastError}" });
        }

        return Ok(new { message = "Ha létezik ehhez a címhez fiók, elküldtük a visszaállító linket." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var tokenHash = HashToken(request.Token);
        var resetToken = await db.PasswordResetTokens.Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Token == tokenHash, ct);

        if (resetToken is null || resetToken.UsedAt is not null || resetToken.ExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "A link érvénytelen vagy lejárt. Kérj új visszaállító linket." });
        }
        if (request.NewPassword.Length < 8)
        {
            return BadRequest(new { message = "A jelszónak legalább 8 karakter hosszúnak kell lennie." });
        }

        var user = resetToken.User!;
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        resetToken.UsedAt = DateTime.UtcNow;
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

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.FullName, user.IsActive, user.CreatedAt);
}
