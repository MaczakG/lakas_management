using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.LeaseExpiry;

// Naponta egyszer (helyi idő 9:00-kor) megnézi cégenként a bérlők szerződésének lejáratát (Tenant.LeaseEndDate). Ha
// egy lejárat a Beállításokban megadott napon belül van (vagy már elmúlt), és arról a dátumról még nem ment értesítő,
// egy összesített levél megy a Beállításokban megadott címekre. Egy lejáratról egyszer szól; ha a dátum módosul
// (hosszabbítás), az újról megint.
public class LeaseExpiryReminderService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<LeaseExpiryReminderService> logger) : BackgroundService
{
    // Az ellenőrzés órája (helyi idő); a LeaseExpiry:CheckHour beállítással felülírható (pl. helyi próbához).
    private int CheckHour => configuration.GetValue("LeaseExpiry:CheckHour", 9);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                if (BudapestClock.Now.Hour == CheckHour)
                {
                    await RunOnceAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LeaseExpiryReminderService tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Cégenként, a cég saját beállításaival — külön scope-ban, hogy a cégek adatai ne keveredjenek.
    private async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> companyIds;
        using (var listScope = scopeFactory.CreateScope())
        {
            companyIds = await listScope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>()
                .Companies.Select(c => c.Id).ToListAsync(ct);
        }

        foreach (var companyId in companyIds)
        {
            try
            {
                await RunForCompanyAsync(companyId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lease expiry reminder failed for company {CompanyId}", companyId);
            }
        }
    }

    private async Task RunForCompanyAsync(Guid companyId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
        var settings = await scope.ServiceProvider.GetRequiredService<AppSettingsService>().GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.LeaseExpiryEmails)) return;
        // Nem fizető (zárolt) cégnek nem küldünk értesítőt.
        if (await scope.ServiceProvider.GetRequiredService<Subscriptions.SubscriptionAccess>().IsLockedAsync(companyId, ct)) return;

        var today = DateOnly.FromDateTime(BudapestClock.Now);
        var limit = today.AddDays(settings.LeaseExpiryNoticeDays);
        var due = (await db.Tenants.Include(t => t.Property)
                .Where(t => t.LeaseEndDate != null && t.LeaseEndDate <= limit)
                .ToListAsync(ct))
            .Where(t => LeaseExpiryContent.IsDue(t.LeaseEndDate, t.LeaseExpiryNoticeFor, t.MoveOutDate, today, settings.LeaseExpiryNoticeDays))
            .ToList();
        if (due.Count == 0) return;

        var mail = LeaseExpiryContent.Build(
            [.. due.Select(t => new LeaseExpiryContent.Item(t.Name, t.Property?.Name, t.LeaseEndDate!.Value))],
            today, configuration["Frontend:BaseUrl"]);
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        if (!await emailSender.SendAsync(settings.LeaseExpiryEmails, EmailTemplate.UniqueSubject(mail.Subject), mail.Html, mail.Text, null, ct))
        {
            logger.LogWarning("Failed to send lease expiry reminder: {Error}", emailSender.LastError);
            return;
        }

        foreach (var tenant in due) tenant.LeaseExpiryNoticeFor = tenant.LeaseEndDate;
        await db.SaveChangesAsync(ct);
    }
}
