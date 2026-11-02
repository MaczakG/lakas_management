using System.Globalization;
using System.Net;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Subscriptions;

// Az előfizetéssel kapcsolatos rendszerlevelek a cég (aktív, megerősített) adminisztrátorainak.
public class SubscriptionEmails(LakaskezeloDbContext db, SystemEmailSender sender, IConfiguration configuration, ILogger<SubscriptionEmails> logger)
{
    private static readonly CultureInfo Hu = CultureInfo.GetCultureInfo("hu-HU");
    private string SubscriptionUrl => $"{configuration["Frontend:BaseUrl"] ?? ""}/subscription.html";

    private static string Date(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Billing.BudapestClock.Zone).ToString("yyyy. MM. dd.", Hu);
    public static string Money(decimal amount) => amount.ToString("#,0", Hu) + " Ft";

    private async Task SendToAdminsAsync(Guid companyId, string subject, string heading, string bodyHtml, string text, CancellationToken ct)
    {
        var recipients = await db.Users.IgnoreQueryFilters()
            .Where(u => u.CompanyId == companyId && u.IsActive && u.EmailVerifiedAt != null && u.Role == UserRole.Admin)
            .Select(u => u.Email).ToListAsync(ct);
        foreach (var to in recipients)
        {
            var html = EmailTemplate.Render(preheader: heading, heading: heading, bodyHtml: bodyHtml, cta: ("Előfizetés", SubscriptionUrl));
            if (!await sender.SendAsync(to, EmailTemplate.UniqueSubject(subject), html, text + $"\n{SubscriptionUrl}", ct))
            {
                logger.LogWarning("Subscription e-mail '{Subject}' could not be sent to company {CompanyId}: {Error}", subject, companyId, sender.LastError);
            }
        }
    }

    public Task PaymentSucceededAsync(SubscriptionPayment p, CancellationToken ct)
    {
        var invoice = p.BillingoPublicUrl is { } url
            ? $"""<p style="margin:0 0 14px;">A számlát itt töltheted le: <a href="{WebUtility.HtmlEncode(url)}">számla</a>.</p>"""
            : """<p style="margin:0 0 14px;">A számlát hamarosan e-mailben is megkapod, és az Előfizetés oldalon is megtalálod.</p>""";
        return SendToAdminsAsync(p.CompanyId, "Sikeres fizetés", "Köszönjük a fizetést!",
            $"""
             <p style="margin:0 0 14px;">Sikeresen terheltük a kártyádat <strong>{Money(p.GrossAmount)}</strong> összeggel ({p.PropertyCount} ingatlan, {Date(p.PeriodStart)} – {Date(p.PeriodEnd)}).</p>
             {invoice}
             """,
            $"Sikeresen terheltük a kártyádat {Money(p.GrossAmount)} összeggel ({Date(p.PeriodStart)} – {Date(p.PeriodEnd)}). {p.BillingoPublicUrl}", ct);
    }

    public Task PaymentFailedAsync(Subscription s, string? reason, CancellationToken ct) =>
        SendToAdminsAsync(s.CompanyId, "Sikertelen fizetés", "Nem sikerült megújítani az előfizetést",
            $"""
             <p style="margin:0 0 14px;">Az előfizetés megújításakor nem sikerült terhelni a kártyádat{(reason is null ? "" : $" ({WebUtility.HtmlEncode(reason)})")}.</p>
             <p style="margin:0;">A rendszer {(s.GraceEndsAt is { } g ? Date(g) : "néhány nap")}-ig még használható; utána csak olvasható lesz. Az Előfizetés oldalon új kártyával fizethetsz.</p>
             """,
            $"Nem sikerült megújítani az előfizetést. A rendszer {(s.GraceEndsAt is { } g2 ? Date(g2) : "néhány nap")}-ig használható; az Előfizetés oldalon fizethetsz.", ct);

    public Task TrialEndingAsync(Subscription s, CancellationToken ct) =>
        SendToAdminsAsync(s.CompanyId, "Hamarosan lejár a próbaidő", "Hamarosan lejár a próbaidő",
            $"""<p style="margin:0;">Az ingyenes próbaidőd {Date(s.TrialEndsAt)}-án lejár. Ha tovább szeretnéd használni a rendszert, az Előfizetés oldalon add meg a kártyádat — a díjat csak a próbaidő vége után számoljuk.</p>""",
            $"Az ingyenes próbaidőd {Date(s.TrialEndsAt)}-án lejár. Az Előfizetés oldalon fizethetsz elő.", ct);

    public Task LockedAsync(Subscription s, string reason, CancellationToken ct) =>
        SendToAdminsAsync(s.CompanyId, "A fiók csak olvasható", "A fiók csak olvasható lett",
            $"""<p style="margin:0;">{WebUtility.HtmlEncode(reason)} Az adataid megmaradtak és megtekinthetők, de módosítani és számlát kiküldeni csak előfizetés után lehet. Az Előfizetés oldalon néhány kattintással újraindíthatod.</p>""",
            $"{reason} Az adataid megmaradtak; az Előfizetés oldalon újraindíthatod.", ct);
}
