using System.Globalization;

namespace Lakaskezelo.Api.Billing;

// Összegek magyar formázása explicit szeparátorokkal ("100 000 Ft", "1 234,56 EUR"), hogy ne
// függjön a szerver kultúrájától / ICU-adataitól (a konténerben a ":N0" vesszőt tett ezres
// elválasztónak). Nem törő szóközt használunk, hogy egy összeg ne törjön két sorba.
public static class MoneyFormat
{
    private static readonly NumberFormatInfo Hu = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    public static string N0(decimal value) => value.ToString("N0", Hu);

    public static string N2(decimal value) => value.ToString("N2", Hu);

    public static string Huf(decimal value) => $"{N0(value)} Ft";
}
