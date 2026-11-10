using System.Net;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Notifications;

namespace Lakaskezelo.Api.UtilityReminder;

// A rezsi-emlékeztető levél tartalma: ingatlanonként, hogy a következő bizonylat előtt mi a teendő a rezsivel.
public static class UtilityReminderContent
{
    public const string Subject = "Rezsi: kitöltendő tételek";

    private static readonly string[] HungarianMonths =
        ["január", "február", "március", "április", "május", "június", "július", "augusztus", "szeptember", "október", "november", "december"];

    // State: a következő bizonylat hónapjának állapota (UtilityPeriods.Ok / Pending / Missing).
    // PendingNow: a hónap kitöltendő (összeg nélküli) tételei. PendingEarlier: korábbi hónapokból még kitöltetlen tételek.
    public record Item(string PropertyName, DateOnly Period, DateTime BillingAt, string State,
        IReadOnlyList<string> PendingNow, IReadOnlyList<(string Label, int Year, int Month)> PendingEarlier);

    public record Mail(string Subject, string Preheader, string Html, string Text);

    // Kell-e szólni az ingatlan miatt: a hónap rezsije hiányos, vagy korábbról maradt kitöltetlen tétel.
    public static bool NeedsReminder(string state, int earlierPendingCount) => state != UtilityPeriods.Ok || earlierPendingCount > 0;

    public static Mail Build(IReadOnlyList<Item> items, string? baseUrl)
    {
        static string Period(int year, int month) => $"{year}. {HungarianMonths[month - 1]}";
        static string E(string s) => WebUtility.HtmlEncode(s);

        // Egy ingatlan teendői sima szövegként (a HTML ugyanezekből a sorokból épül).
        static List<string> Lines(Item item)
        {
            var lines = new List<string>();
            if (item.PendingNow.Count > 0) lines.Add($"Kitöltendő: {string.Join(", ", item.PendingNow)}");
            else if (item.State == UtilityPeriods.Missing) lines.Add("Erre a hónapra még nincs rögzített rezsi.");
            else if (item.State == UtilityPeriods.Pending) lines.Add("Van kitöltendő tétel.");
            if (item.PendingEarlier.Count > 0)
            {
                lines.Add("Korábbi hónapból kitöltendő: "
                    + string.Join(", ", item.PendingEarlier.Select(p => $"{p.Label} ({Period(p.Year, p.Month)})")));
            }
            return lines;
        }

        string Title(Item item) => $"{item.PropertyName}: {Period(item.Period.Year, item.Period.Month)} (kiállítás: {item.BillingAt:yyyy.MM.dd. HH:mm})";

        const string intro = "A következő ingatlanoknál teendő van a rezsivel a soron következő bizonylat előtt:";
        const string outro = "A bizonylat ettől függetlenül kimegy a beállított időben. Az összeg nélküli tétel nem kerül rá; "
            + "ha később kitöltöd (például amikor megjön a szolgáltató számlája), a következő bizonylatra kerül.";

        var html = $"""<p style="margin:0 0 12px;">{E(intro)}</p>"""
            + string.Concat(items.Select(item =>
                $"""<p style="margin:0 0 4px;"><strong>{E(Title(item))}</strong></p>"""
                + $"""<ul style="margin:0 0 12px;padding-left:20px;">{string.Concat(Lines(item).Select(l => $"<li>{E(l)}</li>"))}</ul>"""))
            + $"""<p style="margin:0 0 12px;">{E(outro)}</p>""";
        var text = intro + "\n\n"
            + string.Join("\n\n", items.Select(item => Title(item) + "\n" + string.Join("\n", Lines(item).Select(l => $"- {l}"))))
            + "\n\n" + outro
            + (string.IsNullOrWhiteSpace(baseUrl) ? "" : $"\n\nRezsi felvétele: {baseUrl.TrimEnd('/')}/utility-entry.html");

        var preheader = $"{items.Count} ingatlannál van teendő a rezsivel a következő bizonylat előtt";
        (string Text, string Url)? cta = string.IsNullOrWhiteSpace(baseUrl) ? null : ("Rezsi felvétele", $"{baseUrl.TrimEnd('/')}/utility-entry.html");
        return new Mail(Subject, preheader, EmailTemplate.Render(preheader, "Kitöltendő rezsi tételek", html, cta), text);
    }
}
