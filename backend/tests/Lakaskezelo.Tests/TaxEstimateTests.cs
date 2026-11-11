using Lakaskezelo.Api.Billing;

namespace Lakaskezelo.Tests;

public class TaxEstimateTests
{
    private static Dictionary<int, decimal> D(params (int Month, decimal Amount)[] items) => items.ToDictionary(i => i.Month, i => i.Amount);

    [Fact]
    public void Flat_rate_taxes_ninety_percent_of_the_income()
    {
        // 185 000 bérleti díj + 40 000 bizonylatra került rezsi
        var r = TaxEstimate.Calculate(D((3, 225_000)), D((3, 40_000)), D());
        Assert.Equal(225_000, r.Income);
        Assert.Equal(202_500, r.FlatProfit);
        Assert.Equal(30_375, r.FlatTax);
    }

    [Fact]
    public void Itemized_deducts_the_recorded_costs()
    {
        var r = TaxEstimate.Calculate(D((3, 225_000)), D((3, 40_000)), D());
        Assert.Equal(40_000, r.Costs);
        Assert.Equal(185_000, r.ItemizedProfit);
        Assert.Equal(27_750, r.ItemizedTax);
    }

    [Fact]
    public void Months_carry_their_own_numbers_and_missing_months_are_zero()
    {
        var r = TaxEstimate.Calculate(D((1, 100_000), (2, 200_000)), D((2, 50_000)), D((2, 30_000)));
        Assert.Equal(12, r.Months.Count);
        var jan = r.Months[0]; var feb = r.Months[1]; var mar = r.Months[2];
        Assert.Equal((100_000m, 0m, 90_000m, 13_500m, 100_000m, 15_000m), (jan.Income, jan.Costs, jan.FlatProfit, jan.FlatTax, jan.ItemizedProfit, jan.ItemizedTax));
        Assert.Equal((200_000m, 50_000m, 180_000m, 27_000m, 150_000m, 22_500m, 30_000m), (feb.Income, feb.Costs, feb.FlatProfit, feb.FlatTax, feb.ItemizedProfit, feb.ItemizedTax, feb.Withheld));
        Assert.Equal((0m, 0m, 0m), (mar.Income, mar.FlatTax, mar.ItemizedTax));
        Assert.Equal(30_000, r.Withheld);
    }

    [Fact]
    public void A_loss_month_has_no_tax_but_lowers_the_yearly_itemized_income()
    {
        var r = TaxEstimate.Calculate(D((1, 100_000), (2, 100_000)), D((2, 160_000)), D());
        Assert.Equal(-60_000, r.Months[1].ItemizedProfit);
        Assert.Equal(0, r.Months[1].ItemizedTax);
        // Éves: 200 000 − 160 000 = 40 000 → 6 000 (a havi adók összege 15 000 lenne).
        Assert.Equal(40_000, r.ItemizedProfit);
        Assert.Equal(6_000, r.ItemizedTax);
    }

    [Fact]
    public void A_yearly_loss_means_zero_itemized_tax()
    {
        var r = TaxEstimate.Calculate(D((1, 100_000)), D((1, 150_000)), D());
        Assert.Equal(-50_000, r.ItemizedProfit);
        Assert.Equal(0, r.ItemizedTax);
        Assert.Equal(13_500, r.FlatTax);
    }

    [Fact]
    public void Tax_is_rounded_to_whole_forints()
    {
        Assert.Equal(2, TaxEstimate.Tax(10));      // 1,5 → 2
        Assert.Equal(15, TaxEstimate.Tax(99));     // 14,85 → 15
        Assert.Equal(0, TaxEstimate.Tax(-1000));
    }
}
