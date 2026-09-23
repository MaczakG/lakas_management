using Lakaskezelo.Api.Settings;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Notifications;

// Az egyetlen IEmailSender, ami ténylegesen DI-be van regisztrálva (ld. Program.cs) — a Beállítások
// oldalon kiválasztott AppSettings.EmailProvider alapján a MailgunEmailSender vagy a
// SmtpEmailSender felé delegál. Mindkét konfiguráció megmaradhat egyszerre a Beállításokban,
// csak az egyik van ténylegesen használatban küldéskor.
public class EmailSenderRouter(MailgunEmailSender mailgun, SmtpEmailSender smtp, AppSettingsService settingsService) : IEmailSender
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
        var sender = await ResolveAsync(ct);
        var result = await sender.SendAsync(to, subject, htmlBody, textBody, attachment, ct);
        LastError = sender.LastError;
        return result;
    }
}
