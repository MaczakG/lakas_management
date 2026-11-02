using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Notifications;

// Az egyetlen IEmailSender, ami ténylegesen DI-be van regisztrálva (ld. Program.cs) — a Beállítások
// oldalon kiválasztott AppSettings.EmailProvider alapján a MailgunEmailSender vagy a
// SmtpEmailSender felé delegál. Mindkét konfiguráció megmaradhat egyszerre a Beállításokban,
// csak az egyik van ténylegesen használatban küldéskor.
//
// Teszt-környezetben (Email:OnlyToUsers=true, ld. deploy/docker-compose.yml api-staging) csak a
// rendszer felhasználóinak megy ki levél (pl. a belépési kód) — minden más (számla, rezsi-emlékeztető)
// csak naplózódik, és sikeresnek számít, hogy a teszt-adatokból soha ne kapjon levelet valódi címzett.
public class EmailSenderRouter(
    MailgunEmailSender mailgun, SmtpEmailSender smtp, AppSettingsService settingsService,
    LakaskezeloDbContext db, IConfiguration configuration, ILogger<EmailSenderRouter> logger) : IEmailSender
{
    public string? LastError { get; private set; }

    private async Task<IEmailSender> ResolveAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        return settings.EmailProvider == EmailProvider.Smtp ? smtp : mailgun;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default) => await (await ResolveAsync(ct)).IsConfiguredAsync(ct);

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, EmailAttachment? attachment, CancellationToken ct)
    {
        if (configuration.GetValue("Email:OnlyToUsers", false))
        {
            var normalized = to.Trim().ToLowerInvariant();
            if (!await db.Users.AnyAsync(u => u.Email == normalized, ct))
            {
                logger.LogInformation("E-mail suppressed (Email:OnlyToUsers): {Subject}", subject);
                LastError = null;
                return true;
            }
        }

        var sender = await ResolveAsync(ct);
        var result = await sender.SendAsync(to, subject, htmlBody, textBody, attachment, ct);
        LastError = sender.LastError;
        return result;
    }
}
