using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.ExchangeRates;

// Naponta lekéri az MNB aktuális EUR/USD árfolyamát, és eltárolja (devizánként/naponta egy sort —
// ld. ExchangeRateConfiguration egyedi indexe). Óránként próbálkozik, de az adatbázis-egyediség
// miatt ártalmatlan, ha többször is lefut ugyanarra a napra (hétvégén/ünnepnapon az MNB nem ad ki
// új árfolyamot, ilyenkor a legutóbbi banki napi dátum ismétlődik, és a beszúrás egyszerűen
// kimarad, mert az a sor már létezik).
public class ExchangeRateFetchService(IServiceScopeFactory scopeFactory, ILogger<ExchangeRateFetchService> logger) : BackgroundService
{
    private static readonly string[] TrackedCurrencies = ["EUR", "USD"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Induláskor is megpróbálja — ne kelljen akár egy órát várni az első árfolyamra egy friss
        // telepítésen.
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<MnbExchangeRateClient>();
            var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();

            var rates = await client.FetchCurrentRatesAsync(ct);
            var tracked = rates.Where(r => TrackedCurrencies.Contains(r.CurrencyCode)).ToList();
            if (tracked.Count == 0) return;

            foreach (var rate in tracked)
            {
                var exists = await db.ExchangeRates.AnyAsync(
                    r => r.CurrencyCode == rate.CurrencyCode && r.RateDate == rate.RateDate, ct);
                if (exists) continue;

                db.ExchangeRates.Add(new ExchangeRate
                {
                    Id = Guid.NewGuid(),
                    CurrencyCode = rate.CurrencyCode,
                    RateToHuf = rate.RateToHuf,
                    RateDate = rate.RateDate,
                    FetchedAt = DateTime.UtcNow,
                });
                logger.LogInformation("Stored MNB rate {Currency} = {Rate} HUF ({Date})", rate.CurrencyCode, rate.RateToHuf, rate.RateDate);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MNB exchange rate fetch failed");
        }
    }
}
