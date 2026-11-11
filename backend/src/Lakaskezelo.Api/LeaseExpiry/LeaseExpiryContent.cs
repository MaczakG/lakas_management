using System.Net;
using Lakaskezelo.Api.Notifications;

namespace Lakaskezelo.Api.LeaseExpiry;

// A bérleti szerződések lejáratáról szóló értesítő: kinek kell szólni, és a levél tartalma.
public static class LeaseExpiryContent
{
    public const string Subject = "Lejáró bérleti szerződések";
    public const int MaxNoticeDays = 365;

    public record Item(string TenantName, string? PropertyName, DateOnly LeaseEnd);

    public record Mail(string Subject, string Preheader, string Html, string Text);

    // Kell-e szólni a bérlő szerződése miatt: van lejárat, az a megadott napon belül van (vagy már elmúlt), erről a
    // dátumról még nem ment értesítő, és a bérlő nem költözött ki.
    public static bool IsDue(DateOnly? leaseEnd, DateOnly? noticeFor, DateOnly? moveOut, DateOnly today, int noticeDays) =>
        leaseEnd is { } end && noticeFor != end && (moveOut is null || moveOut > today) && end <= today.AddDays(noticeDays);

    // „45 nap múlva jár le” / „ma jár le” / „12 napja lejárt”.
    public static string When(DateOnly leaseEnd, DateOnly today)
    {
        var days = leaseEnd.DayNumber - today.DayNumber;
        return days > 0 ? $"{days} nap múlva jár le" : days == 0 ? "ma jár le" : $"{-days} napja lejárt";
    }

    public static Mail Build(IReadOnlyList<Item> items, DateOnly today, string? baseUrl)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        string Line(Item item) =>
            $"{item.TenantName}{(string.IsNullOrWhiteSpace(item.PropertyName) ? "" : $" ({item.PropertyName})")}: "
            + $"{item.LeaseEnd:yyyy.MM.dd.}, {When(item.LeaseEnd, today)}";

        var ordered = items.OrderBy(i => i.LeaseEnd).ThenBy(i => i.TenantName).ToList();
        const string intro = "A következő bérleti szerződések hamarosan lejárnak, vagy már lejártak:";
        const string outro = "Hosszabbítás után írd át a lejáratot a bérlő adatlapján; az új dátumról a program újra szól. "
            + "Egy lejáratról egyszer megy értesítő.";
        var tenantsUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/tenants.html";

        var html = $"""<p style="margin:0 0 12px;">{E(intro)}</p>"""
            + $"""<ul style="margin:0 0 12px;padding-left:20px;">{string.Concat(ordered.Select(i => $"<li>{E(Line(i))}</li>"))}</ul>"""
            + $"""<p style="margin:0 0 12px;">{E(outro)}</p>""";
        var text = intro + "\n\n" + string.Join("\n", ordered.Select(i => $"- {Line(i)}")) + "\n\n" + outro
            + (tenantsUrl is null ? "" : $"\n\nBérlők: {tenantsUrl}");

        var preheader = ordered.Count == 1 ? Line(ordered[0]) : $"{ordered.Count} bérleti szerződés lejárata közeleg";
        (string Text, string Url)? cta = tenantsUrl is null ? null : ("Bérlők megnyitása", tenantsUrl);
        return new Mail(Subject, preheader, EmailTemplate.Render(preheader, "Lejáró bérleti szerződések", html, cta), text);
    }
}
