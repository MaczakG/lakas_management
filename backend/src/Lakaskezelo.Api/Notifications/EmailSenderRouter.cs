using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Notifications;

// A cégek (bérlőknek szóló) leveleinek küldője — számla, rezsi-emlékeztető. A kézbesítés a platform
// e-mail fiókján megy (Platform admin → E-mail és tárhely: Mailgun vagy SMTP), de a feladó neve a cég
// számlakibocsátó neve (vagy a cég neve), a válaszcím pedig a cég e-mail címe (Beállítások → Számla-kibocsátó
// adatai; ha üres, az első adminisztrátoré) — így a bérlő válasza a bérbeadóhoz megy, nem az üzemeltetőhöz.
//
// Teszt-környezetben (Email:OnlyToUsers=true, ld. deploy/docker-compose.yml api-staging) csak a
// rendszer felhasználóinak megy ki levél (pl. a belépési kód) — minden más (számla, rezsi-emlékeztető)
// csak naplózódik, és sikeresnek számít, hogy a teszt-adatokból soha ne kapjon levelet valódi címzett.
public class EmailSenderRouter(
    MailgunEmailSender mailgun, SmtpEmailSender smtp, AppSettingsService settingsService,
    LakaskezeloDbContext db, IConfiguration configuration, ILogger<EmailSenderRouter> logger) : IEmailSender
{
    public string? LastError { get; private set; }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var platform = await settingsService.GetPlatformAsync(ct);
        return platform.EmailProvider == EmailProvider.Smtp ? await smtp.IsConfiguredAsync(ct) : await mailgun.IsConfiguredAsync(ct);
    }

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, EmailAttachment? attachment, CancellationToken ct)
    {
        if (await IsSuppressedAsync(db, configuration, to, ct))
        {
            logger.LogInformation("E-mail suppressed (Email:OnlyToUsers): {Subject}", subject);
            LastError = null;
            return true;
        }

        var platform = await settingsService.GetPlatformAsync(ct);
        var company = await settingsService.GetAsync(ct);
        var fromName = company.IssuerName is { Length: > 0 } issuer ? issuer
            : await db.Companies.Where(c => c.Id == company.CompanyId).Select(c => c.Name).SingleOrDefaultAsync(ct);
        var replyTo = company.IssuerEmail is { Length: > 0 } issuerEmail ? issuerEmail
            : await db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive && u.EmailVerifiedAt != null)
                .OrderBy(u => u.CreatedAt).Select(u => u.Email).FirstOrDefaultAsync(ct);

        bool result;
        if (platform.EmailProvider == EmailProvider.Smtp)
        {
            result = await smtp.SendWithAsync(platform, fromName, replyTo, to, subject, htmlBody, textBody, attachment, ct);
            LastError = smtp.LastError;
        }
        else
        {
            result = await mailgun.SendWithAsync(platform, fromName, replyTo, to, subject, htmlBody, textBody, attachment, ct);
            LastError = mailgun.LastError;
        }
        return result;
    }

    // Teszt-környezetben csak a rendszer felhasználóinak (köztük a még meg nem erősített regisztrálóknak
    // és meghívottaknak) megy levél.
    internal static async Task<bool> IsSuppressedAsync(LakaskezeloDbContext db, IConfiguration configuration, string to, CancellationToken ct)
    {
        if (!configuration.GetValue("Email:OnlyToUsers", false)) return false;
        var normalized = to.Trim().ToLowerInvariant();
        return !await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalized, ct)
            && !await db.PlatformAdmins.AnyAsync(a => a.Email == normalized, ct);
    }
}
