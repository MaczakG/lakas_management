using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Notifications;

// Rendszerlevelek (belépési kód, jelszó-visszaállítás, regisztráció megerősítése, meghívó, zárolás) —
// ezek NEM a felhasználó cégének e-mail beállításaival mennek ki (egy frissen regisztrált cégnek még
// nincs is ilyen), hanem a platform üzemeltetőjének postafiókjából: a Platform:EmailCompanyId cég
// (alapból az első, a SaaS előtti cég) Beállításokban megadott SMTP/Mailgun adataival, a termék
// nevével (App:ProductName) mint feladónévvel.
public class SystemEmailSender(
    MailgunEmailSender mailgun, SmtpEmailSender smtp, AppSettingsService settingsService,
    LakaskezeloDbContext db, IConfiguration configuration, ILogger<SystemEmailSender> logger)
{
    public string? LastError { get; private set; }

    public string ProductName => configuration["App:ProductName"] is { Length: > 0 } name ? name : "Lakáskezelő";

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken ct)
    {
        if (await EmailSenderRouter.IsSuppressedAsync(db, configuration, to, ct))
        {
            logger.LogInformation("System e-mail suppressed (Email:OnlyToUsers): {Subject}", subject);
            LastError = null;
            return true;
        }

        var companyId = configuration.GetValue("Platform:EmailCompanyId", Company.DefaultCompanyId);
        var settings = await settingsService.GetForCompanyAsync(companyId, ct);
        bool sent;
        if (settings.EmailProvider == EmailProvider.Smtp)
        {
            sent = await smtp.SendWithAsync(settings, ProductName, to, subject, htmlBody, textBody, null, ct);
            LastError = smtp.LastError;
        }
        else
        {
            sent = await mailgun.SendWithAsync(settings, ProductName, to, subject, htmlBody, textBody, null, ct);
            LastError = mailgun.LastError;
        }
        return sent;
    }
}
