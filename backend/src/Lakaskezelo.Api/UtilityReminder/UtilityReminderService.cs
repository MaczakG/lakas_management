using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.UtilityReminder;

// Naponta egyszer (helyi idő 9:00-kor) ellenőrzi ingatlanonként a KÖVETKEZŐ SZÁMLA időszakát (ugyanazt, amit a
// „Rezsi: Rendben / Kitöltendő / Hiányzik” jelzés). Ha elérkezett a rögzítési határidő (a számlázás előtti utolsó, a
// Beállításokban megadott sorszámú nap, ld. BillingCalendar.ReminderDate):
//  1. a hónap rezsije elkészül az ingatlan rezsi tételeiből (UtilityPeriods.PrepareAsync);
//  2. ha a hónap rezsije hiányos (nincs tétel, vagy van kitöltendő), vagy korábbi hónapból maradt kitöltetlen tétel,
//     és még nem ment ki érte emlékeztető (UtilityReminderLog, ingatlanonként és időszakonként egyszer), egy
//     összesített levél megy a rezsi kontaktnak, amely ingatlanonként felsorolja a kitöltendő tételeket.
// A bizonylat ettől függetlenül kimegy; az összeg nélküli tétel nem kerül rá.
public class UtilityReminderService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<UtilityReminderService> logger) : BackgroundService
{
    // Az ellenőrzés órája (helyi idő); a UtilityReminder:CheckHour beállítással felülírható (pl. helyi próbához).
    private int CheckHour => configuration.GetValue("UtilityReminder:CheckHour", 9);
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

    // Cégenként, a cég saját beállításaival (rezsi-kontakt, határnap) — külön scope-ban, hogy a
    // cégek beállításai/adatai ne keveredjenek.
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
                logger.LogError(ex, "Utility reminder failed for company {CompanyId}", companyId);
            }
        }
    }

    private async Task RunForCompanyAsync(Guid companyId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var settings = await settingsService.GetAsync(ct);
        // Nem fizető (zárolt) cégnek nem készítünk elő rezsit, és emlékeztetőt sem küldünk.
        if (await scope.ServiceProvider.GetRequiredService<Subscriptions.SubscriptionAccess>().IsLockedAsync(companyId, ct)) return;

        var now = BudapestClock.Now;
        var today = DateOnly.FromDateTime(now);

        var properties = await db.Properties.Where(p => p.IsActive).ToListAsync(ct);
        // Az aktuális és a következő év rezsije előre elkészül a rezsi tételekből (évváltáskor az új következő év is).
        foreach (var p in properties)
        {
            await UtilityPeriods.PrepareAheadAndSaveAsync(db, p, now, ct);
        }

        // Ingatlanonként a következő számla időszaka és kiküldése; csak azok, amelyeknél elérkezett a határidő.
        var due = properties
            .Select(p =>
            {
                var (period, at) = BillingCalendar.NextBilling(now, p.BillingDayOfMonth, p.BillingHour, p.BillingMinute);
                return (Property: p, Period: period, At: at);
            })
            .Where(x => today >= BillingCalendar.ReminderDate(DateOnly.FromDateTime(x.At), settings.UtilityDeadlineDay))
            .ToList();
        if (due.Count == 0) return;

        // A határnapon a hónap rezsije elkészül a rezsi tételekből (hónaponként egyszer): a fixek összeggel, a nem
        // fixek kitöltendőként. Így a bizonylat előtt még van idő kitölteni őket.
        foreach (var x in due)
        {
            await UtilityPeriods.PrepareAsync(db, x.Property.Id, x.Period.Year, x.Period.Month, force: false, ct);
        }
        await db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.UtilityContactEmail)) return;

        var ids = due.Select(x => x.Property.Id).ToList();
        var reminded = (await db.UtilityReminderLogs.Where(l => ids.Contains(l.PropertyId))
            .Select(l => new { l.PropertyId, l.Year, l.Month }).ToListAsync(ct))
            .Select(l => (l.PropertyId, l.Year, l.Month)).ToHashSet();
        var states = await UtilityPeriods.StatesAsync(db, [.. due.Select(x => (x.Property.Id, x.Period.Year, x.Period.Month))], ct);

        // A kitöltendő (összeg nélküli, még bizonylatra nem került) tételek: a következő bizonylat hónapjáé és a
        // korábbi hónapokból megmaradtak (a 36 hónapos ablakon belül, amíg bizonylatra kerülhetnek).
        var (fromYear, fromMonth) = InvoiceGenerationService.BillingWindowStart(now.Year, now.Month);
        var fromKey = fromYear * 12 + fromMonth;
        var pendingEntries = await db.UtilityCostEntries
            .Where(u => ids.Contains(u.PropertyId) && u.Amount == 0 && u.InvoiceId == null && !u.ExcludedFromBilling
                && u.Year * 12 + u.Month >= fromKey)
            .OrderBy(u => u.Year).ThenBy(u => u.Month).ThenBy(u => u.CreatedAt)
            .Select(u => new { u.PropertyId, u.Year, u.Month, u.Label })
            .ToListAsync(ct);

        // Szólunk, ha a hónap rezsije hiányos (nincs tétel, vagy van kitöltendő), vagy korábbról maradt kitöltetlen
        // tétel; ingatlanonként és időszakonként egyszer.
        var missing = due
            .Where(x => !reminded.Contains((x.Property.Id, x.Period.Year, x.Period.Month)))
            .Select(x =>
            {
                var periodKey = x.Period.Year * 12 + x.Period.Month;
                var mine = pendingEntries.Where(p => p.PropertyId == x.Property.Id).ToList();
                return (x.Property, x.Period, x.At, Item: new UtilityReminderContent.Item(
                    x.Property.Name, x.Period, x.At, states[(x.Property.Id, x.Period.Year, x.Period.Month)],
                    [.. mine.Where(p => p.Year * 12 + p.Month == periodKey).Select(p => p.Label)],
                    [.. mine.Where(p => p.Year * 12 + p.Month < periodKey).Select(p => (p.Label, p.Year, p.Month))]));
            })
            .Where(x => UtilityReminderContent.NeedsReminder(x.Item.State, x.Item.PendingEarlier.Count))
            .OrderBy(x => x.At).ThenBy(x => x.Property.Name)
            .ToList();
        if (missing.Count == 0) return;

        var mail = UtilityReminderContent.Build([.. missing.Select(x => x.Item)], configuration["Frontend:BaseUrl"]);
        var sent = await emailSender.SendAsync(settings.UtilityContactEmail, EmailTemplate.UniqueSubject(mail.Subject), mail.Html, mail.Text, null, ct);
        if (!sent)
        {
            logger.LogWarning("Failed to send utility reminder digest: {Error}", emailSender.LastError);
            return;
        }

        db.UtilityReminderLogs.AddRange(missing.Select(x => new UtilityReminderLog
        {
            Id = Guid.NewGuid(),
            PropertyId = x.Property.Id,
            Year = x.Period.Year,
            Month = x.Period.Month,
            SentAt = DateTime.UtcNow,
        }));
        await db.SaveChangesAsync(ct);
    }
}
