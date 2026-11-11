using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.ExchangeRates;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Enums;
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
        return Ok(rates.Select(r => new ExchangeRateDto(r.Id, r.CurrencyCode, r.RateToHuf, r.RateDate, r.FetchedAt, Currencies.Name(r.CurrencyCode), Currencies.IsDefault(r.CurrencyCode))));
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

    // A felvehető devizák: amire az MNB most árfolyamot közöl, és még nincs a listán.
    [HttpGet("available")]
    public async Task<ActionResult<List<AvailableCurrencyDto>>> Available(CancellationToken ct)
    {
        try
        {
            var rates = await updater.AvailableAsync(ct);
            return Ok(rates.Select(r => new AvailableCurrencyDto(r.CurrencyCode, Currencies.Name(r.CurrencyCode), r.RateToHuf, r.RateDate)));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException)
        {
            return StatusCode(502, new { message = $"Nem sikerült elérni az MNB árfolyam-szolgáltatást: {ex.Message}" });
        }
    }

    // Új deviza felvétele a követettek közé (az árfolyamok minden cégnek közösek). Utána a bérlőknél is választható.
    [Authorize(Roles = UsersController.AdminRole)]
    [HttpPost]
    public async Task<ActionResult<List<ExchangeRateDto>>> Add(AddCurrencyRequest request, CancellationToken ct)
    {
        var code = (request.CurrencyCode ?? "").Trim().ToUpperInvariant();
        if (!Currencies.IsSupported(code)) return BadRequest(new { message = "Ismeretlen vagy nem választható deviza." });
        if (await db.ExchangeRates.AnyAsync(r => r.CurrencyCode == code, ct)) return await List(ct);
        try
        {
            if (!await updater.AddAsync(code, ct)) return BadRequest(new { message = "Az MNB erre a devizára most nem közöl árfolyamot." });
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException)
        {
            return StatusCode(502, new { message = $"Nem sikerült elérni az MNB árfolyam-szolgáltatást: {ex.Message}" });
        }
        return await List(ct);
    }

    // Követett deviza törlése. Az alap devizák nem törölhetők, és az sem, amelyik egy bérlő bérleti díjának
    // devizája (az árfolyam-tábla közös: a másik cég bérlője is számít, de róla részletet nem árulunk el).
    [Authorize(Roles = UsersController.AdminRole)]
    [HttpDelete("{currencyCode}")]
    public async Task<IActionResult> Delete(string currencyCode, CancellationToken ct)
    {
        var code = (currencyCode ?? "").Trim().ToUpperInvariant();
        var row = await db.ExchangeRates.FirstOrDefaultAsync(r => r.CurrencyCode == code, ct);
        if (row is null) return NotFound();
        if (Currencies.IsDefault(code))
        {
            return Conflict(new { message = $"A(z) {code} alap deviza, nem törölhető." });
        }

        if (Enum.TryParse<RentCurrency>(code, out var currency))
        {
            var own = await db.Tenants.CountAsync(t => t.RentCurrency == currency, ct);
            if (own > 0)
            {
                return Conflict(new { message = $"A(z) {code} nem törölhető: {own} bérlőnél ez a bérleti díj devizája. Előbb állítsd át őket másik devizára." });
            }
            if (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.RentCurrency == currency, ct))
            {
                return Conflict(new { message = $"A(z) {code} nem törölhető, mert használatban van." });
            }
        }

        db.ExchangeRates.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
