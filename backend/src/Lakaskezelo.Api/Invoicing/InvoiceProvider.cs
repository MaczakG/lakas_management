using System.Text.RegularExpressions;
using Lakaskezelo.Domain;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Api.Invoicing;

// Cégek számlázója (Billingo, Számlázz.hu): a havi bérleti díjat és rezsit valódi számlaként állítja ki (saját
// sorszám, NAV-bejelentés a szolgáltatónál), sztornózza, és visszaadja a PDF-et. A hibák ProviderException-t
// dobnak, magyar, a felhasználónak megmutatható üzenettel.
public interface IInvoiceProvider
{
    string Name { get; }
    Task<ProviderInvoice> IssueAsync(AppSettings settings, ProviderInvoiceRequest request, CancellationToken ct);
    Task<ProviderInvoice> CancelAsync(AppSettings settings, Invoice original, CancellationToken ct);
    Task<byte[]> DownloadPdfAsync(AppSettings settings, Invoice invoice, CancellationToken ct);
    Task<ProviderTestResult> TestAsync(AppSettings settings, CancellationToken ct);
}

public class ProviderException(string message, Exception? inner = null) : Exception(message, inner);

// Egy tétel bruttó összeggel (amit a bérlő fizet) és áfakóddal (VatCodes).
public record ProviderLine(string Name, decimal GrossAmount, string VatCode);

public record ProviderBuyer(string Name, string? Email, string? TaxNumber, string PostCode, string City, string Street, int? BillingoPartnerId);

public record ProviderInvoiceRequest(
    ProviderBuyer Buyer, IReadOnlyList<ProviderLine> Lines, DateOnly IssueDate, DateOnly FulfillmentDate, DateOnly DueDate, string Comment);

// Number: a szolgáltató számlaszáma; DocumentId: amivel később elérjük (Billingo: id; Számlázz.hu: számlaszám).
// Pdf: ha a szolgáltató rögtön visszaadta; különben DownloadPdfAsync. BillingoPartnerId: az új/frissített partner.
public record ProviderInvoice(string Number, string DocumentId, byte[]? Pdf, int? BillingoPartnerId = null);

public record ProviderTestResult(bool Success, string? Error, List<ProviderBlock>? Blocks = null);
public record ProviderBlock(int Id, string Name);

public static partial class InvoicingProviders
{
    public const string Billingo = "Billingo";
    public const string Szamlazzhu = "Szamlazzhu";

    public static bool IsKnown(string? name) => name is Billingo or Szamlazzhu;

    // Ki van-e töltve minden, ami a kiállításhoz kell.
    public static bool IsConfigured(AppSettings s) => s.InvoiceProvider switch
    {
        Billingo => !string.IsNullOrWhiteSpace(s.BillingoApiKey) && s.BillingoBlockId is not null,
        Szamlazzhu => !string.IsNullOrWhiteSpace(s.SzamlazzAgentKey),
        _ => false,
    };

    // Nettó és áfa a bruttóból, egész forintra (HUF számla).
    public static (decimal Net, decimal Vat) Split(decimal gross, string vatCode)
    {
        if (VatCodes.IsExempt(vatCode)) return (gross, 0m);
        var net = Math.Round(gross / (1 + VatCodes.Rate(vatCode) / 100m), 0, MidpointRounding.AwayFromZero);
        return (net, gross - net);
    }

    // A vevő címe: a bérlőnél megadott cím („1234 Város, utca 1.”), ha értelmezhető, különben az ingatlan címe.
    public static ProviderBuyer BuyerFor(Tenant tenant, Property property)
    {
        string postCode = property.PostalCode ?? "", city = property.City ?? "", street = string.Join(" ", new[] { property.Street, property.FloorDoor }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(tenant.Address) && AddressRegex().Match(tenant.Address.Trim()) is { Success: true } m)
        {
            postCode = m.Groups[1].Value;
            city = m.Groups[2].Value.Trim();
            street = m.Groups[3].Value.Trim();
        }
        if (string.IsNullOrWhiteSpace(postCode) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(street))
        {
            throw new ProviderException("A számlához hiányzik a vevő címe: add meg a bérlőnél („1234 Város, utca 1.”), vagy az ingatlan irányítószámát, települését és utcáját.");
        }
        return new ProviderBuyer(tenant.Name, tenant.Email, string.IsNullOrWhiteSpace(tenant.TaxId) ? null : tenant.TaxId.Trim(), postCode, city, street, tenant.BillingoPartnerId);
    }

    [GeneratedRegex(@"^(\d{4})\s+([^,]+),\s*(.+)$")]
    private static partial Regex AddressRegex();
}

public class InvoiceProviderResolver(BillingoInvoiceProvider billingo, SzamlazzInvoiceProvider szamlazz)
{
    public IInvoiceProvider? ByName(string? name) => name switch
    {
        InvoicingProviders.Billingo => billingo,
        InvoicingProviders.Szamlazzhu => szamlazz,
        _ => null,
    };

    // A cég számlázója, ha cég és be van állítva; különben null (bizonylat készül).
    public IInvoiceProvider? For(Company company, AppSettings settings) =>
        company.IsBusiness && InvoicingProviders.IsConfigured(settings) ? ByName(settings.InvoiceProvider) : null;
}
