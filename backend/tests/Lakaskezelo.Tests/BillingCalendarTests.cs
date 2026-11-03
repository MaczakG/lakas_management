using Lakaskezelo.Api.Billing;

namespace Lakaskezelo.Tests;

// A „van-e rezsi” jelzés a következő kiküldés időszakát nézi — ezt adja a BillingCalendar.NextBilling.
public class BillingCalendarTests
{
    [Theory]
    // 5-i számlázás: nov. 30-án és dec. 3-án is a decemberi számla jön (2026. dec. 5. szombat → dec. 7. hétfő).
    [InlineData("2026-11-28 10:00", 5, 2026, 12, "2026-12-07 15:00")]
    [InlineData("2026-11-30 10:00", 5, 2026, 12, "2026-12-07 15:00")]
    [InlineData("2026-12-03 10:00", 5, 2026, 12, "2026-12-07 15:00")]
    // A decemberi kiküldés után már a januári időszak a következő.
    [InlineData("2026-12-07 15:01", 5, 2027, 1, "2027-01-05 15:00")]
    // 1-jei számlázás: nov. 3-án a következő a decemberi számla, nem a novemberi.
    [InlineData("2026-11-03 09:00", 1, 2026, 12, "2026-12-01 15:00")]
    // Ha a kiküldés munkaszüneti nap miatt átcsúszik a következő hónapba, addig még az előző időszak a következő.
    [InlineData("2026-11-01 09:00", 1, 2026, 11, "2026-11-02 15:00")]
    public void Next_billing_period_is_the_period_of_the_next_send(string now, int day, int year, int month, string at)
    {
        var next = BillingCalendar.NextBilling(DateTime.Parse(now), day, 15, 0);
        Assert.Equal(new DateOnly(year, month, 1), next.Period);
        Assert.Equal(DateTime.Parse(at), next.At);
    }
}
