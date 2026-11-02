using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Subscriptions;

// Egy időszak díja: aktív ingatlanok száma (legalább 1) × ingatlanonkénti egységár. Éves fizetésnél az
// egységár 12 havi díj, a kedvezménnyel csökkentve. Minden összeg egész forint.
//  - Bruttó ár (PricesIncludeVat): a terhelt összeg = egységár × ingatlanszám pontosan; a nettó és az ÁFA
//    ebből adódik (a Billingo-számla is bruttó egységárral készül, így a végösszeg egyezik).
//  - Nettó ár: bruttó = nettó × (1 + ÁFA), ugyanúgy kerekítve, mint a Billingo.
public record PriceQuote(
    BillingInterval Interval, int PropertyCount, decimal UnitPrice, bool UnitPriceIncludesVat, decimal NetAmount,
    decimal VatAmount, decimal GrossAmount, decimal? VatPercent, string? VatExemption);

public static class Pricing
{
    public static PriceQuote Quote(PlatformSettings settings, int activeProperties, BillingInterval interval)
    {
        var count = Math.Max(1, activeProperties);
        var unit = interval == BillingInterval.Yearly
            ? Round(settings.MonthlyPricePerProperty * 12m * (1m - settings.YearlyDiscountPercent / 100m))
            : Round(settings.MonthlyPricePerProperty);

        var exemption = string.IsNullOrWhiteSpace(settings.VatExemption) ? null : settings.VatExemption.Trim();
        decimal? vatPercent = exemption is null ? settings.VatPercent : null;
        var rate = (vatPercent ?? 0m) / 100m;

        decimal net, gross;
        if (settings.PricesIncludeVat)
        {
            gross = unit * count;
            net = Round(gross / (1m + rate));
        }
        else
        {
            net = unit * count;
            gross = net + Round(net * rate);
        }
        return new PriceQuote(interval, count, unit, settings.PricesIncludeVat, net, gross - net, gross, vatPercent, exemption);
    }

    public static DateTime PeriodEnd(DateTime start, BillingInterval interval) =>
        interval == BillingInterval.Yearly ? start.AddYears(1) : start.AddMonths(1);

    private static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
