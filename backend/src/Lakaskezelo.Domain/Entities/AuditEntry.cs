namespace Lakaskezelo.Domain.Entities;

// Felhasználói napló (Beállítások → Napló): ki, mikor, mit csinált. Az adatmódosításokat a mentés (LakaskezeloDbContext)
// rögzíti automatikusan, mezőnként a régi és az új értékkel; a bejelentkezést az AuthController. A felhasználó nevét
// is eltároljuk, hogy a törölt felhasználó sorai is olvashatók maradjanak.
public class AuditEntry : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime At { get; set; }
    // null: a rendszer (pl. az ütemezett bizonylat-kiküldés).
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    // AuditActions: Created / Updated / Deleted / Login.
    public string Action { get; set; } = string.Empty;
    // Az entitás típusa (pl. "Tenant"), az azonosítója és a mentéskori megnevezése (pl. a bérlő neve).
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? EntityLabel { get; set; }
    // Az érintett ingatlan (ha van), a szűréshez és a megjelenítéshez.
    public Guid? PropertyId { get; set; }
    // A változások JSON-ban: [{"f": mező, "o": régi, "n": új}] (szövegként, a titkos mezők értéke nélkül).
    public string? Changes { get; set; }
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    public const string Login = "Login";
}
