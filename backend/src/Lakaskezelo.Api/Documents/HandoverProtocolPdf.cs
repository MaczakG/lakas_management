using Lakaskezelo.Api.Billing;
using Lakaskezelo.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lakaskezelo.Api.Documents;

// Átadás-átvételi jegyzőkönyv PDF: az ingatlan és a két fél adatai, a beköltözési tudnivalók bepipált sorai
// ellenőrzőlistaként (üres négyzet a papíron való kipipáláshoz), hely az észrevételeknek, kelt és két aláírás. A
// bizonylat PDF-jével (InvoicePdfGenerator) azonos megjelenés.
public static class HandoverProtocolPdf
{
    private const string Ink = "#18181B";
    private const string Muted = "#6B7280";
    private const string Line = "#E4E4E7";
    private const string Surface = "#F4F4F5";
    private const string Accent = "#3F3F46";

    public static byte[] Generate(Property property, Tenant tenant, AppSettings settings, IReadOnlyList<MoveInItem> checklist, DateOnly date)
    {
        static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
        var dateLabel = $"{date:yyyy.MM.dd.}";
        var street = string.Join(" ", new[] { property.Street, property.FloorDoor }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var town = string.Join(" ", new[] { property.PostalCode, property.City }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var address = string.Join(", ", new[] { town, street }.Where(x => x.Length > 0));
        var city = property.City ?? InvoicePdfGenerator.ExtractCity(settings.IssuerAddress);

        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(42);
                page.MarginTop(32);
                page.MarginBottom(26);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").FontColor(Ink).LineHeight(1.3f));

                static void Label(IContainer c, string text) => c.Text(text.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted).LetterSpacing(0.06f);

                page.Content().Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Column(head =>
                    {
                        head.Item().Height(3).Background(Accent);
                        head.Item().PaddingTop(14).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("ÁTADÁS-ÁTVÉTELI JEGYZŐKÖNYV").FontSize(19).Bold().LetterSpacing(-0.01f);
                                c.Item().PaddingTop(3).Text(property.Name).FontSize(11).FontColor(Muted);
                            });
                            row.ConstantItem(160).AlignRight().Column(c =>
                            {
                                c.Item().AlignRight().Element(x => Label(x, "Átadás napja"));
                                c.Item().AlignRight().Text(dateLabel).FontSize(12).Bold();
                            });
                        });
                    });

                    col.Item().Row(row =>
                    {
                        row.Spacing(14);
                        void Party(string title, string? name, string? addr) =>
                            row.RelativeItem().Background(Surface).PaddingVertical(11).PaddingHorizontal(14).Column(c =>
                            {
                                c.Item().Element(x => Label(x, title));
                                c.Item().PaddingTop(5).Text(OrDash(name)).FontSize(12).Bold();
                                c.Item().PaddingTop(2).Text(OrDash(addr));
                            });
                        Party("Átadó", settings.IssuerName, settings.IssuerAddress);
                        Party("Átvevő (bérlő)", tenant.Name, tenant.Address);
                    });

                    col.Item().Column(c =>
                    {
                        c.Item().Element(x => Label(x, "Az ingatlan"));
                        c.Item().PaddingTop(3).Text(string.IsNullOrEmpty(address) ? property.Name : $"{property.Name} — {address}").FontSize(11);
                    });

                    col.Item().Column(section =>
                    {
                        section.Item().Text("Ellenőrzőlista").FontSize(11).Bold();
                        section.Item().PaddingTop(5).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(26);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                            });
                            table.Header(header =>
                            {
                                IContainer Head(IContainer c) => c.BorderBottom(1).BorderColor(Ink).PaddingBottom(5);
                                header.Cell().Element(Head);
                                header.Cell().Element(Head).PaddingRight(12).Element(c => Label(c, "Megnevezés"));
                                header.Cell().Element(Head).Element(c => Label(c, "Megjegyzés"));
                            });
                            foreach (var item in checklist)
                            {
                                IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(6);
                                table.Cell().Element(Cell).AlignTop().PaddingTop(1).Width(11).Height(11).Border(0.75f).BorderColor(Ink);
                                table.Cell().Element(Cell).PaddingRight(12).Text(item.Label).Bold();
                                table.Cell().Element(Cell).Text(item.Note ?? "");
                            }
                        });
                    });

                    col.Item().ShowEntire().Column(c =>
                    {
                        c.Item().Element(x => Label(x, "Egyéb észrevételek"));
                        for (var i = 0; i < 4; i++) c.Item().Height(22).BorderBottom(0.5f).BorderColor(Line);
                    });

                    col.Item().ShowEntire().PaddingTop(10).Column(c =>
                    {
                        c.Item().Text($"Kelt: {(city is not null ? city + ", " : "")}{dateLabel}");
                        c.Item().PaddingTop(30).Row(row =>
                        {
                            row.Spacing(40);
                            void Signature(string role, string? name) => row.RelativeItem().Column(s =>
                            {
                                s.Item().BorderBottom(0.75f).BorderColor(Ink);
                                s.Item().PaddingTop(5).AlignCenter().Text(OrDash(name));
                                s.Item().AlignCenter().Text(role).FontSize(8.5f).FontColor(Muted);
                            });
                            Signature("átadó", settings.IssuerName);
                            Signature("átvevő", tenant.Name);
                        });
                    });
                });

                page.Footer().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text($"Átadás-átvételi jegyzőkönyv — {property.Name}").FontSize(8).FontColor(Muted);
                    row.AutoItem().AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                        text.Span(". oldal");
                    });
                });
            });
        }).GeneratePdf();
    }
}
