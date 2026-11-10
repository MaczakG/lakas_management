using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.UtilityReminder;

// Naponta egyszer (helyi idő 9:00-kor) ellenőrzi ingatlanonként a KÖVETKEZŐ SZÁMLA időszakát (ugyanazt, amit a
// „Rezsi: Rendben / Hiányzik” jelzés): ha elérkezett a rögzítési határidő (a számlázás előtti utolsó, a Beállításokban
// megadott sorszámú nap, ld. BillingCalendar.ReminderDate), a hónap rezsije elkészül az ingatlan rezsi tételeiből
// (UtilityPeriods.PrepareAsync); ha ezután az időszak rezsije hiányos (nincs tétel, vagy van kitöltendő), és még nem ment
// ki érte emlékeztető (UtilityReminderLog, időszakonként) — egy összesített digest e-mailt küld a rezsi kontaktnak az
// összes érintett ingatlannal, majd naplózza, hogy ne menjen ki minden nap újra ugyanazért az időszakért.
public class UtilityReminderService(IServiceScopeFactory scopeFactory, ILogger<UtilityReminderService> logger) : BackgroundService
{
    private const int CheckHour = 9;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(30);

    private static readonly string[] HungarianMonths =
        ["január", "február", "március", "április", "május", "június", "július", "augusztus", "szeptember", "október", "november", "december"];

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

        // Ingatlanonként a következő számla időszaka és kiküldése; csak azok, amelyeknél elérkezett a határidő.
        var due = (await db.Properties.Where(p => p.IsActive).ToListAsync(ct))
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
        // Hiányos az időszak, ha nincs rezsije (se rezsi tétel, se rögzített tétel), vagy van még kitöltendő tétele.
        var states = await UtilityPeriods.StatesAsync(db, [.. due.Select(x => (x.Property.Id, x.Period.Year, x.Period.Month))], ct);

        var missing = due
            .Where(x => states[(x.Property.Id, x.Period.Year, x.Period.Month)] != UtilityPeriods.Ok
                && !reminded.Contains((x.Property.Id, x.Period.Year, x.Period.Month)))
            .OrderBy(x => x.At).ThenBy(x => x.Property.Name)
            .ToList();
        if (missing.Count == 0) return;

        string Period(DateOnly d) => $"{d.Year}. {HungarianMonths[d.Month - 1]}";
        string Line(DateOnly period, DateTime at, string name) => $"{name}: {Period(period)} (kiállítás: {at:yyyy.MM.dd. HH:mm})";
        var listHtml = string.Join("", missing.Select(x => $"<li>{System.Net.WebUtility.HtmlEncode(Line(x.Period, x.At, x.Property.Name))}</li>"));
        var listText = string.Join("\n", missing.Select(x => $"- {Line(x.Period, x.At, x.Property.Name)}"));

        var htmlBody = EmailTemplate.Render(
            preheader: $"{missing.Count} ingatlannál hiányos a rezsi a következő bizonylathoz",
            heading: "Hiányzó rezsi tételek",
            bodyHtml: $"""
                <p style="margin:0 0 12px;">A következő ingatlanoknál a soron következő bizonylat rezsije hiányos: nincs rögzített tétel, vagy van még kitöltendő (összeg nélküli) tétel.</p>
                <ul style="margin:0 0 12px;padding-left:20px;">{listHtml}</ul>
                <p style="margin:0 0 12px;">A rezsit a kiállítás előtt rögzítsd, hogy rákerüljön a bizonylatra. Ami addig nincs kitöltve, kimarad, és a következő bizonylatra kerül.</p>
                """);
        var textBody = $"A soron következő bizonylat rezsije hiányos (nincs tétel, vagy van még kitöltendő):\n{listText}";

        var sent = await emailSender.SendAsync(settings.UtilityContactEmail, EmailTemplate.UniqueSubject("Hiányzó rezsi tételek"), htmlBody, textBody, null, ct);
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
