using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

// Egyszer használható, e-mailben kiküldött link tokenje (csak a SHA-256 hash-e tárolódik). A neve
// történeti: ma már a regisztráció megerősítését és a meghívást is ez hordozza (Purpose).

public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public UserTokenPurpose Purpose { get; set; } = UserTokenPurpose.PasswordReset;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
