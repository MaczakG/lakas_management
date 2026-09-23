using Lakaskezelo.Api.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Lakaskezelo.Api.Notifications;

// A workspace (pl. Google Workspace) SMTP-szerverén keresztül küld sima SMTP-hitelesítéssel
// (host/port/felhasználónév/jelszó — Google Workspace esetén tipikusan alkalmazásjelszóval),
// NEM OAuth/Gmail API-n keresztül. A hitelesítő adatokat minden híváskor frissen olvassa az
// AppSettingsService-ből, mint a MailgunEmailSender — mert a Beállítások oldalon bármikor
// módosíthatók.
public class SmtpEmailSender(AppSettingsService settingsService, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public string? LastError { get; private set; }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        return IsConfigured(settings.SmtpHost, settings.SmtpUsername, settings.SmtpPassword, settings.SmtpFromAddress);
    }

    private static bool IsConfigured(string? host, string? username, string? password, string? from) =>
        !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(username)
        && !string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(from);

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, EmailAttachment? attachment, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (!IsConfigured(settings.SmtpHost, settings.SmtpUsername, settings.SmtpPassword, settings.SmtpFromAddress))
        {
            LastError = "SMTP nincs beállítva (szerver / felhasználónév / jelszó / feladó hiányzik a Beállításokban).";
            logger.LogInformation("SMTP not configured — skipping email to {To}: {Subject}", to, subject);
            return false;
        }

        try
        {
            var fromName = string.IsNullOrWhiteSpace(settings.SmtpFromName) ? "Lakáskezelő" : settings.SmtpFromName;
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, settings.SmtpFromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };
            if (attachment is not null)
            {
                builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.MimeType));
            }
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            // SecureSocketOptions.Auto: 465-nél implicit TLS-t, 587/25-nél STARTTLS-t választ, ha a
            // szerver támogatja — ez fedi a Google Workspace SMTP-relay (587) és a smtp.gmail.com
            // (465/587) beállításokat is.
            await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort ?? 587, SecureSocketOptions.Auto, ct);
            await client.AuthenticateAsync(settings.SmtpUsername, settings.SmtpPassword, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"SMTP kivétel: {ex.Message}";
            logger.LogError(ex, "Failed to send email to {To} via SMTP", to);
            return false;
        }
    }
}
