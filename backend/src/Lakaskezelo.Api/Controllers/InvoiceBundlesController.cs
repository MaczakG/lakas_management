using System.Globalization;
using System.IO.Compression;
using System.Text;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Számla csomagok: havonta az összes kiállított bizonylat PDF-je egy ZIP-ben, egy összesítő CSV-vel együtt
// (könyvelőnek). A sztornózott bizonylat és a sztornója is benne van (a sztornó negatív összeggel, így az összesen
// helyes); a sikertelen (Failed) számlák nem, mert nem lettek kiállítva — a havi listában külön számként látszanak.
[Authorize]
[ApiController]
[Route("api/invoice-bundles")]
public class InvoiceBundlesController(LakaskezeloDbContext db, InvoicePdfStore pdfStore) : ControllerBase
{
    private static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");

    // Az évszűrőhöz: azok az évek, amelyekhez van legalább egy számla (a legújabb elöl).
    [HttpGet("years")]
    public async Task<ActionResult<List<int>>> Years(CancellationToken ct) =>
        Ok(await db.Invoices.Select(i => i.PeriodYear).Distinct().OrderByDescending(y => y).ToListAsync(ct));

    [HttpGet]
    public async Task<ActionResult<List<InvoiceBundleMonthDto>>> List([FromQuery] int year, CancellationToken ct)
    {
        var invoices = await db.Invoices.Where(i => i.PeriodYear == year)
            .Select(i => new { i.PeriodMonth, i.Status, i.AmountTotal, Storno = i.CancelsInvoiceId != null })
            .ToListAsync(ct);
        return Ok(invoices
            .GroupBy(i => i.PeriodMonth)
            .OrderByDescending(g => g.Key)
            .Select(g => new InvoiceBundleMonthDto(
                year, g.Key,
                g.Count(i => i.Status != InvoiceStatus.Failed),
                g.Count(i => i.Status == InvoiceStatus.Failed),
                g.Where(i => i.Status != InvoiceStatus.Failed).Sum(i => i.AmountTotal),
                g.Count(i => i.Storno))));
    }

    // Egy hónap számlái (a lista lenyíló soraihoz): elöl a csomagba (ZIP-be) kerülők sorszám szerint (így a sztornózott
    // bizonylat, a sztornója és az új számla egymás alatt van), a végén a sikertelenek, amelyek innen újrapróbálhatók.
    [HttpGet("{year:int}/{month:int}")]
    public async Task<ActionResult<List<InvoiceDto>>> Invoices(int year, int month, CancellationToken ct)
    {
        var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .Where(i => i.PeriodYear == year && i.PeriodMonth == month)
            .ToListAsync(ct);
        return Ok(invoices
            .OrderBy(i => i.Status == InvoiceStatus.Failed)
            .ThenBy(i => i.Number)
            .Select(InvoicesController.ToDto));
    }

    [HttpGet("{year:int}/{month:int}/zip")]
    public async Task<IActionResult> DownloadZip(int year, int month, CancellationToken ct)
    {
        var invoices = await InvoicesController.WithPdfData(db.Invoices)
            .Where(i => i.PeriodYear == year && i.PeriodMonth == month && i.Status != InvoiceStatus.Failed)
            .OrderBy(i => i.Number)
            .ToListAsync(ct);
        if (invoices.Count == 0) return NotFound(new { message = "Ebben a hónapban nincs kiküldött vagy legenerált bizonylat." });

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var summary = new StringBuilder("Bizonylatszám;Ingatlan;Bérlő;Időszak;Kiállítva;Fizetési határidő;Összeg (Ft);Állapot\r\n");
            foreach (var invoice in invoices)
            {
                var pdf = await pdfStore.GetOrCreateAsync(invoice, ct);
                var entry = zip.CreateEntry(InvoicePdfStore.FileName(invoice), CompressionLevel.Optimal);
                await using (var entryStream = entry.Open())
                {
                    await entryStream.WriteAsync(pdf, ct);
                }

                summary.Append(string.Join(';',
                    Csv(invoice.Number), Csv(invoice.Property?.Name), Csv(invoice.Tenant?.Name),
                    $"{invoice.PeriodYear}.{invoice.PeriodMonth:D2}", invoice.IssuedAt.ToString("yyyy.MM.dd", Hu),
                    invoice.DueDate.ToString("yyyy.MM.dd", Hu), invoice.AmountTotal.ToString("0", Hu),
                    Csv(StatusText(invoice, invoices)))).Append("\r\n");
            }

            // UTF-8 BOM + pontosvessző, hogy a magyar Excel ékezethelyesen, oszlopokra bontva nyissa meg.
            var summaryEntry = zip.CreateEntry("osszesito.csv", CompressionLevel.Optimal);
            await using var summaryStream = summaryEntry.Open();
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(summary.ToString())).ToArray();
            await summaryStream.WriteAsync(bytes, ct);
        }

        return File(buffer.ToArray(), "application/zip", $"bizonylatok-{year}-{month:D2}.zip");
    }

    private static string StatusText(Domain.Entities.Invoice invoice, List<Domain.Entities.Invoice> all)
    {
        var sent = invoice.Status == InvoiceStatus.Sent ? "Elküldve" : "Legenerálva";
        if (invoice.CancelsInvoiceId is { } originalId)
        {
            var original = all.FirstOrDefault(i => i.Id == originalId)?.Number;
            return $"Sztornó{(original is null ? "" : $" ({original} sztornója)")}, {sent.ToLowerInvariant()}";
        }
        return invoice.CancelledAt is not null ? $"Sztornózott, {sent.ToLowerInvariant()}" : sent;
    }

    private static string Csv(string? value)
    {
        var v = value ?? "";
        return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
