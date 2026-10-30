using Lakaskezelo.Data;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Percenként ellenőrzi az összes aktív ingatlant: ha a helyi idő (Europe/Budapest) órája/perce
// egyezik az ingatlanhoz beállított számlázási időponttal, ma van az adott időszak tényleges
// kiküldési napja (a beállított nap, vagy ha az hétvége/munkaszüneti nap, az utána következő első
// munkanap — ld. BillingCalendar), és arra az (év, hónap) időszakra még nincs számla, legenerálja
// és kiküldi. Ingatlanonkénti try/catch — egy hiba nem
// akasztja meg a többi ingatlan feldolgozását; a hibás generálás Failed státuszú Invoice-ként
// kerül mentésre (InvoiceGenerationService), ami a Számlázás oldalon látszik, és onnan manuálisan
// újrapróbálható.
public class BillingSchedulerService(IServiceScopeFactory scopeFactory, ILogger<BillingSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BillingSchedulerService tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = BudapestClock.Now;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();

        var today = DateOnly.FromDateTime(now);
        if (!BillingCalendar.IsWorkday(today)) return;

        var candidates = await db.Properties
            .Where(p => p.IsActive && p.BillingHour == now.Hour && p.BillingMinute == now.Minute)
            .ToListAsync(ct);

        // Az előző hónap is számít: ha annak kiküldési napja hétvége/ünnep miatt átcsúszott ebbe a
        // hónapba (pl. február 28. szombat → március 2.), a februári számla most megy ki.
        var thisMonth = new DateOnly(now.Year, now.Month, 1);
        var due = candidates
            .SelectMany(p => new[] { thisMonth, thisMonth.AddMonths(-1) }, (p, period) => (Property: p, Period: period))
            .Where(x => BillingCalendar.EffectiveBillingDate(x.Period.Year, x.Period.Month, x.Property.BillingDayOfMonth) == today)
            .ToList();

        if (due.Count == 0) return;

        var generator = scope.ServiceProvider.GetRequiredService<InvoiceGenerationService>();

        foreach (var (property, period) in due)
        {
            var alreadyInvoiced = await db.Invoices.AnyAsync(
                i => i.PropertyId == property.Id && i.PeriodYear == period.Year && i.PeriodMonth == period.Month, ct);
            if (alreadyInvoiced) continue;

            try
            {
                await generator.GenerateAndSendAsync(property, period.Year, period.Month, ct);
                logger.LogInformation("Invoice generated for property {PropertyId} ({Year}-{Month})", property.Id, period.Year, period.Month);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Invoice generation failed for property {PropertyId}", property.Id);
            }
        }
    }
}
