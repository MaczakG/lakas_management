using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

// Egy cég előfizetése (cégenként legfeljebb egy). Díj: ingatlanonként (aktív ingatlanok száma) havi
// vagy éves díj, Barion kártyás fizetéssel; a fizetésekről Billingo-számla készül (SubscriptionPayment).
public class Subscription : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;
    // A következő (megújító vagy indító) fizetés gyakorisága — váltás a következő terheléstől érvényes.
    public BillingInterval Interval { get; set; } = BillingInterval.Monthly;
    public DateTime TrialEndsAt { get; set; }
    public DateTime? PaidUntil { get; set; }
    public DateTime? GraceEndsAt { get; set; }
    // A felhasználó lemondta: a kifizetett időszak végéig használható, utána nem terheljük.
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? LockedAt { get; set; }
    public DateTime? TrialReminderSentAt { get; set; }

    // Barion token-fizetés: az első (Barion-oldali) fizetéskor regisztrált kártya azonosítója és a
    // kártyakibocsátó TraceId-ja — a későbbi automatikus terhelések ezzel mennek.
    public string? BarionRecurrenceId { get; set; }
    public string? BarionTraceId { get; set; }
    public DateTime? CardRegisteredAt { get; set; }

    // Számlázási adatok (Billingo-számla vevője). Adószám nélkül magánszemélyként számlázunk.
    public string? BillingName { get; set; }
    public string? BillingTaxNumber { get; set; }
    public string? BillingPostCode { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingStreet { get; set; }
    public string? BillingEmail { get; set; }
    public int? BillingoPartnerId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
