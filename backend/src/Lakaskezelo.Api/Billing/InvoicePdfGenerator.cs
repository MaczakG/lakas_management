using System.Text.RegularExpressions;
using Lakaskezelo.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lakaskezelo.Api.Billing;

// A „Számviteli bizonylat” PDF-je. Az adattartalom a felhasználó eredeti sablonjáé (kiállító, bérbe vevő, a gazdasági
// esemény megnevezése, tételek, árfolyam-megjegyzés, a bérleti díj jogosultjai, kelt, aláírás); az elrendezés
// áttekinthetőbb: fejléc a bizonylat adataival, a két fél egymás mellett, a tételek táblázata jobbra zárt
// összegekkel és kiemelt végösszeggel. Ha a sablon később finomodik, ez az egyetlen fájl, amit módosítani kell — a
// hívó oldal (InvoiceGenerationService, InvoicePdfStore) csak a visszaadott byte[]-tel dolgozik.
public static class InvoicePdfGenerator
{
    private static readonly string[] MonthNames =
        ["január", "február", "március", "április", "május", "június",
         "július", "augusztus", "szeptember", "október", "november", "december"];

    // Színek: sötét szöveg, egy halvány felület és vonal; a kiemelés (felső sáv, a végösszeg széle) sötétszürke,
    // sztornónál piros.
    private const string Ink = "#18181B";
    private const string Muted = "#6B7280";
    private const string Line = "#E4E4E7";
    private const string Surface = "#F4F4F5";
    private const string Accent = "#3F3F46";
    private const string Danger = "#B91C1C";

