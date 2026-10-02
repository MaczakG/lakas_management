namespace Lakaskezelo.Api.ExchangeRates;

// Óránként frissíti az MNB EUR/USD árfolyamát (ld. ExchangeRateUpdater) — devizánként egy sort ír
// felül. Hétvégén/ünnepnapon az MNB nem ad ki új árfolyamot, ilyenkor a legutóbbi banki napi
// érték marad, csak a lekérdezés ideje frissül.
public class ExchangeRateFetchService(IServiceScopeFactory scopeFactory, ILogger<ExchangeRateFetchService> logger) : BackgroundService
{
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
            await scope.ServiceProvider.GetRequiredService<ExchangeRateUpdater>().UpdateAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MNB exchange rate fetch failed");
        }
    }
}
