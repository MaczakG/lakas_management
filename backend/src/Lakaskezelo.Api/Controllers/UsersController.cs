using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Nincs szerepkör/jogosultsági megkülönböztetés — minden bejelentkezett felhasználó kezelheti a
// többi felhasználót, a felhasználó kifejezett kérése szerint.
[Authorize]
[ApiController]
[Route("api/users")]
public class UsersController(LakaskezeloDbContext db, PasswordHasher<User> passwordHasher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List(CancellationToken ct)
    {
        var users = await db.Users.OrderBy(u => u.FullName).ToListAsync(ct);
        return Ok(users.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(UpsertUserRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct))
        {
            return Conflict(new { message = "Ehhez az e-mail címhez már tartozik felhasználó." });
        }
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return BadRequest(new { message = "A jelszónak legalább 8 karakter hosszúnak kell lennie." });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            SecurityStamp = SecurityStamp.New(),
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpsertUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail && u.Id != id, ct))
        {
            return Conflict(new { message = "Ehhez az e-mail címhez már tartozik felhasználó." });
        }

        // Új jelszó, új e-mail cím (ide megy a belépési kód) vagy inaktiválás után a felhasználó
        // korábban kiadott tokenjei azonnal érvényüket vesztik (ld. User.SecurityStamp).
        var revokeSessions = user.Email != normalizedEmail || (user.IsActive && !request.IsActive);

        user.Email = normalizedEmail;
        user.FullName = request.FullName.Trim();
        user.IsActive = request.IsActive;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password.Length < 8) return BadRequest(new { message = "A jelszónak legalább 8 karakter hosszúnak kell lennie." });
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            revokeSessions = true;
        }
        if (revokeSessions)
        {
            SecurityStamp.Rotate(user);
        }

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(user));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        if (await db.Users.CountAsync(ct) <= 1)
        {
            return BadRequest(new { message = "Az utolsó felhasználó nem törölhető." });
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.FullName, user.IsActive, user.CreatedAt);
}
