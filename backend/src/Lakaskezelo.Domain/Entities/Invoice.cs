using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

// Számla
public class Invoice : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal AmountTotal { get; set; }

    // Csak akkor van értéke, ha a bérlő nem HUF-alapú bérleti díjat állított be — a PDF-en
    // lábjegyzetként jelenik meg, hogy az átváltás átlátható legyen (a bizonylat sablonja maga
    // csak a végleges HUF összeget mutatja, ld. InvoicePdfGenerator).
    public string? RentConversionNote { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Generated;
    // Az S3-ban tárolt PDF kulcsa; null, ha a feltöltés nem sikerült vagy a számla még a Drive-os
    // időszakból való — ilyenkor a PDF letöltéskor a tárolt adatokból újragenerálódik.
    public string? PdfStorageKey { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }

    // Sztornózás: kiállított bizonylat nem törölhető és nem archiválható; helyette sztornó bizonylat készül
    // (negatív összegekkel, az eredetire hivatkozva), és mindkettő megmarad.
    // Az eredeti bizonylaton: mikor és ki sztornózta (sztornózott bizonylat).
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    // A sztornó bizonylaton: melyik bizonylatot sztornózza.
    public Guid? CancelsInvoiceId { get; set; }
    public Invoice? CancelsInvoice { get; set; }

    // Sztornó bizonylat-e. (Aktív bizonylat: nem sztornó, és nem is sztornózták — ilyenből egy lehet időszakonként.)
    public bool IsStorno => CancelsInvoiceId != null;

    public List<InvoiceLine> Lines { get; set; } = [];
}
