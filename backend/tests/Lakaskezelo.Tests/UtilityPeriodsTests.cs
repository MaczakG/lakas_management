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

public class UtilityPrepareAheadTests
{
    private const int Floor = 2026 * 12 + 4;    // az ingatlan első bizonylata: 2026. április
    private const int Window = 2023 * 12 + 12;

    [Fact]
    public void An_untouched_month_with_tenant_from_the_floor_on_is_prepared()
    {
        Assert.True(UtilityPeriods.ShouldPrepareAhead(false, false, false, true, Floor, Floor, Window));
        Assert.True(UtilityPeriods.ShouldPrepareAhead(false, false, false, true, 2027 * 12 + 12, Floor, Window));
    }

    [Fact]
    public void Months_before_the_first_invoice_are_not_prepared()
    {
        Assert.False(UtilityPeriods.ShouldPrepareAhead(false, false, false, true, Floor - 1, Floor, Window));
    }

    [Theory]
    [InlineData(true, false, false, true)]   // már elő volt készítve (a törölt tétel nem jön vissza)
    [InlineData(false, true, false, true)]   // van benne tétel
    [InlineData(false, false, true, true)]   // van rá érvényes bizonylat
    [InlineData(false, false, false, false)] // nincs bérlő a hónapban
    public void Unsafe_months_are_left_alone(bool prepared, bool hasEntries, bool hasInvoice, bool tenant)
    {
        Assert.False(UtilityPeriods.ShouldPrepareAhead(prepared, hasEntries, hasInvoice, tenant, Floor + 1, Floor, Window));
    }

    private static Lakaskezelo.Domain.Entities.UtilityCostEntry Entry(string label = "Közös költség", decimal amount = 15000, bool forward = false) =>
        new() { Id = Guid.NewGuid(), Label = label, Amount = amount, ForwardBilled = forward };

    [Fact]
    public void An_entry_matching_the_item_is_untouched()
    {
        Assert.True(UtilityPeriods.IsUntouched(Entry(), false, "Közös költség", 15000, false));
    }

    [Fact]
    public void An_edited_filled_or_billed_entry_is_not_untouched()
    {
        Assert.False(UtilityPeriods.IsUntouched(Entry(amount: 16000), false, "Közös költség", 15000, false));
        Assert.False(UtilityPeriods.IsUntouched(Entry(label: "Közös ktg."), false, "Közös költség", 15000, false));
        Assert.False(UtilityPeriods.IsUntouched(Entry(forward: true), false, "Közös költség", 15000, false));
        Assert.False(UtilityPeriods.IsUntouched(Entry(), true, "Közös költség", 15000, false));   // van csatolmánya
        var noted = Entry(); noted.Note = "egyeztetve";
        Assert.False(UtilityPeriods.IsUntouched(noted, false, "Közös költség", 15000, false));
        var billed = Entry(); billed.InvoiceId = Guid.NewGuid();
        Assert.False(UtilityPeriods.IsUntouched(billed, false, "Közös költség", 15000, false));
    }

    [Fact]
    public void Expected_amount_is_the_fixed_amount_or_zero()
    {
        Assert.Equal(15000, UtilityPeriods.ExpectedAmount(true, 15000));
        Assert.Equal(0, UtilityPeriods.ExpectedAmount(false, 15000));
        Assert.Equal(0, UtilityPeriods.ExpectedAmount(true, null));
    }
}

public class UtilityBillingTimingTests
{
    private static readonly Guid Property = Guid.NewGuid();
    private static Lakaskezelo.Domain.Entities.Tenant Tenant(Lakaskezelo.Domain.Enums.UtilityBillingTiming timing) => new() { Id = Guid.NewGuid(), Name = "T", UtilityBillingTiming = timing };

    [Fact]
    public void By_default_the_invoice_carries_its_own_months_utilities()
    {
        Assert.Equal((2026, 12), InvoiceGenerationService.UtilityPeriod(Tenant(Lakaskezelo.Domain.Enums.UtilityBillingTiming.SameMonth), 2026, 12));
        Assert.Equal((2026, 12), InvoiceGenerationService.UtilityPeriod(null, 2026, 12));
    }

    [Fact]
    public void With_one_month_lag_the_invoice_carries_the_previous_months_utilities()
    {
        var lagged = Tenant(Lakaskezelo.Domain.Enums.UtilityBillingTiming.PreviousMonth);
        Assert.Equal((2026, 11), InvoiceGenerationService.UtilityPeriod(lagged, 2026, 12));
        Assert.Equal((2025, 12), InvoiceGenerationService.UtilityPeriod(lagged, 2026, 1));
        Assert.Equal((2025, 12), UtilityPeriods.UtilityPeriodOf(new DateOnly(2026, 1, 1), 1));
        Assert.Equal((2026, 1), UtilityPeriods.UtilityPeriodOf(new DateOnly(2026, 1, 1), 0));
    }

    [Fact]
    public void With_lag_this_months_utilities_are_late_only_once_next_months_invoice_exists()
    {
        var entry = new Lakaskezelo.Domain.Entities.UtilityCostEntry { Id = Guid.NewGuid(), PropertyId = Property, Year = 2026, Month = 11, Amount = 1000 };
        var november = new HashSet<(Guid, int, int)> { (Property, 2026, 11) };
        Assert.True(TenantBalances.IsLate(entry, november));          // alap: a novemberi bizonylat már megvan → utólagos
        Assert.False(TenantBalances.IsLate(entry, november, lag: 1)); // csúszás: a decemberi bizonylat viszi → még nem az
        var december = new HashSet<(Guid, int, int)> { (Property, 2026, 11), (Property, 2026, 12) };
        Assert.True(TenantBalances.IsLate(entry, december, lag: 1));
        var yearEnd = new Lakaskezelo.Domain.Entities.UtilityCostEntry { Id = Guid.NewGuid(), PropertyId = Property, Year = 2026, Month = 12, Amount = 1000 };
        Assert.True(TenantBalances.IsLate(yearEnd, new HashSet<(Guid, int, int)> { (Property, 2027, 1) }, lag: 1));
    }
}
