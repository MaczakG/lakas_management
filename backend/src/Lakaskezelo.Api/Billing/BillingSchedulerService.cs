using Lakaskezelo.Data;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Percenként ellenőrzi az összes aktív ingatlant: ha az ingatlanhoz beállított számlázási időpont az elmúlt
// CatchUp (30 perc) ablakba esik (helyi idő, Europe/Budapest — így egy újraindítás/telepítés alatt kimaradt perc
// sem marad ki; egy folyamat egy ingatlant egy időszakra naponta csak egyszer próbál), ma van az adott időszak tényleges
// kiküldési napja (a beállított nap, vagy ha az hétvége/munkaszüneti nap, az utána következő első
// munkanap — ld. BillingCalendar), és arra az (év, hónap) időszakra még nincs számla, legenerálja
// és kiküldi. Ingatlanonkénti try/catch — egy hiba nem
// akasztja meg a többi ingatlan feldolgozását; a hibás generálás Failed státuszú Invoice-ként
// kerül mentésre (InvoiceGenerationService), ami a Számlázás oldalon látszik, és onnan manuálisan
// újrapróbálható.
public class BillingSchedulerService(IServiceScopeFactory scopeFactory, ILogger<BillingSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int CatchUpMinutes = 30;

    // Ebben a folyamatban ma már megpróbált (ingatlan, időszak) párok: a 30 perces ablak miatt ugyanaz az ingatlan
    // több percben is jelölt, de egy hibás (pl. a kiküldés után mentés nélkül elakadt) generálást nem ismétlünk.
    private readonly HashSet<(Guid PropertyId, DateOnly Period)> attempted = [];
    private DateOnly attemptedDay;

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
        if (attemptedDay != today) { attempted.Clear(); attemptedDay = today; }
        var nowMinutes = now.Hour * 60 + now.Minute;

        // Minden előfizető cég ingatlana (IgnoreQueryFilters); a számlázás előtt a kérés az ingatlan
        // cégére áll, hogy a bérlő, rezsi, beállítások stb. annak a cégnek az adataiból jöjjenek.
        var candidates = await db.Properties.IgnoreQueryFilters()
            .Where(p => p.IsActive && p.BillingHour * 60 + p.BillingMinute <= nowMinutes
                && p.BillingHour * 60 + p.BillingMinute > nowMinutes - CatchUpMinutes)
            .ToListAsync(ct);
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContext>();

        // Az előző hónap is számít: ha annak kiküldési napja hétvége/ünnep miatt átcsúszott ebbe a
        // hónapba (pl. február 28. szombat → március 2.), a februári számla most megy ki.
        var thisMonth = new DateOnly(now.Year, now.Month, 1);
        var due = candidates
            .SelectMany(p => new[] { thisMonth, thisMonth.AddMonths(-1) }, (p, period) => (Property: p, Period: period))
            .Where(x => BillingCalendar.EffectiveBillingDate(x.Period.Year, x.Period.Month, x.Property.BillingDayOfMonth) == today
                && !attempted.Contains((x.Property.Id, x.Period)))
            .ToList();

        if (due.Count == 0) return;

        var generator = scope.ServiceProvider.GetRequiredService<InvoiceGenerationService>();

        var access = scope.ServiceProvider.GetRequiredService<Subscriptions.SubscriptionAccess>();

        foreach (var (property, period) in due)
        {
            attempted.Add((property.Id, period));
            companyContext.Set(property.CompanyId);
            // Nem fizető (zárolt) cégnek nem küldünk ki bérlői számlát.
            if (await access.IsLockedAsync(property.CompanyId, ct))
            {
                logger.LogInformation("Skipping invoice for property {PropertyId}: company subscription is locked", property.Id);
                continue;
            }
            // Archivált számla esetén sem generál újra magától: az újat a felhasználó készíti el, amikor a rezsi rendben van.
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
