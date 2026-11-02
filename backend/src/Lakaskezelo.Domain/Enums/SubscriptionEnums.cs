namespace Lakaskezelo.Domain.Enums;

// Egy cég (előfizető) előfizetésének állapota.
//  Trial    — ingyenes próbaidő (TrialEndsAt-ig), még nem fizetett.
//  Active   — kifizetett időszakban van (PaidUntil-ig), a megújítás automatikus.
//  PastDue  — a megújító terhelés nem sikerült; a türelmi idő (GraceEndsAt) végéig még használható.
//  Locked   — lejárt próbaidő / türelmi idő vagy lemondás után: csak olvasható, amíg nem fizet.
public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Locked = 3,
}

public enum BillingInterval
{
    Monthly = 0,
    Yearly = 1,
}

public enum SubscriptionPaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

// Checkout: a felhasználó a Barion fizetőoldalán fizet (és ezzel regisztrálja a kártyáját);
// Renewal: automatikus terhelés a regisztrált kártyára, felhasználó nélkül.
public enum SubscriptionPaymentKind
{
    Checkout = 0,
    Renewal = 1,
}
