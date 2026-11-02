using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Subscriptions;

// Egy időszak díja: aktív ingatlanok száma (legalább 1) × ingatlanonkénti nettó egységár. Éves
// fizetésnél az egységár 12 havi díj, a kedvezménnyel csökkentve. Minden összeg egész forint: az
// egységárat kerekítjük, és a bruttót ugyanúgy számoljuk, mint a Billingo (nettó × (1 + ÁFA)), hogy a
// Barionon terhelt összeg és a számla végösszege egyezzen.
public record PriceQuote(
    BillingInterval Interval, int PropertyCount, decimal UnitNetPrice, decimal NetAmount, decimal VatAmount,
    decimal GrossAmount, decimal? VatPercent, string? VatExemption);

public static class Pricing
{
    public static PriceQuote Quote(PlatformSettings settings, int activeProperties, BillingInterval interval)
    {
        var count = Math.Max(1, activeProperties);
        var unit = interval == BillingInterval.Yearly
            ? Math.Round(settings.MonthlyNetPricePerProperty * 12m * (1m - settings.YearlyDiscountPercent / 100m), 0, MidpointRounding.AwayFromZero)
            : Math.Round(settings.MonthlyNetPricePerProperty, 0, MidpointRounding.AwayFromZero);
        var net = unit * count;

        var exemption = string.IsNullOrWhiteSpace(settings.VatExemption) ? null : settings.VatExemption.Trim();
        decimal? vatPercent = exemption is null ? settings.VatPercent : null;
        var vat = vatPercent is { } p ? Math.Round(net * p / 100m, 0, MidpointRounding.AwayFromZero) : 0m;
        return new PriceQuote(interval, count, unit, net, vat, net + vat, vatPercent, exemption);
    }

    public static DateTime PeriodEnd(DateTime start, BillingInterval interval) =>
        interval == BillingInterval.Yearly ? start.AddYears(1) : start.AddMonths(1);
}
