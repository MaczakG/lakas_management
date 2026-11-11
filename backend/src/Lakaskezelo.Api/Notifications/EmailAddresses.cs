using Lakaskezelo.Api.Controllers;

namespace Lakaskezelo.Api.Notifications;

// Egy mezőben több e-mail cím (a bérlő értesítőinek címzettjei): vesszővel, pontosvesszővel, szóközzel vagy
// új sorral elválasztva írhatók be; tárolva ", " választja el őket.
public static class EmailAddresses
{
    public const int MaxCount = 5;

    // A mezőben álló címek a beírás sorrendjében, ismétlődés nélkül.
    public static IReadOnlyList<string> Split(string? value) =>
        (value ?? "").Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string? First(string? value) => Split(value).FirstOrDefault();

    // Egységes alakra hozza a mezőt (üresnél null); hibás címnél vagy túl sok címnél a hibaüzenetet adja vissza.
    public static string? Normalize(string? value, out string? error)
    {
        error = null;
        var addresses = Split(value);
        if (addresses.Count == 0) return null;
        if (addresses.FirstOrDefault(a => !AuthController.LooksLikeEmail(a.ToLowerInvariant())) is { } invalid)
        {
            error = $"Érvénytelen e-mail cím: {invalid}";
            return null;
        }
        if (addresses.Count > MaxCount)
        {
            error = $"Legfeljebb {MaxCount} e-mail cím adható meg.";
            return null;
        }
        return string.Join(", ", addresses);
    }
}
