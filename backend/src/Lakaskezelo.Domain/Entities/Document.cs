namespace Lakaskezelo.Domain.Entities;

// Feltöltött dokumentum (szerződés, szkennelt rezsi számla, jegyzőkönyv, fotó…). Ingatlanhoz, bérlőhöz és/vagy
// tulajdonoshoz kapcsolódik: mindegyik adatlap a hozzá kapcsolt dokumentumokat mutatja (pl. egy bérleti
// szerződés az ingatlannál és a bérlőnél is látszik). Legalább egy kapcsolat kötelező.
// A fájl verziókban él (DocumentVersion); a FileName/ContentType/SizeBytes a jelenlegi (legutóbbi) verzióé,
// hogy a listázáshoz ne kelljen a verziókat betölteni.
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

    // Cégenként szerkeszthető dokumentumtípus; null, ha nincs megadva (vagy a típust törölték).
    public Guid? DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int CurrentVersion { get; set; } = 1;

    public DateTime UploadedAt { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public User? UploadedBy { get; set; }

    public List<DocumentVersion> Versions { get; set; } = [];
    public List<DocumentEvent> Events { get; set; } = [];
}

// Egy dokumentum egy feltöltött fájlja. A tartalom S3-ban van (StorageKey), ha a platformon be van állítva
// tárhely; különben az adatbázisban (DocumentContent).
public class DocumentVersion : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }
    public int Number { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public DocumentContent? Content { get; set; }
    public DateTime UploadedAt { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public User? UploadedBy { get; set; }
}

// A verzió fájlja, ha nincs S3 tárhely. Külön táblában, hogy a listázás ne húzza be a fájlokat.
public class DocumentContent : ICompanyOwned
{
    public Guid VersionId { get; set; }
    public Guid CompanyId { get; set; }
    public byte[] Data { get; set; } = [];
}

// Napló: ki mikor töltötte fel, nézte meg, töltötte le vagy módosította a dokumentumot.
public class DocumentEvent : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? VersionNumber { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public DateTime At { get; set; }
}

public static class DocumentEventTypes
{
    public const string Uploaded = "Uploaded";
    public const string NewVersion = "NewVersion";
    public const string Viewed = "Viewed";
    public const string Downloaded = "Downloaded";
    public const string Updated = "Updated";
}

// Cégenként szerkeszthető dokumentumtípus (Szerződés, Rezsi számla…).
public class DocumentType : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    // Új cég alapértelmezett típusai (a meglévő cégeknek a DocumentTypes migráció hozza létre ugyanezeket).
    public static readonly string[] Defaults = ["Szerződés", "Rezsi számla", "Jegyzőkönyv", "Fotó", "Egyéb"];

    public static IEnumerable<DocumentType> DefaultsFor(Guid companyId, DateTime now) =>
        Defaults.Select((name, i) => new DocumentType { Id = Guid.NewGuid(), CompanyId = companyId, Name = name, SortOrder = i, CreatedAt = now });
}
