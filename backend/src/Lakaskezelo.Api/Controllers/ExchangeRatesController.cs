using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.ExchangeRates;
using Lakaskezelo.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/exchange-rates")]
public class ExchangeRatesController(LakaskezeloDbContext db, ExchangeRateUpdater updater) : ControllerBase
{
    // Devizánként egy sor — az aktuális árfolyam.
    [HttpGet]
    public async Task<ActionResult<List<ExchangeRateDto>>> List(CancellationToken ct)
    {
        var rates = await db.ExchangeRates.OrderBy(r => r.CurrencyCode).ToListAsync(ct);
        return Ok(rates.Select(r => new ExchangeRateDto(r.Id, r.CurrencyCode, r.RateToHuf, r.RateDate, r.FetchedAt)));
    }

    // Manuális "Frissítés most" — ugyanaz a lekérés, mint amit az ExchangeRateFetchService
    // óránként automatikusan futtat.
    [HttpPost("fetch-now")]
    public async Task<ActionResult<List<ExchangeRateDto>>> FetchNow(CancellationToken ct)
    {
        try
        {
            await updater.UpdateAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException)
        {
            return StatusCode(502, new { message = $"Nem sikerült elérni az MNB árfolyam-szolgáltatást: {ex.Message}" });
        }

        return await List(ct);
    }
}
