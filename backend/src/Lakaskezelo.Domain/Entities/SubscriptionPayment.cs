using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

// Egy előfizetési díj-terhelés (Barion) és a róla kiállított Billingo-számla.
public class SubscriptionPayment : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    public SubscriptionPaymentKind Kind { get; set; }
    public SubscriptionPaymentStatus Status { get; set; } = SubscriptionPaymentStatus.Pending;
    public BillingInterval Interval { get; set; }

    // A díj alapja: ennyi aktív ingatlan × ingatlanonkénti egységár (Ft) az időszakra.
    public int PropertyCount { get; set; }
    // Ingatlanonkénti egységár az időszakra; UnitPriceIncludesVat: bruttó (ÁFÁ-val) vagy nettó.
    public decimal UnitPrice { get; set; }
    public bool UnitPriceIncludesVat { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal? VatPercent { get; set; }
    public string? VatExemption { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public string? BarionPaymentId { get; set; }
    public string? RecurrenceId { get; set; }
    public string? ErrorMessage { get; set; }

    public int? BillingoDocumentId { get; set; }
    public string? BillingoPublicUrl { get; set; }
    public string? BillingoError { get; set; }
    public int BillingoAttempts { get; set; }

    // A fizető kifejezett kérése a szolgáltatás azonnali megkezdésére (fogyasztói elállási jog, 45/2014. Korm. r.).
    public DateTime? ImmediatePerformanceConsentAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
