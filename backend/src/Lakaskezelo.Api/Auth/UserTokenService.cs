using System.Security.Cryptography;
using System.Text;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Auth;

// E-mailben kiküldött, egyszer használható linkek (jelszó-visszaállítás, regisztráció megerősítése,
// meghívó). A linkben a véletlen token van, az adatbázisban csak a SHA-256 hash-e.
public class UserTokenService(LakaskezeloDbContext db)
{
    // Új tokent ad a felhasználónak az adott célra; az ugyanilyen célú, még fel nem használt korábbiakat
    // eldobja (mindig csak a legutóbb kiküldött link érvényes). Nem ment — a hívó SaveChanges-e menti.
    public async Task<string> IssueAsync(User user, UserTokenPurpose purpose, TimeSpan lifetime, CancellationToken ct)
    {
        var stale = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null).ToListAsync(ct);
        db.PasswordResetTokens.RemoveRange(stale);

        var plain = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Purpose = purpose,
            Token = Hash(plain),
            ExpiresAt = DateTime.UtcNow.Add(lifetime),
            CreatedAt = DateTime.UtcNow,
        });
        return plain;
    }

    // Érvényes (nem lejárt, nem felhasznált, adott célú, aktív felhasználóhoz tartozó) token, a
    // felhasználóval együtt. Cég-kontextus nélkül hívható (a linket bejelentkezés nélkül nyitják meg).
    public async Task<PasswordResetToken?> FindValidAsync(string? plain, UserTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plain)) return null;
        var hash = Hash(plain.Trim());
        var token = await db.PasswordResetTokens.IgnoreQueryFilters().Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Token == hash, ct);
        return token is { UsedAt: null, User.IsActive: true } && token.Purpose == purpose && token.ExpiresAt > DateTime.UtcNow
            ? token
            : null;
    }

    public Task<bool> IssuedRecentlyAsync(Guid userId, UserTokenPurpose purpose, TimeSpan window, CancellationToken ct)
    {
        var since = DateTime.UtcNow.Subtract(window);
        return db.PasswordResetTokens.AnyAsync(t => t.UserId == userId && t.Purpose == purpose && t.CreatedAt > since, ct);
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
