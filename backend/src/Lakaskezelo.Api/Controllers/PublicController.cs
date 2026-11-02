using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Subscriptions;
using Microsoft.AspNetCore.Mvc;

namespace Lakaskezelo.Api.Controllers;

// Bejelentkezés nélkül elérhető adatok: az ÁSZF és az adatkezelési tájékoztató (aszf.html, adatkezeles.html)
// ezekkel töltődik ki — a szolgáltató adatai és a díjak mindig a Platform oldalon megadott aktuális értékek.
[ApiController]
[Route("api/public")]
public class PublicController(PlatformSettingsService platform, IConfiguration configuration) : ControllerBase
{
    [HttpGet("legal")]
    public async Task<ActionResult<LegalInfoDto>> Legal(CancellationToken ct)
    {
        var s = await platform.GetAsync(ct);
        return Ok(new LegalInfoDto(
            configuration["App:ProductName"] is { Length: > 0 } name ? name : "Lakáskezelő",
            configuration["Frontend:BaseUrl"] ?? "",
            new OperatorInfoDto(s.OperatorName, s.OperatorAddress, s.OperatorTaxNumber, s.OperatorRegistrationNumber, s.OperatorEmail, s.OperatorPhone),
            s.TermsVersion, s.MonthlyPricePerProperty, s.PricesIncludeVat, s.VatPercent, s.VatExemption, s.YearlyDiscountPercent,
            s.TrialDays, s.GraceDays));
    }
}
