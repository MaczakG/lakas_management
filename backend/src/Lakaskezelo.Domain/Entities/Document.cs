namespace Lakaskezelo.Domain.Entities;

// Feltöltött dokumentum (szerződés, szkennelt rezsi számla, jegyzőkönyv, fotó…). Ingatlanhoz, bérlőhöz és/vagy
// tulajdonoshoz kapcsolódik: mindegyik adatlap a hozzá kapcsolt dokumentumokat mutatja (pl. egy bérleti
// szerződés az ingatlannál és a bérlőnél is látszik). Legalább egy kapcsolat kötelező.
// A fájl tartalma S3-ban van (StorageKey), ha a platformon be van állítva tárhely; különben az adatbázisban
// (DocumentContent).
public class Document : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    public Guid? PropertyId { get; set; }
    public Property? Property { get; set; }
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid? OwnerId { get; set; }
    public Owner? Owner { get; set; }

    // Megjelenített név (alapból a fájlnév kiterjesztés nélkül), és az eredeti fájlnév a letöltéshez.
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Category { get; set; } = DocumentCategories.Other;
    public string? Note { get; set; }

    public string? StorageKey { get; set; }
    public DocumentContent? Content { get; set; }

    public DateTime UploadedAt { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public User? UploadedBy { get; set; }
}

// A fájl tartalma, ha nincs S3 tárhely. Külön táblában, hogy a listázás ne húzza be a fájlokat.
public class DocumentContent : ICompanyOwned
{
    public Guid DocumentId { get; set; }
    public Guid CompanyId { get; set; }
    public byte[] Data { get; set; } = [];
}

public static class DocumentCategories
{
    public const string Contract = "Contract";
    public const string UtilityBill = "UtilityBill";
    public const string Handover = "Handover";
    public const string Photo = "Photo";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Contract, UtilityBill, Handover, Photo, Other };
}
