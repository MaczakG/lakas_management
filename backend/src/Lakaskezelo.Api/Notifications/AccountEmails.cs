using System.Net;

namespace Lakaskezelo.Api.Notifications;

// A fiókkal kapcsolatos rendszerlevelek szövege (regisztráció, meghívó). A küldés a SystemEmailSender-rel
// megy, a linkek a Frontend:BaseUrl alá mutatnak.
public class AccountEmails(SystemEmailSender sender, IConfiguration configuration)
{
    public string? LastError => sender.LastError;

    private string Url(string page, string token) => $"{configuration["Frontend:BaseUrl"] ?? ""}/{page}?token={token}";

    public Task<bool> SendVerificationAsync(string to, string companyName, string token, CancellationToken ct)
    {
        var url = Url("verify-email.html", token);
        var html = EmailTemplate.Render(
            preheader: "Erősítsd meg az e-mail címed",
            heading: "Erősítsd meg az e-mail címed",
            bodyHtml: $"""
                <p style="margin:0 0 14px;">A(z) <strong>{WebUtility.HtmlEncode(companyName)}</strong> fiókja elkészült. A belépéshez erősítsd meg az e-mail címedet az alábbi gombbal (48 órán belül).</p>
                <p style="margin:0;font-size:13px;color:#64748b;">Ha nem te regisztráltál, hagyd figyelmen kívül ezt az e-mailt — a fiók megerősítés nélkül néhány nap múlva törlődik.</p>
                """,
            cta: ("E-mail cím megerősítése", url));
        var text = $"A(z) {companyName} fiókja elkészült. Erősítsd meg az e-mail címedet (48 órán belül): {url}";
        return sender.SendAsync(to, EmailTemplate.UniqueSubject("Erősítsd meg az e-mail címed"), html, text, ct);
    }

    public Task<bool> SendAlreadyRegisteredAsync(string to, CancellationToken ct)
    {
        var url = $"{configuration["Frontend:BaseUrl"] ?? ""}/forgot-password.html";
        var html = EmailTemplate.Render(
            preheader: "Már van fiókod",
            heading: "Már van fiókod",
            bodyHtml: """<p style="margin:0;">Valaki (remélhetőleg te) ezzel az e-mail címmel próbált új fiókot regisztrálni, de ehhez a címhez már tartozik fiók. Ha nem emlékszel a jelszavadra, az alábbi gombbal újat állíthatsz be. Ha nem te voltál, nincs teendőd.</p>""",
            cta: ("Elfelejtett jelszó", url));
        var text = $"Ehhez az e-mail címhez már tartozik fiók. Elfelejtett jelszó: {url}";
        return sender.SendAsync(to, EmailTemplate.UniqueSubject("Már van fiókod"), html, text, ct);
    }

    public Task<bool> SendInvitationAsync(string to, string inviterName, string companyName, string token, CancellationToken ct)
    {
        var url = Url("accept-invite.html", token);
        var html = EmailTemplate.Render(
            preheader: "Meghívó",
            heading: "Meghívtak",
            bodyHtml: $"""
                <p style="margin:0 0 14px;"><strong>{WebUtility.HtmlEncode(inviterName)}</strong> meghívott a(z) <strong>{WebUtility.HtmlEncode(companyName)}</strong> csapatába ({WebUtility.HtmlEncode(sender.ProductName)}). Az alábbi gombbal állítsd be a jelszavadat (7 napon belül).</p>
                <p style="margin:0;font-size:13px;color:#64748b;">Ha nem számítottál erre a meghívóra, hagyd figyelmen kívül ezt az e-mailt.</p>
                """,
            cta: ("Meghívó elfogadása", url));
        var text = $"{inviterName} meghívott a(z) {companyName} csapatába. Meghívó elfogadása (7 napon belül): {url}";
        return sender.SendAsync(to, EmailTemplate.UniqueSubject("Meghívó"), html, text, ct);
    }
}
