namespace Lakaskezelo.Domain.Entities;

// A platform üzemeltetőjének fiókja — teljesen külön a cégek felhasználóitól (User): saját belépés
// (/platform-login.html), saját jelszó, kétlépcsős kód és zárolás. Nem tartozik céghez, és a cégek
// adataihoz sem fér hozzá, csak a platform-végpontokhoz (/api/platform).
public class PlatformAdmin
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    // Üres, amíg az első jelszót be nem állítja (a jelszó-visszaállító linkkel).
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string SecurityStamp { get; set; } = string.Empty;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum PlatformAdminTokenKind
{
    TwoFactorCode = 0,
    PasswordReset = 1,
}

// Egyszer használható belépési kód vagy jelszó-visszaállító link egy platform-admin számára (csak a hash-e).
public class PlatformAdminToken
{
    public Guid Id { get; set; }
    public Guid AdminId { get; set; }
    public PlatformAdmin? Admin { get; set; }
    public PlatformAdminTokenKind Kind { get; set; }
    public string Hash { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
