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
    List<UpcomingEvent> Upcoming,
    // Ingatlanválasztó: az összes ingatlan (aktívak elöl), a kiválasztott (null = mind), és egy ingatlannál a mai bérlő.
    List<DashboardProperty> Properties, Guid? PropertyId = null, string? CurrentTenantName = null,
    // Egyenleg (teljes időszak, nem az évre szűrve): várt = kiállított bizonylatok + bizonylatra váró rezsi,
    // kifizetett = befizetések; az egyenleg a kettő különbsége. Ugyanaz a számítás, mint a bérlők egyenlegénél.
    decimal BalanceExpected = 0, decimal BalancePaid = 0, List<PropertyBalance>? BalanceByProperty = null,
    // Adózási kimutatás (ld. Billing.TaxEstimate); cégnél null, mert csak magánszemély bérbeadóra értelmezett.
    Lakaskezelo.Api.Billing.TaxEstimate.Result? Tax = null);

public record PropertyBalance(Guid PropertyId, string Name, decimal Expected, decimal Paid);

public record DashboardProperty(Guid Id, string Name, bool IsActive);
