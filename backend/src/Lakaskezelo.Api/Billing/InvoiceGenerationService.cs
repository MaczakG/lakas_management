using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Api.Storage;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Egy adott ingatlan adott havi számlájának összeállítása, PDF-generálása, S3-ba mentése és
// e-mailes kiküldése — ezt hívja mind a BillingSchedulerService (automatikus, ütemezett), mind az
// InvoicesController manuális "Generálás most" / "Újraküldés" végpontja, hogy a logika egy helyen
// éljen.
public class InvoiceGenerationService(
    LakaskezeloDbContext db,
    AppSettingsService settingsService,
    S3InvoiceStorage storage,
    IEmailSender emailSender,
    ILogger<InvoiceGenerationService> logger)
{
    // A Beállítások oldalon szerkeszthető sablon alapértéke — csak akkor kerül elő, ha a mező
    // üres, hogy a régebbi (még nem testreszabott) telepítéseken ne változzon a kiküldött szöveg.
    private const string DefaultInvoiceEmailSubject = "Számla — {ingatlan}";
    private const string DefaultInvoiceEmailBody = "Elkészült a(z) {ingatlan} ingatlanhoz tartozó számlád a(z) {időszak} időszakra.\n\nFizetendő összeg: {összeg}";

    // onProgress (opcionális): a felület folyamatjelzőjének — előbb a lépések terve („plan”), aztán minden lépés
    // kezdete (data, pdf, upload, email, save). A visszahívás hibája (pl. bezárt böngésző) a generálást nem állítja meg.
    public async Task<Invoice> GenerateAndSendAsync(Property property, int year, int month, CancellationToken ct,
        Func<GenerationProgress, Task>? onProgress = null)
    {
        async Task Report(GenerationProgress progress)
        {
            if (onProgress is null) return;
            try { await onProgress(progress); }
            catch (Exception ex) { logger.LogDebug(ex, "Invoice progress callback failed"); }
        }

        var existing = await db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.PropertyId == property.Id && i.PeriodYear == year && i.PeriodMonth == month, ct);

        var settings = await settingsService.GetAsync(ct);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.PropertyId == property.Id, ct);
        var utilityLines = await db.UtilityCostEntries
            .Where(u => u.PropertyId == property.Id && u.Year == year && u.Month == month)
            .ToListAsync(ct);

        // A bizonylat "Bérleti díj jogosultjai" táblázatához kell a tulajdonos-lista Owner-navigációval.
        await db.Entry(property).Collection(p => p.PropertyOwners).Query().Include(po => po.Owner).LoadAsync(ct);

        var invoice = existing ?? new Invoice
        {
            Id = Guid.NewGuid(),
            // Már most kell (nem csak mentéskor): a PDF tárhely-kulcsa a cégre épül (InvoicePdfStore.StorageKey).
            CompanyId = property.CompanyId,
            PropertyId = property.Id,
            PeriodYear = year,
            PeriodMonth = month,
        };

        invoice.TenantId = tenant?.Id;
        invoice.IssuedAt = DateTime.UtcNow;
        invoice.DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8);
        invoice.Number = existing?.Number ?? BuildInvoiceNumber(property, year, month);

        try
        {
            var storageConfigured = await storage.IsConfiguredAsync(ct);
            var willEmail = !string.IsNullOrWhiteSpace(tenant?.Email);
            var plan = new List<string> { GenerationSteps.Data, GenerationSteps.Pdf };
            if (storageConfigured) plan.Add(GenerationSteps.Upload);
            if (willEmail) plan.Add(GenerationSteps.Email);
            plan.Add(GenerationSteps.Save);
            await Report(new GenerationProgress(GenerationSteps.Plan, plan));
            await Report(new GenerationProgress(GenerationSteps.Data));

            // A bérleti díj sor összege a bérlőnél beállított devizanemtől függ (RentCurrency) — az
            // Ingatlan RentAmount mezője HUF helyett EUR/USD is lehet, de a számlán mindig a
            // legfrissebb tárolt MNB árfolyamon átváltott HUF összeg jelenik meg. Ha nincs még
            // elérhető árfolyam a beállított devizához, a számla Failed státusszal jön létre, jól
            // olvasható hibaüzenettel.
            var (rentAmountHuf, conversionNote) = await ResolveRentAmountAsync(property, tenant, ct);

            invoice.RentConversionNote = conversionNote;

            // A sorokat egy különálló, lokális listában építjük fel, és csak a db.InvoiceLines
            // DbSet-en át vesszük fel/töröljük — szándékosan NEM az invoice.Lines navigációs listát
            // mutáljuk (se .Clear(), se .Add()). Két egymásra ható EF change-tracker gotcha miatt:
            // (1) egy előre beállított Guid kulcsú új entitást navigációs listán át felvéve az EF
            // tévesen Modified-nek (UPDATE) jelöl Added (INSERT) helyett — ugyanez a hiba, mint amit
            // a PropertyOwners-nél találtunk; (2) a hívó (InvoicesController.Generate) a válasz
            // összeállításához explicit db.Entry(invoice).Collection(i => i.Lines).LoadAsync()-ot hív
            // — ha időközben kézzel is feltöltöttük volna az invoice.Lines listát, a friss lekérdezés
            // duplikálná a sorokat a végeredményben.
            var oldLines = existing?.Lines.ToList() ?? [];
            if (oldLines.Count > 0) db.InvoiceLines.RemoveRange(oldLines);

            var newLines = new List<InvoiceLine>
            {
                new() { Id = Guid.NewGuid(), InvoiceId = invoice.Id, Label = InvoiceLine.RentLabel, Amount = rentAmountHuf, Position = 0 },
            };
            // A rezsi tételesen kerül a számlára: a Rezsi felvételben rögzített minden tétel külön sor, a felvett
            // megnevezéssel (pl. Áram, Gáz, Közös költség), a rögzítés sorrendjében.
            foreach (var u in utilityLines.Where(u => u.Amount != 0).OrderBy(u => u.CreatedAt).ThenBy(u => u.Label))
            {
                var label = string.IsNullOrWhiteSpace(u.Label) ? "Rezsi" : u.Label.Trim();
                newLines.Add(new InvoiceLine { Id = Guid.NewGuid(), InvoiceId = invoice.Id, Label = label, Amount = u.Amount, Position = newLines.Count });
            }
            db.InvoiceLines.AddRange(newLines);
            invoice.AmountTotal = newLines.Sum(l => l.Amount);

            await Report(new GenerationProgress(GenerationSteps.Pdf));
            var pdfBytes = InvoicePdfGenerator.Generate(invoice, property, tenant, settings, newLines);

            // A tárhely hibája nem akaszthatja meg a számla kiküldését: ilyenkor a PDF letöltéskor
            // (InvoicePdfStore) a tárolt adatokból újragenerálódik és akkor mentődik el.
            invoice.PdfStorageKey = null;
            if (storageConfigured)
            {
                await Report(new GenerationProgress(GenerationSteps.Upload));
                try
                {
                    var key = InvoicePdfStore.StorageKey(invoice);
                    await storage.UploadAsync(key, pdfBytes, "application/pdf", ct);
                    invoice.PdfStorageKey = key;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Storing invoice PDF {Number} in S3 failed", invoice.Number);
                }
            }

            var emailed = false;
            if (willEmail)
            {
                await Report(new GenerationProgress(GenerationSteps.Email));
                var periodLabel = $"{year}.{month:D2}";
                var placeholders = new Dictionary<string, string>
                {
                    ["{ingatlan}"] = property.Name,
                    ["{időszak}"] = periodLabel,
                    ["{összeg}"] = MoneyFormat.Huf(invoice.AmountTotal),
                    ["{számlaszám}"] = invoice.Number,
                };
                string Fill(string template) => placeholders.Aggregate(template, (acc, kv) => acc.Replace(kv.Key, kv.Value));

                var subject = Fill(string.IsNullOrWhiteSpace(settings.InvoiceEmailSubject) ? DefaultInvoiceEmailSubject : settings.InvoiceEmailSubject);
                var bodyText = Fill(string.IsNullOrWhiteSpace(settings.InvoiceEmailBody) ? DefaultInvoiceEmailBody : settings.InvoiceEmailBody);

                var bodyParagraphsHtml = string.Concat(bodyText
                    .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => $"""<p style="margin:0 0 12px;">{System.Net.WebUtility.HtmlEncode(p).Replace("\n", "<br>")}</p>"""));
                var htmlBody = EmailTemplate.Render(preheader: $"{subject} — {periodLabel}", heading: subject, bodyHtml: bodyParagraphsHtml);
                var attachment = new EmailAttachment($"{invoice.Number}.pdf", pdfBytes, "application/pdf");
                emailed = await emailSender.SendAsync(tenant.Email!, EmailTemplate.UniqueSubject(subject), htmlBody, bodyText, attachment, ct);
            }

            invoice.Status = emailed ? InvoiceStatus.Sent : InvoiceStatus.Generated;
            invoice.SentAt = emailed ? DateTime.UtcNow : null;
            invoice.ErrorMessage = null;
        }
        catch (Exception ex)
        {
            invoice.Status = InvoiceStatus.Failed;
            invoice.ErrorMessage = ex.Message;
        }

        await Report(new GenerationProgress(GenerationSteps.Save));
        if (existing is null) db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    private async Task<(decimal AmountHuf, string? ConversionNote)> ResolveRentAmountAsync(Property property, Tenant? tenant, CancellationToken ct)
    {
        var currency = tenant?.RentCurrency ?? RentCurrency.HUF;
        if (currency == RentCurrency.HUF)
        {
            return (property.RentAmount, null);
        }

        var currencyCode = currency.ToString();
        var rate = await db.ExchangeRates
            .Where(r => r.CurrencyCode == currencyCode)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Nincs elérhető MNB árfolyam ehhez: {currencyCode}. A rezsi/díj számításhoz az Árfolyamok oldalon kell legalább egy sikeres lekérdezésnek megtörténnie.");

        var amountHuf = Math.Round(property.RentAmount * rate.RateToHuf, 0, MidpointRounding.AwayFromZero);
        var note = $"Bérleti díj devizában: {MoneyFormat.N2(property.RentAmount)} {currencyCode} × {MoneyFormat.N2(rate.RateToHuf)} Ft/{currencyCode} MNB árfolyamon ({rate.RateDate:yyyy.MM.dd.}).";
        return (amountHuf, note);
    }

    // A bizonylat száma "{Előtag}-{hónap}-{év}" alakú, pl. "A22-8-2026" — az ingatlanon
    // beállított előtaggal (Property.InvoicePrefix), hónap nullák nélkül.
    private static string BuildInvoiceNumber(Property property, int year, int month)
    {
        var prefix = string.IsNullOrWhiteSpace(property.InvoicePrefix) ? "SZLA" : property.InvoicePrefix;
        return $"{prefix}-{month}-{year}";
    }
}

// A számla-generálás egy lépése a folyamatjelzőhöz (ld. InvoicesController.GenerateWithProgress).
public record GenerationProgress(string Step, IReadOnlyList<string>? Plan = null);

public static class GenerationSteps
{
    public const string Plan = "plan";
    public const string Data = "data";
    public const string Pdf = "pdf";
    public const string Upload = "upload";
    public const string Email = "email";
    public const string Save = "save";
}
