using Lakaskezelo.Api.Billing;

namespace Lakaskezelo.Tests;

public class UtilityPeriodsTests
{
    [Theory]
    // Se rezsi tétel, se rögzített tétel: a rezsit kézzel kell felvenni.
    [InlineData(0, 0, false, 0, 0, UtilityPeriods.Missing)]
    // Kézzel rögzített tételek (rezsi tételek nélkül): rendben, mint eddig.
    [InlineData(2, 0, false, 0, 0, UtilityPeriods.Ok)]
    // Még nincs előkészítve, de csak fix rezsi tételek vannak: maguktól bekerülnek.
    [InlineData(0, 0, false, 3, 0, UtilityPeriods.Ok)]
    // Még nincs előkészítve, és nem fix tétel is van: azt ki kell majd tölteni.
    [InlineData(0, 0, false, 3, 1, UtilityPeriods.Pending)]
    // Előkészítve, van kitöltendő tétel.
    [InlineData(3, 1, true, 3, 1, UtilityPeriods.Pending)]
    // Előkészítve, minden kitöltve.
    [InlineData(3, 0, true, 3, 1, UtilityPeriods.Ok)]
    // Előkészítve, de minden tételt töröltek: nincs mit számlázni, nem hiányzik semmi.
    [InlineData(0, 0, true, 3, 1, UtilityPeriods.Ok)]
    // Kézzel felvett, de még összeg nélküli tétel is kitöltendő.
    [InlineData(1, 1, false, 0, 0, UtilityPeriods.Pending)]
    public void State_of_a_month(int entries, int pending, bool prepared, int items, int nonFixed, string expected)
    {
        Assert.Equal(expected, UtilityPeriods.State(entries, pending, prepared, items, nonFixed));
    }
}

public class UtilityAutoPrepareTests
{
    // 2026. november a következő bizonylat hónapja; az ablak 36 hónappal korábban kezdődik.
    private const int Next = 2026 * 12 + 11;
    private const int Window = Next - 36;

    [Fact]
    public void An_empty_unbilled_month_with_a_tenant_is_prepared_when_opened()
    {
        Assert.True(UtilityPeriods.ShouldAutoPrepare(false, false, true, Next, Next, Window));
        Assert.True(UtilityPeriods.ShouldAutoPrepare(false, false, true, Next - 1, Next, Window));
    }

    [Fact]
    public void A_month_that_already_has_entries_is_left_alone()
    {
        Assert.False(UtilityPeriods.ShouldAutoPrepare(true, false, true, Next, Next, Window));
    }

    [Fact]
    public void A_month_with_an_issued_invoice_is_left_alone()
    {
        Assert.False(UtilityPeriods.ShouldAutoPrepare(false, true, true, Next - 2, Next, Window));
    }

    [Fact]
    public void A_month_without_a_tenant_is_left_alone()
    {
        Assert.False(UtilityPeriods.ShouldAutoPrepare(false, false, false, Next - 5, Next, Window));
    }

    [Fact]
    public void Months_after_the_next_invoice_or_before_the_window_are_left_alone()
    {
        Assert.False(UtilityPeriods.ShouldAutoPrepare(false, false, true, Next + 1, Next, Window));
        Assert.True(UtilityPeriods.ShouldAutoPrepare(false, false, true, Window, Next, Window));
        Assert.False(UtilityPeriods.ShouldAutoPrepare(false, false, true, Window - 1, Next, Window));
    }
}

public class LateUtilityTests
{
    private static readonly Guid Property = Guid.NewGuid();

    private static Lakaskezelo.Domain.Entities.UtilityCostEntry Entry(int year, int month, Guid? property = null) =>
        new() { Id = Guid.NewGuid(), PropertyId = property ?? Property, Year = year, Month = month, Amount = 1000 };

    [Fact]
    public void An_entry_of_an_already_invoiced_month_is_late()
    {
        var invoiced = new HashSet<(Guid, int, int)> { (Property, 2026, 8) };
        Assert.True(Lakaskezelo.Api.Billing.TenantBalances.IsLate(Entry(2026, 8), invoiced));
    }

    [Fact]
    public void An_entry_of_a_month_without_invoice_is_not_late()
    {
        var invoiced = new HashSet<(Guid, int, int)> { (Property, 2026, 8) };
        Assert.False(Lakaskezelo.Api.Billing.TenantBalances.IsLate(Entry(2026, 9), invoiced));
        // másik ingatlan ugyanazon hónapjának bizonylata nem számít
        Assert.False(Lakaskezelo.Api.Billing.TenantBalances.IsLate(Entry(2026, 8, Guid.NewGuid()), invoiced));
    }
}
