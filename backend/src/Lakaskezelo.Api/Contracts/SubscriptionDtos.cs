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
public record CheckoutRequest(BillingInterval Interval, bool AcceptImmediatePerformance);
public record CheckoutResponse(string GatewayUrl);
public record SyncPaymentRequest(string PaymentId);

public record PlatformSettingsDto(
    bool BillingEnabled, decimal MonthlyPricePerProperty, bool PricesIncludeVat, decimal YearlyDiscountPercent, int TrialDays, int GraceDays,
    decimal VatPercent, string? VatExemption, string BarionEnvironment, bool BarionPosKeySet, string? BarionPayeeEmail,
    bool BillingoApiKeySet, int? BillingoBlockId, OperatorInfoDto Operator, string TermsVersion);

public record OperatorInfoDto(string? Name, string? Address, string? TaxNumber, string? RegistrationNumber, string? Email, string? Phone);

// A titkos kulcsok (BarionPosKey, BillingoApiKey) üresen hagyva nem változnak.
public record UpdatePlatformSettingsRequest(
    bool BillingEnabled, decimal MonthlyPricePerProperty, bool PricesIncludeVat, decimal YearlyDiscountPercent, int TrialDays, int GraceDays,
    decimal VatPercent, string? VatExemption, string BarionEnvironment, string? BarionPosKey, string? BarionPayeeEmail,
    string? BillingoApiKey, int? BillingoBlockId, OperatorInfoDto? Operator, string? TermsVersion);

// Nyilvános: az ÁSZF és az adatkezelési tájékoztató kitöltéséhez.
public record LegalInfoDto(string ProductName, string WebsiteUrl, OperatorInfoDto Operator, string TermsVersion,
    decimal MonthlyPricePerProperty, bool PricesIncludeVat, decimal VatPercent, string? VatExemption, decimal YearlyDiscountPercent,
    int TrialDays, int GraceDays);

public record ExtendRequest(int Days);

public record PlatformPaymentDto(Guid Id, string CompanyName, SubscriptionPaymentKind Kind, SubscriptionPaymentStatus Status,
    BillingInterval Interval, int PropertyCount, decimal GrossAmount, DateTime CreatedAt, string? ErrorMessage, string? InvoiceUrl,
    string? BillingoError);

public record PlatformCompanyDto(Guid Id, string Name, DateTime CreatedAt, int Users, int ActiveProperties, SubscriptionStatus? Status,
    DateTime? TrialEndsAt, DateTime? PaidUntil, BillingInterval? Interval, decimal PaidTotal, bool IsOperator);
