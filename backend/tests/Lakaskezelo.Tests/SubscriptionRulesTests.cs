using Lakaskezelo.Api.Subscriptions;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Tests;

public class PricingTests
{
    private static PlatformSettings Settings(decimal price = 990m, decimal discount = 15m, decimal vat = 27m, string? exemption = null) =>
        new() { MonthlyNetPricePerProperty = price, YearlyDiscountPercent = discount, VatPercent = vat, VatExemption = exemption };

    [Fact]
    public void Monthly_price_is_per_active_property_with_vat()
    {
        var q = Pricing.Quote(Settings(), 3, BillingInterval.Monthly);
        Assert.Equal(990m, q.UnitNetPrice);
        Assert.Equal(2970m, q.NetAmount);
        Assert.Equal(802m, q.VatAmount); // 2970 × 27% = 801,9 → 802
        Assert.Equal(3772m, q.GrossAmount);
    }

    [Fact]
    public void At_least_one_property_is_billed()
    {
        Assert.Equal(1, Pricing.Quote(Settings(), 0, BillingInterval.Monthly).PropertyCount);
    }

    [Fact]
    public void Yearly_price_is_twelve_months_with_discount_rounded_to_whole_forints()
    {
        var q = Pricing.Quote(Settings(), 2, BillingInterval.Yearly);
        Assert.Equal(10098m, q.UnitNetPrice); // 990 × 12 × 0,85
        Assert.Equal(20196m, q.NetAmount);
        Assert.Equal(25649m, q.GrossAmount); // 20196 × 1,27 = 25648,92 → 25649
    }

    [Fact]
    public void Vat_exemption_means_no_vat()
    {
        var q = Pricing.Quote(Settings(exemption: "AAM"), 2, BillingInterval.Monthly);
        Assert.Null(q.VatPercent);
        Assert.Equal("AAM", q.VatExemption);
        Assert.Equal(q.NetAmount, q.GrossAmount);
    }

    [Fact]
    public void Period_end_follows_interval()
    {
        var start = new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 2, 28, 10, 0, 0, DateTimeKind.Utc), Pricing.PeriodEnd(start, BillingInterval.Monthly));
        Assert.Equal(new DateTime(2027, 1, 31, 10, 0, 0, DateTimeKind.Utc), Pricing.PeriodEnd(start, BillingInterval.Yearly));
    }
}

public class SubscriptionRulesTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PlatformSettings Enabled = new() { BillingEnabled = true, GraceDays = 7 };

    [Fact]
    public void Nothing_is_locked_while_billing_is_off_or_for_the_operator()
    {
        var expired = new Subscription { Status = SubscriptionStatus.Locked };
        Assert.False(SubscriptionRules.IsLocked(expired, new PlatformSettings { BillingEnabled = false }, false, Now));
        Assert.False(SubscriptionRules.IsLocked(expired, Enabled, isOperator: true, Now));
    }

    [Fact]
    public void Trial_locks_when_it_ends()
    {
        Assert.False(SubscriptionRules.IsLocked(new Subscription { Status = SubscriptionStatus.Trial, TrialEndsAt = Now.AddHours(1) }, Enabled, false, Now));
        Assert.True(SubscriptionRules.IsLocked(new Subscription { Status = SubscriptionStatus.Trial, TrialEndsAt = Now.AddHours(-1) }, Enabled, false, Now));
    }

    [Fact]
    public void Active_locks_only_after_paid_period_plus_grace_even_if_scanner_did_not_run()
    {
        Assert.False(SubscriptionRules.IsLocked(new Subscription { Status = SubscriptionStatus.Active, PaidUntil = Now.AddDays(-6) }, Enabled, false, Now));
        Assert.True(SubscriptionRules.IsLocked(new Subscription { Status = SubscriptionStatus.Active, PaidUntil = Now.AddDays(-8) }, Enabled, false, Now));
    }

    [Fact]
    public void Past_due_locks_after_grace()
    {
        Assert.False(SubscriptionRules.IsLocked(new Subscription { Status = SubscriptionStatus.PastDue, GraceEndsAt = Now.AddDays(1) }, Enabled, false, Now));
        Assert.True(SubscriptionRules.IsLocked(new Subscription { Status = SubscriptionStatus.PastDue, GraceEndsAt = Now.AddDays(-1) }, Enabled, false, Now));
        Assert.True(SubscriptionRules.IsLocked(new Subscription { Status = SubscriptionStatus.Locked }, Enabled, false, Now));
    }
}
