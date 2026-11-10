using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.UtilityReminder;

namespace Lakaskezelo.Tests;

public class UtilityReminderContentTests
{
    private static readonly DateOnly December = new(2026, 12, 1);
    private static readonly DateTime BillingAt = new(2026, 12, 1, 8, 0, 0);

    [Theory]
    [InlineData(UtilityPeriods.Ok, 0, false)]      // minden rendben: nincs levél
    [InlineData(UtilityPeriods.Pending, 0, true)]  // a hónapban van kitöltendő
    [InlineData(UtilityPeriods.Missing, 0, true)]  // nincs rögzített rezsi
    [InlineData(UtilityPeriods.Ok, 2, true)]       // a hónap rendben, de korábbról maradt kitöltetlen
    public void Reminder_is_needed_for_incomplete_month_or_earlier_pending(string state, int earlier, bool expected)
    {
        Assert.Equal(expected, UtilityReminderContent.NeedsReminder(state, earlier));
    }

    [Fact]
    public void Mail_lists_pending_items_per_property()
    {
        var mail = UtilityReminderContent.Build(
        [
            new("Tata, Kossuth tér 4.", December, BillingAt, UtilityPeriods.Pending, ["Áram", "Víz"], [("Gáz", 2026, 10)]),
            new("Garázs <3>", December, BillingAt, UtilityPeriods.Missing, [], []),
            new("Budapest, Minta utca", December, BillingAt, UtilityPeriods.Ok, [], [("Áram", 2026, 11), ("Áram", 2026, 9)]),
        ], "https://pelda.invalid/");

        Assert.Equal("Rezsi: kitöltendő tételek", mail.Subject);
        Assert.Contains("3 ingatlannál", mail.Preheader);

        Assert.Contains("Tata, Kossuth tér 4.: 2026. december (kiállítás: 2026.12.01. 08:00)\n- Kitöltendő: Áram, Víz\n- Korábbi hónapból kitöltendő: Gáz (2026. október)", mail.Text);
        Assert.Contains("Garázs <3>: 2026. december (kiállítás: 2026.12.01. 08:00)\n- Erre a hónapra még nincs rögzített rezsi.", mail.Text);
        Assert.Contains("Budapest, Minta utca: 2026. december (kiállítás: 2026.12.01. 08:00)\n- Korábbi hónapból kitöltendő: Áram (2026. november), Áram (2026. szeptember)", mail.Text);
        Assert.Contains("A bizonylat ettől függetlenül kimegy", mail.Text);
        Assert.Contains("Rezsi felvétele: https://pelda.invalid/utility-entry.html", mail.Text);

        // A HTML-ben az ingatlan neve kódolva kerül be, és a gomb a rezsi felvételre visz.
        Assert.Contains("Gar&#225;zs &lt;3&gt;", mail.Html);
        Assert.DoesNotContain("<3>", mail.Html);
        Assert.Contains("https://pelda.invalid/utility-entry.html", mail.Html);
    }

    [Fact]
    public void Mail_without_base_url_has_no_link()
    {
        var mail = UtilityReminderContent.Build([new("Tata", December, BillingAt, UtilityPeriods.Pending, ["Áram"], [])], null);

        Assert.DoesNotContain("utility-entry.html", mail.Text);
        Assert.DoesNotContain("utility-entry.html", mail.Html);
    }
}
