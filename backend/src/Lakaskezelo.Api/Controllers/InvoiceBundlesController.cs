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

// Számla csomagok: havonta az összes számla PDF-je egy ZIP-ben, egy összesítő CSV-vel együtt
// (könyvelőnek, archiváláshoz). A sikertelen (Failed) számlák nem kerülnek a csomagba, mert nem
// mentek ki — a havi listában külön számként látszanak.
[Authorize]
[ApiController]
[Route("api/invoice-bundles")]
public class InvoiceBundlesController(LakaskezeloDbContext db, InvoicePdfStore pdfStore) : ControllerBase
{
    private static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");

    [HttpGet]
    public async Task<ActionResult<List<InvoiceBundleMonthDto>>> List([FromQuery] int year, CancellationToken ct)
    {
        var invoices = await db.Invoices.Where(i => i.PeriodYear == year)
            .Select(i => new { i.PeriodMonth, i.Status, i.AmountTotal })
            .ToListAsync(ct);
        return Ok(invoices
            .GroupBy(i => i.PeriodMonth)
            .OrderByDescending(g => g.Key)
            .Select(g => new InvoiceBundleMonthDto(
                year, g.Key,
                g.Count(i => i.Status != InvoiceStatus.Failed),
                g.Count(i => i.Status == InvoiceStatus.Failed),
                g.Where(i => i.Status != InvoiceStatus.Failed).Sum(i => i.AmountTotal))));
    }

    [HttpGet("{year:int}/{month:int}/zip")]
    public async Task<IActionResult> DownloadZip(int year, int month, CancellationToken ct)
    {
        var invoices = await InvoicesController.WithPdfData(db.Invoices)
            .Where(i => i.PeriodYear == year && i.PeriodMonth == month && i.Status != InvoiceStatus.Failed)
            .OrderBy(i => i.Number)
            .ToListAsync(ct);
        if (invoices.Count == 0) return NotFound(new { message = "Ebben a hónapban nincs kiküldött vagy legenerált számla." });

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var summary = new StringBuilder("Számlaszám;Ingatlan;Bérlő;Időszak;Kiállítva;Fizetési határidő;Összeg (Ft);Állapot\r\n");
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
                    invoice.Status == InvoiceStatus.Sent ? "Elküldve" : "Legenerálva")).Append("\r\n");
            }

            // UTF-8 BOM + pontosvessző, hogy a magyar Excel ékezethelyesen, oszlopokra bontva nyissa meg.
            var summaryEntry = zip.CreateEntry("osszesito.csv", CompressionLevel.Optimal);
            await using var summaryStream = summaryEntry.Open();
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(summary.ToString())).ToArray();
            await summaryStream.WriteAsync(bytes, ct);
        }

        return File(buffer.ToArray(), "application/zip", $"szamlak-{year}-{month:D2}.zip");
    }

    private static string Csv(string? value)
    {
        var v = value ?? "";
        return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
