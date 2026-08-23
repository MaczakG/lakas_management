using Lakaskezelo.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lakaskezelo.Api.Billing;

// Ideiglenes, ésszerű elrendezésű magyar bérleti számla — a felhasználó által később megküldött
// tényleges sablon alapján ez az egyetlen fájl fog cserélődni. A hívó oldal (BillingSchedulerService,
// InvoicesController) csak a visszaadott byte[]-tel dolgozik, a tartalmától függetlenül.
public static class InvoicePdfGenerator
{
    public static byte[] Generate(Invoice invoice, Property property, Tenant? tenant, AppSettings settings)
    {
        var hu = new System.Globalization.CultureInfo("hu-HU");
        var periodLabel = new DateOnly(invoice.PeriodYear, invoice.PeriodMonth, 1).ToString("yyyy. MMMM", hu);

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10.5f).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Text("SZÁMLA").FontSize(22).Bold();
                    col.Item().PaddingTop(2).Text(periodLabel).FontSize(12).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(20).Column(col =>
                {
                    col.Spacing(4);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Kibocsátó").Bold();
                            c.Item().Text(settings.IssuerName ?? "—");
                            if (!string.IsNullOrWhiteSpace(settings.IssuerAddress)) c.Item().Text(settings.IssuerAddress);
                            if (!string.IsNullOrWhiteSpace(settings.IssuerTaxId)) c.Item().Text($"Adószám: {settings.IssuerTaxId}");
                            if (!string.IsNullOrWhiteSpace(settings.IssuerBankAccount)) c.Item().Text($"Bankszámla: {settings.IssuerBankAccount}");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Vevő").Bold();
                            c.Item().Text(tenant?.Name ?? "—");
                            if (!string.IsNullOrWhiteSpace(tenant?.Email)) c.Item().Text(tenant.Email);
                            c.Item().PaddingTop(4).Text(property.Name);
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text($"Számlaszám: {invoice.Number}");
                            c.Item().Text($"Kiállítás dátuma: {invoice.IssuedAt:yyyy.MM.dd.}");
                            c.Item().Text($"Fizetési határidő: {invoice.DueDate:yyyy.MM.dd.}");
                            c.Item().Text($"Teljesítés dátuma: {invoice.DueDate:yyyy.MM.dd.}");
                        });
                    });

                    col.Item().PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Tétel").Bold();
                            header.Cell().AlignRight().Text("Összeg").Bold();
                            header.Cell().ColumnSpan(2).PaddingTop(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);
                        });

                        foreach (var line in invoice.Lines)
                        {
                            table.Cell().PaddingVertical(3).Text(line.Label);
                            table.Cell().PaddingVertical(3).AlignRight().Text($"{line.Amount:N0} Ft");
                        }
                    });

                    col.Item().PaddingTop(16).AlignRight().Text($"Fizetendő összesen: {invoice.AmountTotal:N0} Ft").Bold().FontSize(13);

                    if (!string.IsNullOrWhiteSpace(settings.IssuerBankAccount))
                    {
                        col.Item().PaddingTop(24).Text($"Kérjük az összeget a fizetési határidőig a következő bankszámlára utalni: {settings.IssuerBankAccount}. Közlemény: {invoice.Number}.")
                            .FontSize(9.5f).FontColor(Colors.Grey.Darken1);
                    }
                });

                page.Footer().PaddingTop(16).Column(col =>
                {
                    col.Item().BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(8)
                        .Text("Ez egy ideiglenes számlasablon — a végleges elrendezés a megadott minta alapján fog elkészülni.")
                        .FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        return document.GeneratePdf();
    }
}
