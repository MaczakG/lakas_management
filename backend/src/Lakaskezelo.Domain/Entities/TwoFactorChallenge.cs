namespace Lakaskezelo.Domain.Entities;

// Sikeres jelszó-ellenőrzés után jön létre (ld. AuthController.Login) — az Id-t adjuk vissza a
// kliensnek "challengeId"-ként, a hozzá tartozó 6 jegyű kódot csak e-mailben kapja meg a
// felhasználó. A CodeHash-ben csak a kód SHA-256 hash-e van tárolva, ugyanúgy, mint a
// PasswordResetToken-nél.
public class TwoFactorChallenge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
