using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record InvoiceLineDto(string Label, decimal Amount);

public record InvoiceDto(
    Guid Id, Guid PropertyId, string PropertyName, string? TenantName,
    int PeriodYear, int PeriodMonth, string Number, DateTime IssuedAt, DateOnly DueDate,
    decimal AmountTotal, InvoiceStatus Status, DateTime? SentAt, string? ErrorMessage,
    List<InvoiceLineDto> Lines, DateTime? CancelledAt = null, Guid? CancelsInvoiceId = null);

public record GenerateInvoiceRequest(Guid PropertyId, int Year, int Month);

// A számla adatlapjához: a számla, a sztornózó felhasználó neve, és ugyanannak az ingatlannak ugyanarra az időszakra
// szóló többi bizonylata (az eredeti, a sztornó és az új számla) sorszám szerint.
public record InvoiceDetailDto(InvoiceDto Invoice, string? CancelledByName, string? RentConversionNote, List<InvoiceDto> Related);

public record InvoiceBundleMonthDto(int Year, int Month, int InvoiceCount, int FailedCount, decimal TotalAmount, int StornoCount = 0);
