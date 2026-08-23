using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.UtilityReminder;

// Naponta egyszer (helyi idő 9:00-kor) ellenőrzi: ha a mai nap eléri a Beállításokban megadott
// rögzítési határidőt, és egy aktív ingatlanhoz még nincs rezsi tétel a folyó hónapra, és még nem
// ment ki érte emlékeztető ebben a hónapban (UtilityReminderLog) — egy összesített digest e-mailt
// küld a rezsi kontaktnak az összes érintett ingatlannal, majd naplózza, hogy ne menjen ki
// minden nap újra ugyanazért a hónapért.
public class UtilityReminderService(IServiceScopeFactory scopeFactory, ILogger<UtilityReminderService> logger) : BackgroundService
{
    private const int CheckHour = 9;
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
                logger.LogError(ex, "UtilityReminderService tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.UtilityContactEmail)) return;

        var now = BudapestClock.Now;
        if (now.Day < settings.UtilityDeadlineDay) return;

        var properties = await db.Properties.Where(p => p.IsActive).ToListAsync(ct);
        var alreadyReminded = await db.UtilityReminderLogs
            .Where(l => l.Year == now.Year && l.Month == now.Month)
            .Select(l => l.PropertyId)
            .ToListAsync(ct);
        var propertiesWithEntries = await db.UtilityCostEntries
            .Where(u => u.Year == now.Year && u.Month == now.Month)
            .Select(u => u.PropertyId)
            .Distinct()
            .ToListAsync(ct);

        var missing = properties
            .Where(p => !propertiesWithEntries.Contains(p.Id) && !alreadyReminded.Contains(p.Id))
            .ToList();

        if (missing.Count == 0) return;

        var listHtml = string.Join("", missing.Select(p => $"<li>{System.Net.WebUtility.HtmlEncode(p.Name)}</li>"));
        var listText = string.Join("\n", missing.Select(p => $"- {p.Name}"));
        var periodLabel = $"{now.Year}.{now.Month:D2}";

        var htmlBody = EmailTemplate.Render(
            preheader: $"{missing.Count} ingatlanhoz hiányzik a(z) {periodLabel} havi rezsi",
            heading: "Hiányzó rezsi tételek",
            bodyHtml: $"""
                <p style="margin:0 0 12px;">A(z) {periodLabel} időszakra a következő ingatlanokhoz még nincs rezsi tétel rögzítve:</p>
                <ul style="margin:0 0 12px;padding-left:20px;">{listHtml}</ul>
                """);
        var textBody = $"A(z) {periodLabel} időszakra hiányzó rezsi tételek:\n{listText}";

        var sent = await emailSender.SendAsync(settings.UtilityContactEmail, EmailTemplate.UniqueSubject("Hiányzó rezsi tételek"), htmlBody, textBody, null, ct);
        if (!sent)
        {
            logger.LogWarning("Failed to send utility reminder digest: {Error}", emailSender.LastError);
            return;
        }

        db.UtilityReminderLogs.AddRange(missing.Select(p => new UtilityReminderLog
        {
            Id = Guid.NewGuid(),
            PropertyId = p.Id,
            Year = now.Year,
            Month = now.Month,
            SentAt = DateTime.UtcNow,
        }));
        await db.SaveChangesAsync(ct);
    }
}
