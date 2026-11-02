using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Subscriptions;

// Mikor csak olvasható egy cég (nem fizetett). Tiszta függvény — a háttér-ütemező (SubscriptionScanner)
// ugyanezt állítja be a Status mezőben, de a zárolás akkor is érvényes, ha az ütemező még nem futott.
public static class SubscriptionRules
{
    public static bool IsLocked(Subscription? subscription, PlatformSettings settings, bool isOperator, DateTime now)
    {
        if (!settings.BillingEnabled || isOperator || subscription is null) return false;
        return subscription.Status switch
        {
            SubscriptionStatus.Locked => true,
            SubscriptionStatus.Trial => now > subscription.TrialEndsAt,
            SubscriptionStatus.Active => subscription.PaidUntil is { } until && now > until.AddDays(settings.GraceDays),
            SubscriptionStatus.PastDue => subscription.GraceEndsAt is { } grace && now > grace,
            _ => false,
        };
    }
}
