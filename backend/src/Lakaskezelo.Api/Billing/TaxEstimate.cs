namespace Lakaskezelo.Api.Billing;

// Adózási kimutatás a vezérlőpultra (magánszemély bérbeadónak): mennyi SZJA-t fizetne 10% költséghányaddal, és
// mennyit tételes költségelszámolással. Becslés a programban rögzített adatokból, nem adóbevallás.
//  - Bevétel: a kiállított bizonylatok összege (bérleti díj és a bizonylatra került rezsi; a bérlő által levont
//    SZJA sora nélkül), a bizonylat időszakának hónapjában.
//  - Költség (tételes): minden felvitt, összeggel rendelkező rezsi tétel, amely nem „tovább számlázandó”.
//  - 10%: jövedelem = bevétel × 90%. Tételes: jövedelem = bevétel − költség. Adó = a jövedelem 15%-a.
// Az éves adó az éves jövedelemből számol (legalább 0): egy veszteséges hónap az éves jövedelmet csökkenti.
public static class TaxEstimate
{
    public const decimal FlatCostRate = 0.10m;
    public const decimal TaxRate = InvoiceGenerationService.SzjaRate;

    // FlatProfit / ItemizedProfit: a hónap jövedelme a két esetben (a tételes lehet negatív). Withheld: a bérlő
    // (kifizető) által a bizonylatokon már levont SZJA.
    public record Month(int Number, decimal Income, decimal Costs, decimal FlatProfit, decimal FlatTax,
        decimal ItemizedProfit, decimal ItemizedTax, decimal Withheld);

    public record Result(decimal Income, decimal Costs, decimal FlatProfit, decimal FlatTax,
        decimal ItemizedProfit, decimal ItemizedTax, decimal Withheld, IReadOnlyList<Month> Months);

    public static decimal Tax(decimal profit) => Math.Round(Math.Max(profit, 0) * TaxRate, 0, MidpointRounding.AwayFromZero);

    // A három szótár kulcsa a hónap (1–12); a hiányzó hónap 0.
    public static Result Calculate(IReadOnlyDictionary<int, decimal> income, IReadOnlyDictionary<int, decimal> costs,
        IReadOnlyDictionary<int, decimal> withheld)
    {
        var months = Enumerable.Range(1, 12).Select(m =>
        {
            var inc = income.GetValueOrDefault(m);
            var cost = costs.GetValueOrDefault(m);
            var flat = inc * (1 - FlatCostRate);
            var itemized = inc - cost;
            return new Month(m, inc, cost, flat, Tax(flat), itemized, Tax(itemized), withheld.GetValueOrDefault(m));
        }).ToList();

        var totalIncome = months.Sum(m => m.Income);
        var totalCosts = months.Sum(m => m.Costs);
        var flatProfit = totalIncome * (1 - FlatCostRate);
        var itemizedProfit = totalIncome - totalCosts;
        return new Result(totalIncome, totalCosts, flatProfit, Tax(flatProfit), itemizedProfit, Tax(itemizedProfit),
            months.Sum(m => m.Withheld), months);
    }
}
