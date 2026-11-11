using Lakaskezelo.Api.LeaseExpiry;

namespace Lakaskezelo.Tests;

public class LeaseExpiryContentTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    [Fact]
    public void IsDue_only_inside_the_notice_window()
    {
        Assert.False(LeaseExpiryContent.IsDue(Today.AddDays(31), null, null, Today, 30));
        Assert.True(LeaseExpiryContent.IsDue(Today.AddDays(30), null, null, Today, 30));
        Assert.True(LeaseExpiryContent.IsDue(Today, null, null, Today, 30));
    }

    [Fact]
    public void IsDue_for_an_already_expired_lease_that_was_never_reported()
    {
        Assert.True(LeaseExpiryContent.IsDue(Today.AddDays(-10), null, null, Today, 30));
    }

    [Fact]
    public void IsDue_is_false_without_a_lease_end_date()
    {
        Assert.False(LeaseExpiryContent.IsDue(null, null, null, Today, 30));
    }

    [Fact]
    public void IsDue_reports_one_date_once_and_a_changed_date_again()
    {
        var end = Today.AddDays(20);
        Assert.False(LeaseExpiryContent.IsDue(end, end, null, Today, 30));
        // Hosszabbítás: az új dátum még messze van, aztán a határon belülre kerül.
        var extended = end.AddYears(1);
        Assert.False(LeaseExpiryContent.IsDue(extended, end, null, Today, 30));
        Assert.True(LeaseExpiryContent.IsDue(extended, end, null, extended.AddDays(-30), 30));
    }

    [Fact]
    public void IsDue_skips_tenants_who_moved_out()
    {
        var end = Today.AddDays(5);
        Assert.False(LeaseExpiryContent.IsDue(end, null, Today.AddDays(-1), Today, 30));
        Assert.False(LeaseExpiryContent.IsDue(end, null, Today, Today, 30));
        Assert.True(LeaseExpiryContent.IsDue(end, null, Today.AddDays(3), Today, 30));
    }

    [Theory]
    [InlineData(45, "45 nap múlva jár le")]
    [InlineData(0, "ma jár le")]
    [InlineData(-12, "12 napja lejárt")]
    public void When_describes_the_distance_in_days(int offset, string expected)
    {
        Assert.Equal(expected, LeaseExpiryContent.When(Today.AddDays(offset), Today));
    }

    [Fact]
    public void Build_lists_the_leases_by_date_with_a_link_to_the_tenants()
    {
        var mail = LeaseExpiryContent.Build(
            [
                new("Kiss Anna", "Tata", Today.AddDays(20)),
                new("Nagy Bence", null, Today.AddDays(-3)),
            ], Today, "https://app.example/");

        Assert.Equal(LeaseExpiryContent.Subject, mail.Subject);
        Assert.Contains("2 ", mail.Preheader);
        var first = mail.Text.IndexOf("Nagy Bence: 2026.11.08., 3 napja", StringComparison.Ordinal);
        var second = mail.Text.IndexOf("Kiss Anna (Tata): 2026.12.01., 20 nap", StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first);
        Assert.Contains("https://app.example/tenants.html", mail.Text);
        Assert.Contains("https://app.example/tenants.html", mail.Html);
        Assert.Contains("Kiss Anna (Tata)", mail.Html);
    }

    [Fact]
    public void Build_with_one_lease_puts_it_into_the_preheader_and_works_without_base_url()
    {
        var mail = LeaseExpiryContent.Build([new("Kiss Anna", "Tata", Today)], Today, null);
        Assert.Contains("Kiss Anna (Tata)", mail.Preheader);
        Assert.Contains("ma jár le", mail.Text);
        Assert.DoesNotContain("tenants.html", mail.Text);
    }
}
