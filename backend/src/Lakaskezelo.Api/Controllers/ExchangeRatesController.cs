using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.ExchangeRates;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/exchange-rates")]
public class ExchangeRatesController(LakaskezeloDbContext db, MnbExchangeRateClient client) : ControllerBase
{
    private static readonly string[] TrackedCurrencies = ["EUR", "USD"];

    [HttpGet]
    public async Task<ActionResult<List<ExchangeRateDto>>> List(CancellationToken ct)
    {
        var rates = await db.ExchangeRates
            .OrderByDescending(r => r.RateDate).ThenBy(r => r.CurrencyCode)
            .Take(200)
            .ToListAsync(ct);
        return Ok(rates.Select(r => new ExchangeRateDto(r.Id, r.CurrencyCode, r.RateToHuf, r.RateDate, r.FetchedAt)));
    }

    // Manuális "Frissítés most" — ugyanaz a lekérés, mint amit az ExchangeRateFetchService
    // óránként automatikusan futtat.
    [HttpPost("fetch-now")]
    public async Task<ActionResult<List<ExchangeRateDto>>> FetchNow(CancellationToken ct)
    {
        List<MnbRate> rates;
        try
        {
            rates = await client.FetchCurrentRatesAsync(ct);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = $"Nem sikerült elérni az MNB árfolyam-szolgáltatást: {ex.Message}" });
        }

        var tracked = rates.Where(r => TrackedCurrencies.Contains(r.CurrencyCode)).ToList();
        foreach (var rate in tracked)
        {
            var existing = await db.ExchangeRates.FirstOrDefaultAsync(
                r => r.CurrencyCode == rate.CurrencyCode && r.RateDate == rate.RateDate, ct);
            if (existing is not null) continue;

            db.ExchangeRates.Add(new ExchangeRate
            {
                Id = Guid.NewGuid(),
                CurrencyCode = rate.CurrencyCode,
                RateToHuf = rate.RateToHuf,
                RateDate = rate.RateDate,
                FetchedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);

        return await List(ct);
    }
}
