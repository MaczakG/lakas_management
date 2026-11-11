using Lakaskezelo.Api.ExchangeRates;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Tests;

public class CurrenciesTests
{
    [Theory]
    [InlineData("EUR", true)]
    [InlineData("GBP", true)]
    [InlineData("CHF", true)]
    [InlineData("HUF", false)]   // a forint nem deviza
    [InlineData("eur", false)]   // csak a szabványos, nagybetűs kód
    [InlineData("XXX", false)]
    [InlineData("7", false)]     // számként megadott enum-érték nem deviza
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_accepts_only_known_currency_codes(string? code, bool expected)
    {
        Assert.Equal(expected, Currencies.IsSupported(code));
    }

    [Theory]
    [InlineData("EUR", true)]
    [InlineData("USD", true)]
    [InlineData("GBP", true)]
    [InlineData("CHF", false)]
    public void Only_the_base_currencies_are_default(string code, bool expected)
    {
        Assert.Equal(expected, Currencies.IsDefault(code));
    }

    [Fact]
    public void Every_currency_has_a_name_and_the_stored_values_are_stable()
    {
        foreach (var currency in Enum.GetValues<RentCurrency>().Where(c => c != RentCurrency.HUF))
        {
            Assert.NotEqual(currency.ToString(), Currencies.Name(currency.ToString()));
        }
        // Az adatbázisban a számérték tárolódik: ezek nem változhatnak.
        Assert.Equal(0, (int)RentCurrency.HUF);
        Assert.Equal(1, (int)RentCurrency.EUR);
        Assert.Equal(2, (int)RentCurrency.USD);
        Assert.Equal(3, (int)RentCurrency.GBP);
        Assert.Equal(7, (int)RentCurrency.CHF);
    }

    [Fact]
    public void Default_currencies_are_supported()
    {
        Assert.All(Currencies.Default, c => Assert.True(Currencies.IsSupported(c)));
    }
}
