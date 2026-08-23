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

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Generated;
    public string? PdfDriveFileId { get; set; }
    public string? PdfDriveLink { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
}
