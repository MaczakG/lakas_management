namespace Lakaskezelo.Api.Contracts;

// Vezérlőpult (GET /api/dashboard) — ld. DashboardController.
public record StatusSummary(int Count, decimal Amount);
public record MonthAmount(int Month, decimal Amount, decimal PreviousAmount, int Count);
public record PropertyAmount(Guid PropertyId, string Name, decimal Amount, int Count);
public record UtilityMonth(int Month, List<decimal> Values);
public record UpcomingEvent(DateOnly Date, string Kind, Guid PropertyId, string PropertyName, Guid? TenantId, string? TenantName);

public record DashboardDto(
    int Year, List<int> Years, int CompareUntilMonth,
    decimal InvoicedTotal, int InvoiceCount, decimal PreviousYearToDate,
    StatusSummary Sent, StatusSummary Generated, StatusSummary Failed,
    List<MonthAmount> Monthly, List<PropertyAmount> ByProperty,
    decimal UtilityTotal, List<string> UtilityCategories, List<UtilityMonth> UtilityMonthly,
    int ActiveProperties, int RentedProperties,
    List<UpcomingEvent> Upcoming);
