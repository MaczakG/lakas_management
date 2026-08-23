using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record PropertyListItemDto(
    Guid Id, string Name, decimal RentAmount, RentCurrency RentCurrency, bool IsActive,
    string OwnerNames, string TenantNames,
    bool UtilityComplete, int BillingDayOfMonth, int BillingHour, int BillingMinute);

public record PropertyDetailDto(
    Guid Id, string Name, decimal RentAmount, string? DriveFolderId, string? InvoicePrefix,
    int BillingDayOfMonth, int BillingHour, int BillingMinute, bool IsActive,
    List<PropertyOwnerDto> Owners, List<TenantDto> Tenants);

public record PropertyOwnerDto(Guid OwnerId, string OwnerName);

public record UpsertPropertyRequest(
    string Name, decimal RentAmount, string? DriveFolderId, string? InvoicePrefix,
    int BillingDayOfMonth, int BillingHour, int BillingMinute, bool IsActive,
    List<Guid> OwnerIds);

public record UtilityCostEntryDto(Guid Id, int Year, int Month, string Label, decimal Amount, DateTime CreatedAt, bool IsLocked);
public record UpsertUtilityCostEntryRequest(int Year, int Month, string Label, decimal Amount);

public record DriveDocumentDto(string Id, string Name, string? WebViewLink, DateTime? CreatedAt, long? SizeBytes);
