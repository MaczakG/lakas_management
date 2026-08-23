using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

// Számla
public class Invoice
{
    public Guid Id { get; set; }
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
    public string? PdfDriveFileId { get; set; }
    public string? PdfDriveLink { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
}
