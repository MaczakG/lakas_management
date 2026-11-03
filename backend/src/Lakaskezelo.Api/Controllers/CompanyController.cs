using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// A bejelentkezett felhasználó cégének alapadatai (minden szerepkörnek): cég-e, melyik számlázó van összekötve, és
// az alapértelmezett áfa — pl. az ingatlan adatlapjának áfa-mezőihez és a „számla”/„bizonylat” feliratokhoz.
[Authorize]
[ApiController]
[Route("api/company")]
public class CompanyController(LakaskezeloDbContext db, AppSettingsService settingsService, ICompanyContext companyContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CompanyInfoDto>> Get(CancellationToken ct)
    {
        var id = companyContext.CompanyId ?? throw new InvalidOperationException("Nincs cég a kérésben.");
        var company = await db.Companies.SingleAsync(c => c.Id == id, ct);
        var s = await settingsService.GetAsync(ct);
        var provider = company.IsBusiness && Invoicing.InvoicingProviders.IsConfigured(s) ? s.InvoiceProvider : null;
        return Ok(new CompanyInfoDto(company.Name, company.IsBusiness, provider, s.DefaultRentVat, s.DefaultUtilityVat));
    }
}