    // A tételeket külön paraméterként kapja, nem az invoice.Lines navigációból olvassa — az
    // InvoiceGenerationService szándékosan nem tölti fel azt a listát (ld. ottani megjegyzés).
    // Sztornó bizonylatnál (invoice.IsStorno) a cancelsNumber az eredeti (sztornózott) bizonylat száma; a sorok
    // ilyenkor az eredeti sorok negatív összegekkel.
    public static byte[] Generate(Invoice invoice, Property property, Tenant? tenant, AppSettings settings, List<InvoiceLine> lines, string? cancelsNumber = null)
    {
        var periodLabel = $"{invoice.PeriodYear}. {MonthNames[invoice.PeriodMonth - 1]}";
        var issuerCity = ExtractCity(settings.IssuerAddress);
        var issuedLabel = $"{invoice.IssuedAt:yyyy.MM.dd.}";
        var accent = invoice.IsStorno ? Danger : Accent;
        static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(42);
                page.MarginTop(32);
                page.MarginBottom(26);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").FontColor(Ink).LineHeight(1.3f));

                // Kis, nagybetűs címke (mezőnév, táblázatfej).
                static void Label(IContainer c, string text) => c.Text(text.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted).LetterSpacing(0.06f);

                page.Content().Column(col =>
                {
                    col.Spacing(14);

                    // ---------- fejléc: a bizonylat neve és azonosító adatai ----------
                    col.Item().Column(head =>
                    {
                        head.Item().Height(3).Background(accent);
                        head.Item().PaddingTop(14).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                // Sztornónál a „SZTORNÓ” külön, kiemelt sorban áll a név fölött (egy sorban nem férne ki).
                                if (invoice.IsStorno) c.Item().PaddingBottom(1).Text("SZTORNÓ").FontSize(11).Bold().FontColor(Danger).LetterSpacing(0.08f);
                                c.Item().Text("SZÁMVITELI BIZONYLAT").FontSize(19).Bold().LetterSpacing(-0.01f);
                                c.Item().PaddingTop(3).Text(periodLabel).FontSize(11).FontColor(Muted);
                            });
                            row.ConstantItem(236).Table(meta =>
                            {
                                meta.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(118);
                                });
                                void Meta(string label, string value, bool strong = false)
                                {
                                    meta.Cell().PaddingVertical(2).AlignRight().AlignMiddle().Element(c => Label(c, label));
                                    var text = meta.Cell().PaddingVertical(2).PaddingLeft(10).AlignRight().Text(value);
                                    if (strong) text.FontSize(12).Bold();
                                }
                                Meta("Bizonylat száma", invoice.Number, strong: true);
                                if (invoice.IsStorno) Meta("Sztornózott bizonylat", OrDash(cancelsNumber));
                                Meta("Időszak", periodLabel);
                                Meta("Kelt", issuedLabel);
                            });
                        });
                    });

                    // ---------- a két fél ----------
                    col.Item().Row(row =>
                    {
                        row.Spacing(14);
                        void Party(string title, string? name, string? address, string extraLabel, string? extraValue) =>
                            row.RelativeItem().Background(Surface).PaddingVertical(11).PaddingHorizontal(14).Column(c =>
                            {
                                c.Item().Element(x => Label(x, title));
                                c.Item().PaddingTop(5).Text(OrDash(name)).FontSize(12).Bold();
                                c.Item().PaddingTop(2).Text(OrDash(address));
                                c.Item().PaddingTop(6).Element(x => Label(x, extraLabel));
                                c.Item().PaddingTop(1).Text(OrDash(extraValue));
                            });
                        Party("Bizonylat kiállító", settings.IssuerName, settings.IssuerAddress, "Bankszámlaszám", settings.IssuerBankAccount);
                        Party("Bérbe vevő", tenant?.Name, tenant?.Address, "Adószám", tenant?.TaxId);
                    });

                    // ---------- a gazdasági esemény ----------
                    col.Item().Column(c =>
                    {
                        c.Item().Element(x => Label(x, "Gazdasági esemény megnevezése"));
                        c.Item().PaddingTop(3).Text(invoice.IsStorno
                            ? $"{property.Name} bérleti díja — a(z) {cancelsNumber} számú bizonylat sztornója"
                            : $"{property.Name} bérleti díja").FontSize(11);
                    });

                    // ---------- tételek ----------
                    col.Item().Column(items =>
                    {
                        items.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(130);
                            });

                            table.Header(header =>
                            {
                                header.Cell().BorderBottom(1).BorderColor(Ink).PaddingBottom(5).Element(c => Label(c, "Megnevezés"));
                                header.Cell().BorderBottom(1).BorderColor(Ink).PaddingBottom(5).AlignRight().Element(c => Label(c, "Összeg"));
                            });

                            foreach (var line in lines)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4.5f).PaddingRight(12).Text(line.Label);
                                table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4.5f).AlignRight().Text(MoneyFormat.Huf(line.Amount));
                            }
                        });

                        // A végösszeg kiemelve, a táblázat alatt jobbra (egyetlen tételnél is, hogy egy pillantással látsszon).
                        items.Item().ShowEntire().PaddingTop(8).AlignRight().Width(250).Background(Surface).BorderLeft(3).BorderColor(accent)
                            .PaddingVertical(8).PaddingHorizontal(14).Row(total =>
                            {
                                total.RelativeItem().AlignMiddle().Text("Összesen").FontSize(11).Bold();
                                total.AutoItem().AlignMiddle().Text(MoneyFormat.Huf(lines.Sum(l => l.Amount))).FontSize(15).Bold();
                            });

                        if (!string.IsNullOrWhiteSpace(invoice.RentConversionNote))
                        {
                            items.Item().PaddingTop(6).Text(invoice.RentConversionNote).FontSize(8.5f).FontColor(Muted);
                        }
                    });

                    // ---------- a bérleti díj jogosultjai ----------
                    if (property.PropertyOwners.Count > 0)
                    {
                        var owners = property.PropertyOwners;
                        // A tulajdonosnál megadott hányad; ha nincs megadva, egyenlő arányt feltételezünk.
                        string ShareOf(PropertyOwner po) => string.IsNullOrWhiteSpace(po.Share) ? $"1/{owners.Count}" : po.Share;
                        var shareLabel = string.Join("-", owners.Select(ShareOf));

                        col.Item().Column(section =>
                        {
                            section.Item().Text($"Bérleti díj jogosultjai {shareLabel} arányban").FontSize(11).Bold();
                            section.Item().PaddingTop(5).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(4);
                                    columns.RelativeColumn(2.2f);
                                    columns.RelativeColumn(1.8f);
                                });

                                table.Header(header =>
                                {
                                    IContainer Head(IContainer c) => c.BorderBottom(0.75f).BorderColor(Ink).PaddingBottom(4);
                                    header.Cell().Element(Head).PaddingRight(8).Element(c => Label(c, "Név"));
                                    header.Cell().Element(Head).PaddingRight(12).Element(c => Label(c, "Lakcím"));
                                    header.Cell().Element(Head).PaddingRight(8).Element(c => Label(c, "Adóazonosító jele"));
                                    header.Cell().Element(Head).AlignRight().Element(c => Label(c, "Tulajdoni hányad"));
                                });

                                // Kisebb betűméret és cellánkénti jobb oldali térköz, hogy a hosszabb
                                // lakcím ne folyjon bele a szomszédos adóazonosító oszlopba.
                                foreach (var po in owners)
                                {
                                    IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4);
                                    table.Cell().Element(Cell).PaddingRight(8).Text(po.Owner!.Name).FontSize(9.5f);
                                    table.Cell().Element(Cell).PaddingRight(12).Text(OrDash(po.Owner!.Address)).FontSize(9.5f);
                                    table.Cell().Element(Cell).PaddingRight(8).Text(OrDash(po.Owner!.TaxId)).FontSize(9.5f);
                                    table.Cell().Element(Cell).AlignRight().Text(ShareOf(po)).FontSize(9.5f);
                                }
                            });
                        });
                    }

                    // ---------- kelt és aláírás ----------
                    col.Item().ShowEntire().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem().AlignBottom().Text($"Kelt: {(issuerCity is not null ? issuerCity + ", " : "")}{issuedLabel}");
                        row.ConstantItem(210).Column(c =>
                        {
                            c.Item().Height(26);
                            c.Item().BorderBottom(0.75f).BorderColor(Ink);
                            c.Item().PaddingTop(5).AlignCenter().Text(settings.IssuerName ?? "");
                        });
                    });
                });

                // Lábléc: a bizonylat száma és az oldalszám (több oldalas bizonylatnál is azonosítható minden lap).
                page.Footer().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text(invoice.Number).FontSize(8).FontColor(Muted);
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                        text.Span(". oldal");
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
