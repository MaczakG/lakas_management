using Lakaskezelo.Data;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Percenként ellenőrzi az összes aktív ingatlant: ha a helyi idő (Europe/Budapest) napja/órája/
// perce egyezik az ingatlanhoz beállított számlázási időponttal, és az adott (év, hónap)
// időszakra még nincs számla, legenerálja és kiküldi. Ingatlanonkénti try/catch — egy hiba nem
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

        var dueProperties = await db.Properties
            .Where(p => p.IsActive && p.BillingDayOfMonth == now.Day && p.BillingHour == now.Hour && p.BillingMinute == now.Minute)
            .ToListAsync(ct);

        if (dueProperties.Count == 0) return;

        var generator = scope.ServiceProvider.GetRequiredService<InvoiceGenerationService>();

        foreach (var property in dueProperties)
        {
            var alreadyInvoiced = await db.Invoices.AnyAsync(
                i => i.PropertyId == property.Id && i.PeriodYear == now.Year && i.PeriodMonth == now.Month, ct);
            if (alreadyInvoiced) continue;

            try
            {
                await generator.GenerateAndSendAsync(property, now.Year, now.Month, ct);
                logger.LogInformation("Invoice generated for property {PropertyId} ({Year}-{Month})", property.Id, now.Year, now.Month);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Invoice generation failed for property {PropertyId}", property.Id);
            }
        }
    }
}
