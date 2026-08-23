using System.Text.RegularExpressions;
using Lakaskezelo.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lakaskezelo.Api.Billing;

// A felhasználó által megküldött "Számviteli bizonylat" sablon egy-az-egyben leképezve. Ha a
// sablon később finomodik, ez az egyetlen fájl, amit módosítani kell — a hívó oldal
// (InvoiceGenerationService) csak a visszaadott byte[]-tel dolgozik.
public static class InvoicePdfGenerator
{
    private static readonly string[] MonthNames =
        ["január", "február", "március", "április", "május", "június",
         "július", "augusztus", "szeptember", "október", "november", "december"];

    // A tételeket külön paraméterként kapja, nem az invoice.Lines navigációból olvassa — az
    // InvoiceGenerationService szándékosan nem tölti fel azt a listát (ld. ottani megjegyzés).
    public static byte[] Generate(Invoice invoice, Property property, Tenant? tenant, AppSettings settings, List<InvoiceLine> lines)
    {
        var periodLabel = $"{invoice.PeriodYear}.{MonthNames[invoice.PeriodMonth - 1]}";
        var issuerCity = ExtractCity(settings.IssuerAddress);

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10.5f).FontFamily("Arial"));

                page.Content().Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("SZÁMVITELI BIZONYLAT").FontSize(16).Bold();
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text($"Bizonylat száma: {invoice.Number}");
                            c.Item().Text($"Időszak: {periodLabel}");
                        });
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        void Cell(string text) => table.Cell().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Text(text);

                        Cell($"Bizonylat kiállító neve: {settings.IssuerName}");
                        Cell($"Bérbe vevő neve: {tenant?.Name ?? "—"}");
                        Cell($"Bizonylat kiállító címe: {settings.IssuerAddress}");
                        Cell($"Bérbe vevő címe: {tenant?.Address ?? "—"}");
                        Cell($"Bizonylat kiállító bankszámlaszáma: {settings.IssuerBankAccount}");
                        Cell($"Bérbe vevő adószáma: {tenant?.TaxId ?? "—"}");
                    });

                    col.Item().Text($"Gazdasági esemény megnevezése: {property.Name} bérleti díja");

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                        });

                        foreach (var line in lines)
                        {
                            table.Cell().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Text(line.Label);
                            table.Cell().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Text($"{line.Amount:N0} Ft");
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(invoice.RentConversionNote))
                    {
                        col.Item().Text(invoice.RentConversionNote).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                    }

                    if (property.PropertyOwners.Count > 0)
                    {
                        var owners = property.PropertyOwners;
                        var shareLabel = string.Join("-", Enumerable.Repeat($"1/{owners.Count}", owners.Count));

                        col.Item().Text($"Bérleti díj jogosultjai {shareLabel} arányban:");

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Név").Bold();
                                header.Cell().Text("Lakcím").Bold();
                                header.Cell().Text("Adóazonosító jele").Bold();
                                header.Cell().ColumnSpan(3).PaddingTop(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);
                            });

                            foreach (var po in owners)
                            {
                                table.Cell().PaddingVertical(3).Text(po.Owner!.Name);
                                table.Cell().PaddingVertical(3).Text(po.Owner!.Address ?? "—");
                                table.Cell().PaddingVertical(3).Text(po.Owner!.TaxId ?? "—");
                            }
                        });
                    }

                    col.Item().PaddingTop(10).Text($"Kelt: {(issuerCity is not null ? issuerCity + ", " : "")}{invoice.IssuedAt:yyyy.MM.dd.}");

                    col.Item().PaddingTop(40).Column(c =>
                    {
                        c.Item().Width(160).BorderBottom(1).BorderColor(Colors.Black);
                        c.Item().PaddingTop(4).Text(settings.IssuerName ?? "");
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    // Egy szabadszöveges cím első ("irányítószám Város") tagjából próbálja kiolvasni a várost —
    // pl. "2083 Solymár, Budai Nagy Antal u. 14/b 2a" -> "Solymár". Ha nem illeszkedik a mintára,
    // a teljes első tagot adja vissza inkább, mint hogy semmit ne írjon ki.
    private static string? ExtractCity(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var firstSegment = address.Split(',')[0].Trim();
        var match = Regex.Match(firstSegment, @"^\d+\s+(.+)$");
        return match.Success ? match.Groups[1].Value.Trim() : firstSegment;
    }
}
