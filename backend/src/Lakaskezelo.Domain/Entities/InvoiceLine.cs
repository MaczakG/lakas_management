namespace Lakaskezelo.Domain.Entities;

public class InvoiceLine : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    // A sor helye a bizonylaton (0-tól). A régi (sorszám nélküli) számláknál mind 0 — ott a bérleti díj kerül előre.
    public int Position { get; set; }

    public const string RentLabel = "Bérleti díj";

    // A bizonylat és a lista sorrendje: a tárolt sorszám szerint; régi számláknál bérleti díj elöl, aztán ABC.
    public static IEnumerable<InvoiceLine> InDisplayOrder(IEnumerable<InvoiceLine> lines) => lines
        .OrderBy(l => l.Position)
        .ThenBy(l => l.Label == RentLabel ? 0 : 1)
        .ThenBy(l => l.Label, StringComparer.Create(new System.Globalization.CultureInfo("hu-HU"), false));
}
