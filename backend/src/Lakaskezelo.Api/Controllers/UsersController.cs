using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// A cég felhasználói. A listát mindenki látja; meghívni, módosítani és törölni csak adminisztrátor
// tud. Új felhasználó csak meghívóval jön létre: a jelszavát ő maga állítja be a meghívó linkjén,
// így azt senki más nem ismeri. A cégnek mindig marad legalább egy aktív adminisztrátora.
[Authorize]
[ApiController]
[Route("api/users")]
public class UsersController(
    LakaskezeloDbContext db, UserTokenService userTokens, AccountEmails accountEmails, ILogger<UsersController> logger) : ControllerBase
{
    public const string AdminRole = nameof(UserRole.Admin);
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List(CancellationToken ct)
    {
        var users = await db.Users.OrderBy(u => u.FullName).ToListAsync(ct);
        return Ok(users.Select(AuthController.ToDto));
    }

    [Authorize(Roles = AdminRole)]
    [HttpPost("invite")]
    public async Task<ActionResult<UserDto>> Invite(InviteUserRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var fullName = request.FullName?.Trim() ?? "";
        if (!AuthController.LooksLikeEmail(email)) return BadRequest(new { message = "Érvénytelen e-mail cím." });
        if (fullName.Length is < 2 or > 200) return BadRequest(new { message = "Add meg a nevet (2–200 karakter)." });
        if (!Enum.IsDefined(request.Role)) return BadRequest(new { message = "Érvénytelen szerepkör." });
        // Az e-mail cím az összes cégben egyedi (ezzel lép be) — ezért itt minden cég felhasználóit nézzük.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
        {
            return Conflict(new { message = "Ehhez az e-mail címhez már tartozik felhasználó (ebben vagy egy másik cégben)." });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            IsActive = true,
            Role = request.Role,
            CreatedAt = DateTime.UtcNow,
            SecurityStamp = SecurityStamp.New(),
            PasswordHash = string.Empty, // a meghívó elfogadásakor állítja be
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        if (await SendInvitationAsync(user, ct) is { } error) return error;
        return Ok(AuthController.ToDto(user));
    }

    [Authorize(Roles = AdminRole)]
    [HttpPost("{id:guid}/resend-invite")]
    public async Task<IActionResult> ResendInvite(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        if (user.EmailVerifiedAt is not null) return BadRequest(new { message = "Ez a felhasználó már elfogadta a meghívót." });
        if (await userTokens.IssuedRecentlyAsync(user.Id, UserTokenPurpose.Invitation, TimeSpan.FromMinutes(1), ct))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "A meghívót az imént küldtük el — próbáld újra egy perc múlva." });
        }
        if (await SendInvitationAsync(user, ct) is { } error) return error;
        return Ok(new { message = $"A meghívót újraküldtük a(z) {user.Email} címre." });
    }

    [Authorize(Roles = AdminRole)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var fullName = request.FullName?.Trim() ?? "";
        if (!AuthController.LooksLikeEmail(email)) return BadRequest(new { message = "Érvénytelen e-mail cím." });
        if (fullName.Length is < 2 or > 200) return BadRequest(new { message = "Add meg a nevet (2–200 karakter)." });
        if (!Enum.IsDefined(request.Role)) return BadRequest(new { message = "Érvénytelen szerepkör." });
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email && u.Id != id, ct))
        {
            return Conflict(new { message = "Ehhez az e-mail címhez már tartozik felhasználó." });
        }

        var losesAdmin = IsActiveAdmin(user) && (request.Role != UserRole.Admin || !request.IsActive);
        if (losesAdmin && !await HasOtherActiveAdminAsync(user.Id, ct))
        {
            return BadRequest(new { message = "Ő a cég egyetlen aktív adminisztrátora — előbb nevezz ki mást adminisztrátornak." });
        }

        // Új e-mail cím (ide megy a belépési kód) vagy inaktiválás után a felhasználó korábban kiadott
        // tokenjei azonnal érvényüket vesztik (ld. User.SecurityStamp). A szerepkör-változás kérésenként
        // az adatbázisból olvasódik (Program.cs OnTokenValidated), ahhoz nem kell.
        var revokeSessions = user.Email != email || (user.IsActive && !request.IsActive);

        user.Email = email;
        user.FullName = fullName;
        user.IsActive = request.IsActive;
        user.Role = request.Role;
        if (revokeSessions) SecurityStamp.Rotate(user);

        await db.SaveChangesAsync(ct);
        return Ok(AuthController.ToDto(user));
    }

    [Authorize(Roles = AdminRole)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        if (IsActiveAdmin(user) && !await HasOtherActiveAdminAsync(user.Id, ct))
        {
            return BadRequest(new { message = "A cég egyetlen aktív adminisztrátora nem törölhető." });
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static bool IsActiveAdmin(User user) => user.Role == UserRole.Admin && user.IsActive && user.EmailVerifiedAt is not null;

    private Task<bool> HasOtherActiveAdminAsync(Guid exceptUserId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id != exceptUserId && u.Role == UserRole.Admin && u.IsActive && u.EmailVerifiedAt != null, ct);

    private async Task<ObjectResult?> SendInvitationAsync(User user, CancellationToken ct)
    {
        var token = await userTokens.IssueAsync(user, UserTokenPurpose.Invitation, InvitationLifetime, ct);
        await db.SaveChangesAsync(ct);

        var inviter = await db.Users.FindAsync([User.GetUserId()], ct);
        var companyName = await db.Companies.Where(c => c.Id == user.CompanyId).Select(c => c.Name).SingleAsync(ct);
        if (!await accountEmails.SendInvitationAsync(user.Email, inviter?.FullName ?? "Egy kollégád", companyName, token, ct))
        {
            logger.LogError("Invitation e-mail could not be sent to user {UserId}: {Error}", user.Id, accountEmails.LastError);
            return StatusCode(502, new { message = "A felhasználó létrejött, de a meghívó e-mailt nem sikerült elküldeni. Próbáld újraküldeni." });
        }
        return null;
    }
}
