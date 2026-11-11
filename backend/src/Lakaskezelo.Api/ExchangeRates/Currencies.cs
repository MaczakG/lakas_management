using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.ExchangeRates;

// A választható devizák neve és köre. Deviza az lehet, amit a program bérleti díj devizájaként kezelni tud
// (RentCurrency), és amire az MNB árfolyamot közöl.
public static class Currencies
{
    // Ezek mindig követve vannak; a többit az Árfolyamok oldalon lehet felvenni.
    public static readonly string[] Default = ["EUR", "USD", "GBP"];

    private static readonly Dictionary<string, string> Names = new()
    {
        ["EUR"] = "euró", ["USD"] = "amerikai dollár", ["GBP"] = "angol font", ["AUD"] = "ausztrál dollár",
        ["BRL"] = "brazil real", ["CAD"] = "kanadai dollár", ["CHF"] = "svájci frank", ["CNY"] = "kínai jüan",
        ["CZK"] = "cseh korona", ["DKK"] = "dán korona", ["HKD"] = "hongkongi dollár", ["IDR"] = "indonéz rúpia",
        ["ILS"] = "izraeli sékel", ["INR"] = "indiai rúpia", ["ISK"] = "izlandi korona", ["JPY"] = "japán jen",
        ["KRW"] = "dél-koreai won", ["MXN"] = "mexikói peso", ["MYR"] = "maláj ringgit", ["NOK"] = "norvég korona",
        ["NZD"] = "új-zélandi dollár", ["PHP"] = "Fülöp-szigeteki peso", ["PLN"] = "lengyel zloty", ["RON"] = "román lej",
        ["RSD"] = "szerb dinár", ["RUB"] = "orosz rubel", ["SEK"] = "svéd korona", ["SGD"] = "szingapúri dollár",
        ["THB"] = "thai bát", ["TRY"] = "török líra", ["UAH"] = "ukrán hrivnya", ["ZAR"] = "dél-afrikai rand",
    };

    // Az alap devizák mindig követettek (az óránkénti frissítés visszatenné őket), ezért nem törölhetők.
    public static bool IsDefault(string code) => Default.Contains(code);

    public static string Name(string code) => Names.GetValueOrDefault(code, code);

    // Kezelhető-e a deviza bérleti díj devizájaként (a forint nem deviza).
    public static bool IsSupported(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code != nameof(RentCurrency.HUF)
        && code == code.ToUpperInvariant() && Enum.TryParse<RentCurrency>(code, ignoreCase: false, out _) && !int.TryParse(code, out _);
}
