using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record SubscriptionStatusDto(
    bool BillingEnabled, bool IsOperator, SubscriptionStatus Status, bool Locked, DateTime TrialEndsAt, DateTime? PaidUntil,
    DateTime? GraceEndsAt, bool CancelAtPeriodEnd);

public record QuoteDto(BillingInterval Interval, int PropertyCount, decimal UnitPrice, bool UnitPriceIncludesVat, decimal NetAmount, decimal VatAmount,
    decimal GrossAmount, decimal? VatPercent, string? VatExemption);

public record SubscriptionPaymentDto(Guid Id, SubscriptionPaymentKind Kind, SubscriptionPaymentStatus Status, BillingInterval Interval,
    int PropertyCount, decimal GrossAmount, DateTime PeriodStart, DateTime PeriodEnd, DateTime CreatedAt, string? ErrorMessage,
    string? InvoiceUrl, bool InvoicePending);

public record BillingDetailsDto(string? Name, string? TaxNumber, string? PostCode, string? City, string? Street, string? Email);

public record SubscriptionDetailsDto(
    SubscriptionStatusDto State, BillingInterval Interval, bool CardRegistered, BillingDetailsDto BillingDetails, int ActiveProperties,
    decimal MonthlyPricePerProperty, bool PricesIncludeVat, decimal YearlyDiscountPercent, QuoteDto MonthlyQuote, QuoteDto YearlyQuote, bool PaymentReady,
    List<SubscriptionPaymentDto> Payments);

public record UpdatePlanRequest(BillingInterval Interval, bool CancelAtPeriodEnd);
public record CheckoutRequest(BillingInterval Interval);
public record CheckoutResponse(string GatewayUrl);
public record SyncPaymentRequest(string PaymentId);

public record PlatformSettingsDto(
    bool BillingEnabled, decimal MonthlyPricePerProperty, bool PricesIncludeVat, decimal YearlyDiscountPercent, int TrialDays, int GraceDays,
    decimal VatPercent, string? VatExemption, string BarionEnvironment, bool BarionPosKeySet, string? BarionPayeeEmail,
    bool BillingoApiKeySet, int? BillingoBlockId);

// A titkos kulcsok (BarionPosKey, BillingoApiKey) üresen hagyva nem változnak.
public record UpdatePlatformSettingsRequest(
    bool BillingEnabled, decimal MonthlyPricePerProperty, bool PricesIncludeVat, decimal YearlyDiscountPercent, int TrialDays, int GraceDays,
    decimal VatPercent, string? VatExemption, string BarionEnvironment, string? BarionPosKey, string? BarionPayeeEmail,
    string? BillingoApiKey, int? BillingoBlockId);

public record PlatformCompanyDto(Guid Id, string Name, DateTime CreatedAt, int Users, int ActiveProperties, SubscriptionStatus? Status,
    DateTime? TrialEndsAt, DateTime? PaidUntil, BillingInterval? Interval, decimal PaidTotal, bool IsOperator);
