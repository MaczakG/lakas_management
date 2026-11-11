using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.ExchangeRates;

// Lekéri az MNB aktuális árfolyamait (az MNB árfolyam-webszolgáltatásán, API-n keresztül), és a követett devizákat
// devizánként egyetlen sorban tárolja (ld. ExchangeRateConfiguration egyedi indexe): a meglévő sort felülírja, ha
// még nincs, létrehozza. Követett deviza: az alapértelmezettek (Currencies.Default) és mindaz, amit az Árfolyamok
// oldalon felvettek (amelyiknek már van sora). Ezt hívja az óránkénti ExchangeRateFetchService és a „Frissítés most”
// gomb is.
public class ExchangeRateUpdater(LakaskezeloDbContext db, MnbExchangeRateClient client, ILogger<ExchangeRateUpdater> logger)
{
    public async Task UpdateAsync(CancellationToken ct)
    {
        var rates = await client.FetchCurrentRatesAsync(ct);
        var wanted = (await db.ExchangeRates.Select(r => r.CurrencyCode).ToListAsync(ct)).Union(Currencies.Default).ToHashSet();
        await StoreAsync(rates.Where(r => wanted.Contains(r.CurrencyCode)).ToList(), ct);
    }

    // Az MNB által most közölt, a program által kezelhető, de még nem követett devizák (a felvételhez).
    public async Task<List<MnbRate>> AvailableAsync(CancellationToken ct)
    {
        var rates = await client.FetchCurrentRatesAsync(ct);
        var tracked = (await db.ExchangeRates.Select(r => r.CurrencyCode).ToListAsync(ct)).ToHashSet();
        return rates.Where(r => Currencies.IsSupported(r.CurrencyCode) && !tracked.Contains(r.CurrencyCode))
            .OrderBy(r => r.CurrencyCode).ToList();
    }

    // Új deviza követése: az MNB aktuális árfolyamával. Hamis, ha az MNB most nem közöl rá árfolyamot.
    public async Task<bool> AddAsync(string currencyCode, CancellationToken ct)
    {
        var rate = (await client.FetchCurrentRatesAsync(ct)).FirstOrDefault(r => r.CurrencyCode == currencyCode);
        if (rate is null) return false;
        await StoreAsync([rate], ct);
        return true;
    }

    private async Task StoreAsync(List<MnbRate> tracked, CancellationToken ct)
    {
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
