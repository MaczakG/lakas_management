using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record InvoiceLineDto(string Label, decimal Amount);

public record InvoiceDto(
    Guid Id, Guid PropertyId, string PropertyName, string? TenantName,
    int PeriodYear, int PeriodMonth, string Number, DateTime IssuedAt, DateOnly DueDate,
    decimal AmountTotal, InvoiceStatus Status, string? PdfDriveLink, DateTime? SentAt, string? ErrorMessage,
    List<InvoiceLineDto> Lines);

public record GenerateInvoiceRequest(Guid PropertyId, int Year, int Month);
