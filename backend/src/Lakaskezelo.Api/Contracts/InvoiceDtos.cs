using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record InvoiceLineDto(string Label, decimal Amount);

public record InvoiceDto(
    Guid Id, Guid PropertyId, string PropertyName, string? TenantName,
    int PeriodYear, int PeriodMonth, string Number, DateTime IssuedAt, DateOnly DueDate,
    decimal AmountTotal, InvoiceStatus Status, DateTime? SentAt, string? ErrorMessage,
    List<InvoiceLineDto> Lines, DateTime? CancelledAt = null, Guid? CancelsInvoiceId = null, string? Provider = null,
    DateOnly? PaidAt = null, decimal PaidAmount = 0,
    // A rezsitételek csatolmányai: hány tartozott a bizonylathoz, és hány ment ki az e-maillel.
    int AttachmentsAvailable = 0, int AttachmentsSent = 0);

public record InvoicePaymentRequest(DateOnly Date, decimal Amount, string? Note = null);

// SendAttachments: a felhasználó válasza arra, menjenek-e a rezsitételek csatolmányai a levéllel (csak akkor számít,
// ha a bérlőnél nincs beállítva).
public record GenerateInvoiceRequest(Guid PropertyId, int Year, int Month, bool? SendAttachments = null);

// Kézi bizonylatkészítés előtt: a levéllel küldhető csatolmányok, és a bérlő beállítása (Ask: rá kell kérdezni).
public record InvoiceAttachmentPlanDto(string? TenantName, InvoiceAttachmentPreference Preference, bool WillEmail, List<InvoiceAttachmentDto> Attachments);

public record InvoiceAttachmentDto(Guid DocumentId, string Name, string FileName, long SizeBytes, string EntryLabel);

// A számla adatlapjához: a számla, a sztornózó felhasználó neve, és ugyanannak az ingatlannak ugyanarra az időszakra
// szóló többi bizonylata (az eredeti, a sztornó és az új számla) sorszám szerint.
public record InvoiceDetailDto(InvoiceDto Invoice, string? CancelledByName, string? RentConversionNote, List<InvoiceDto> Related,
    List<TenantPaymentDto> Payments);

public record InvoiceBundleMonthDto(int Year, int Month, int InvoiceCount, int FailedCount, decimal TotalAmount, int StornoCount = 0);
