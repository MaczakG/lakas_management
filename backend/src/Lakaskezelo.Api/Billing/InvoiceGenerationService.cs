using Lakaskezelo.Api.GoogleDrive;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Egy adott ingatlan adott havi számlájának összeállítása, PDF-generálása, Drive-feltöltése és
// e-mailes kiküldése — ezt hívja mind a BillingSchedulerService (automatikus, ütemezett), mind az
// InvoicesController manuális "Generálás most" / "Újraküldés" végpontja, hogy a logika egy helyen
// éljen.
public class InvoiceGenerationService(
    LakaskezeloDbContext db,
    AppSettingsService settingsService,
    GoogleDriveService driveService,
    IEmailSender emailSender)
{
    public async Task<Invoice> GenerateAndSendAsync(Property property, int year, int month, CancellationToken ct)
    {
        var existing = await db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.PropertyId == property.Id && i.PeriodYear == year && i.PeriodMonth == month, ct);

        var settings = await settingsService.GetAsync(ct);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.PropertyId == property.Id, ct);
        var utilityLines = await db.UtilityCostEntries
            .Where(u => u.PropertyId == property.Id && u.Year == year && u.Month == month)
            .ToListAsync(ct);

        var invoice = existing ?? new Invoice
        {
            Id = Guid.NewGuid(),
            PropertyId = property.Id,
            PeriodYear = year,
            PeriodMonth = month,
        };

        invoice.TenantId = tenant?.Id;
        invoice.IssuedAt = DateTime.UtcNow;
        invoice.DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8);
        invoice.Number = existing?.Number ?? await NextInvoiceNumberAsync(ct);

        try
        {
            // A bérleti díj sor összege a bérlőnél beállított devizanemtől függ (RentCurrency) — az
            // Ingatlan RentAmount mezője HUF helyett EUR/USD is lehet, de a számlán mindig a
            // legfrissebb tárolt MNB árfolyamon átváltott HUF összeg jelenik meg. Ha nincs még
            // elérhető árfolyam a beállított devizához, a számla Failed státusszal jön létre, jól
            // olvasható hibaüzenettel.
            var rentLine = await BuildRentLineAsync(property, tenant, ct);

            invoice.Lines.Clear();
            invoice.Lines.Add(rentLine);
            invoice.Lines.AddRange(utilityLines.Select(u => new InvoiceLine { Id = Guid.NewGuid(), Label = u.Label, Amount = u.Amount }));
            invoice.AmountTotal = invoice.Lines.Sum(l => l.Amount);

            var pdfBytes = InvoicePdfGenerator.Generate(invoice, property, tenant, settings);

            if (!string.IsNullOrWhiteSpace(property.DriveFolderId) && await driveService.IsConfiguredAsync(ct))
            {
                var fileName = $"{invoice.Number} - {property.Name} - {year}-{month:D2}.pdf";
                var uploaded = await driveService.UploadFileAsync(property.DriveFolderId, fileName, pdfBytes, "application/pdf", ct);
                invoice.PdfDriveFileId = uploaded?.Id;
                invoice.PdfDriveLink = uploaded?.WebViewLink;
            }

            var emailed = false;
            if (!string.IsNullOrWhiteSpace(tenant?.Email))
            {
                var htmlBody = EmailTemplate.Render(
                    preheader: $"Számla — {property.Name} — {year}.{month:D2}",
                    heading: $"Számla — {invoice.Number}",
                    bodyHtml: $"""
                        <p style="margin:0 0 12px;">Elkészült a(z) <b>{System.Net.WebUtility.HtmlEncode(property.Name)}</b> ingatlanhoz tartozó számlád a(z) {year}.{month:D2}. időszakra.</p>
                        <p style="margin:0 0 12px;">Fizetendő összeg: <b>{invoice.AmountTotal:N0} Ft</b><br>Fizetési határidő: {invoice.DueDate:yyyy.MM.dd.}</p>
                        {(invoice.PdfDriveLink is not null ? $"""<p style="margin:0;">A számla PDF: <a href="{invoice.PdfDriveLink}">megnyitás</a></p>""" : "")}
                        """);
                var textBody = $"Számla — {invoice.Number}. Fizetendő: {invoice.AmountTotal:N0} Ft. Határidő: {invoice.DueDate:yyyy.MM.dd.}.";
                emailed = await emailSender.SendAsync(tenant.Email!, EmailTemplate.UniqueSubject($"Számla — {property.Name}"), htmlBody, textBody, ct);
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

        if (existing is null) db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    private async Task<InvoiceLine> BuildRentLineAsync(Property property, Tenant? tenant, CancellationToken ct)
    {
        var currency = tenant?.RentCurrency ?? RentCurrency.HUF;
        if (currency == RentCurrency.HUF)
        {
            return new InvoiceLine { Id = Guid.NewGuid(), Label = "Bérleti díj", Amount = property.RentAmount };
        }

        var currencyCode = currency.ToString();
        var rate = await db.ExchangeRates
            .Where(r => r.CurrencyCode == currencyCode)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Nincs elérhető MNB árfolyam ehhez: {currencyCode}. A rezsi/díj számításhoz az Árfolyamok oldalon kell legalább egy sikeres lekérdezésnek megtörténnie.");

        var amountHuf = Math.Round(property.RentAmount * rate.RateToHuf, 0, MidpointRounding.AwayFromZero);
        var label = $"Bérleti díj ({property.RentAmount:N2} {currencyCode} × {rate.RateToHuf:N2} Ft/{currencyCode} MNB árfolyamon, {rate.RateDate:yyyy.MM.dd.})";
        return new InvoiceLine { Id = Guid.NewGuid(), Label = label, Amount = amountHuf };
    }

    private async Task<string> NextInvoiceNumberAsync(CancellationToken ct)
    {
        var settingsRow = await db.AppSettings.SingleAsync(s => s.Id == Domain.Entities.AppSettings.SingletonId, ct);
        var number = $"{DateTime.UtcNow:yyyy}-{settingsRow.NextInvoiceNumber:D4}";
        settingsRow.NextInvoiceNumber++;
        settingsRow.UpdatedAt = DateTime.UtcNow;
        AppSettingsService.Invalidate();
        return number;
    }
}
