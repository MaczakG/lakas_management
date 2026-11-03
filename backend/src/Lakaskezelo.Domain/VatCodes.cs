namespace Lakaskezelo.Domain;

// Áfakódok a számlázóval kiállított számlákhoz. TAM: tárgyi adómentes (pl. lakásbérbeadás), AAM: alanyi adómentes.
public static class VatCodes
{
    public static readonly IReadOnlyList<string> All = ["TAM", "AAM", "27", "18", "5", "0"];

    public static bool IsValid(string? code) => code is not null && All.Contains(code);

    // A kulcs százalékban; az adómentes kódoknál 0.
    public static decimal Rate(string code) => decimal.TryParse(code, out var r) ? r : 0m;

    public static bool IsExempt(string code) => !decimal.TryParse(code, out _);
}
