using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.ExchangeRates;

// Lekéri az MNB aktuális EUR/USD árfolyamát, és devizánként egyetlen sorban tárolja (ld.
// ExchangeRateConfiguration egyedi indexe): a meglévő sort felülírja, ha még nincs, létrehozza.
// Ezt hívja az óránkénti ExchangeRateFetchService és a "Frissítés most" gomb is.
public class ExchangeRateUpdater(LakaskezeloDbContext db, MnbExchangeRateClient client, ILogger<ExchangeRateUpdater> logger)
{
    public static readonly string[] TrackedCurrencies = ["EUR", "USD"];

    public async Task UpdateAsync(CancellationToken ct)
    {
        var rates = await client.FetchCurrentRatesAsync(ct);
        var tracked = rates.Where(r => TrackedCurrencies.Contains(r.CurrencyCode)).ToList();
        if (tracked.Count == 0) return;

        var codes = tracked.Select(r => r.CurrencyCode).ToList();
        var existing = await db.ExchangeRates.Where(r => codes.Contains(r.CurrencyCode)).ToDictionaryAsync(r => r.CurrencyCode, ct);
        var now = DateTime.UtcNow;

        foreach (var rate in tracked)
        {
            if (!existing.TryGetValue(rate.CurrencyCode, out var row))
            {
                row = new ExchangeRate { Id = Guid.NewGuid(), CurrencyCode = rate.CurrencyCode };
                db.ExchangeRates.Add(row);
            }

            if (row.RateToHuf != rate.RateToHuf || row.RateDate != rate.RateDate)
            {
                logger.LogInformation("Updated MNB rate {Currency} = {Rate} HUF ({Date})", rate.CurrencyCode, rate.RateToHuf, rate.RateDate);
            }
            row.RateToHuf = rate.RateToHuf;
            row.RateDate = rate.RateDate;
            row.FetchedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
