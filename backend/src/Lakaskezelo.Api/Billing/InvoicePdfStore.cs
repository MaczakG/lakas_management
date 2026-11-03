using Microsoft.EntityFrameworkCore;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Api.Storage;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Api.Billing;

// Egy számla PDF-jének elérése: az S3-ban tárolt eredetit adja vissza, ha van. Ha nincs (régi,
// Drive-os időszakbeli számla, vagy a generáláskor nem sikerült a feltöltés), a tárolt
// számla-adatokból újragenerálja, és el is menti, hogy legközelebb már az eredeti jöjjön.
public class InvoicePdfStore(LakaskezeloDbContext db, AppSettingsService settingsService, S3InvoiceStorage storage,
    Invoicing.InvoiceProviderResolver providers, ILogger<InvoicePdfStore> logger)
{
    // Cégenkénti előtag: a tárhelyen is elkülönülnek az előfizetők PDF-jei. (A korábban feltöltött,
    // előtag nélküli kulcsok az Invoice.PdfStorageKey-ben vannak, azok így is megtalálhatók.)
    public static string StorageKey(Invoice invoice) =>
        $"companies/{invoice.CompanyId}/invoices/{invoice.PeriodYear}/{invoice.PeriodMonth:D2}/{invoice.Id}.pdf";

    public static string FileName(Invoice invoice) =>
        SanitizeFileName($"{invoice.Number} - {invoice.Property?.Name}.pdf");

    // Az invoice-nál be kell legyen töltve: Property (+PropertyOwners.Owner), Tenant, Lines.
    public async Task<byte[]> GetOrCreateAsync(Invoice invoice, CancellationToken ct)
    {
        var configured = await storage.IsConfiguredAsync(ct);
        if (configured && invoice.PdfStorageKey is { } key && await storage.DownloadAsync(key, ct) is { } stored)
        {
            return stored;
        }

        var settings = await settingsService.GetAsync(ct);
        byte[] pdf;
        if (invoice.Provider is not null)
        {
            // A számlázóban kiállított számla PDF-je a számlázótól jön (nem generáljuk újra: az az eredeti számla).
            var provider = providers.ByName(invoice.Provider);
            if (provider is null || settings.InvoiceProvider != invoice.Provider || !Invoicing.InvoicingProviders.IsConfigured(settings))
            {
                throw new Invoicing.ProviderException($"A számla PDF-je a(z) {invoice.Provider} fiókban van, de az összekötés nincs beállítva (Beállítások).");
            }
            pdf = await provider.DownloadPdfAsync(settings, invoice, ct);
        }
        else
        {
            var cancelsNumber = invoice.CancelsInvoiceId is { } originalId
                ? await db.Invoices.Where(i => i.Id == originalId).Select(i => i.Number).FirstOrDefaultAsync(ct)
                : null;
            pdf = InvoicePdfGenerator.Generate(invoice, invoice.Property!, invoice.Tenant, settings, [.. InvoiceLine.InDisplayOrder(invoice.Lines)], cancelsNumber);
        }

        if (configured)
        {
            try
            {
                var newKey = StorageKey(invoice);
                await storage.UploadAsync(newKey, pdf, "application/pdf", ct);
                invoice.PdfStorageKey = newKey;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not store regenerated PDF for invoice {Number}", invoice.Number);
            }
        }
        return pdf;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
