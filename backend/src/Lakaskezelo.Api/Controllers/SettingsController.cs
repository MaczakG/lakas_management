using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// A cég Beállítások oldala (adminisztrátornak): rezsi-emlékeztető, számlakibocsátó adatai, számla e-mail
// szövege. Az e-mail küldés és a számla-tárhely platform-szintű (PlatformController /api/platform/delivery).
[Authorize(Roles = UsersController.AdminRole)]
[ApiController]
[Route("api/settings")]
public class SettingsController(LakaskezeloDbContext db, AppSettingsService settingsService, ICompanyContext companyContext,
    Invoicing.InvoiceProviderResolver providers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CompanySettingsDto>> Get(CancellationToken ct) =>
        Ok(ToDto(await settingsService.GetAsync(ct), await CurrentCompanyAsync(ct)));

    private Task<Company> CurrentCompanyAsync(CancellationToken ct)
    {
        var id = companyContext.CompanyId ?? throw new InvalidOperationException("Nincs cég a kérésben.");
        return db.Companies.SingleAsync(c => c.Id == id, ct);
    }

    [HttpPut]
    public async Task<ActionResult<CompanySettingsDto>> Update(CompanySettingsDto dto, CancellationToken ct)
    {
        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var issuerEmail = Clean(dto.IssuerEmail)?.ToLowerInvariant();
        if (issuerEmail is not null && !AuthController.LooksLikeEmail(issuerEmail))
            return BadRequest(new { message = "Érvénytelen válaszcím (e-mail)." });
        if (dto.InvoiceProvider is { Length: > 0 } prov && !Invoicing.InvoicingProviders.IsKnown(prov))
            return BadRequest(new { message = "Ismeretlen számlázó." });
        if (!Lakaskezelo.Domain.VatCodes.IsValid(dto.DefaultRentVat) || !Lakaskezelo.Domain.VatCodes.IsValid(dto.DefaultUtilityVat))
            return BadRequest(new { message = "Érvénytelen áfakulcs." });
        var utilityEmail = Clean(dto.UtilityContactEmail);
        if (utilityEmail is not null && !AuthController.LooksLikeEmail(utilityEmail.ToLowerInvariant()))
            return BadRequest(new { message = "Érvénytelen rezsi kontakt e-mail." });

        // A globális szűrő miatt ez a bejelentkezett felhasználó cégének sora; ha még nincs, létrehozzuk
        // (a CompanyId-t mentéskor a DbContext tölti ki a cég-kontextusból).
        var settings = await db.AppSettings.SingleOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new AppSettings { Id = Guid.NewGuid() };
            db.AppSettings.Add(settings);
        }

        settings.UtilityContactEmail = utilityEmail;
        settings.UtilityDeadlineDay = Math.Clamp(dto.UtilityDeadlineDay, 1, 28);
        settings.IssuerName = Clean(dto.IssuerName);
        settings.IssuerAddress = Clean(dto.IssuerAddress);
        settings.IssuerTaxId = Clean(dto.IssuerTaxId);
        settings.IssuerBankAccount = Clean(dto.IssuerBankAccount);
        settings.IssuerEmail = issuerEmail;
        settings.InvoiceEmailSubject = Clean(dto.InvoiceEmailSubject);
        settings.InvoiceEmailBody = Clean(dto.InvoiceEmailBody);
        settings.InvoiceProvider = dto.IsBusiness ? Clean(dto.InvoiceProvider) : null;
        if (Clean(dto.BillingoApiKey) is { } billingoKey) settings.BillingoApiKey = billingoKey;
        settings.BillingoBlockId = dto.BillingoBlockId;
        if (Clean(dto.SzamlazzAgentKey) is { } agentKey) settings.SzamlazzAgentKey = agentKey;
        settings.SzamlazzInvoicePrefix = Clean(dto.SzamlazzInvoicePrefix);
        settings.DefaultRentVat = dto.DefaultRentVat;
        settings.DefaultUtilityVat = dto.DefaultUtilityVat;
        settings.UpdatedAt = DateTime.UtcNow;

        var company = await CurrentCompanyAsync(ct);
        company.IsBusiness = dto.IsBusiness;
        if (settings.InvoiceProvider is not null && !Invoicing.InvoicingProviders.IsConfigured(settings))
        {
            return BadRequest(new { message = settings.InvoiceProvider == Invoicing.InvoicingProviders.Billingo
                ? "A Billingo összekötéshez add meg az API-kulcsot és válaszd ki a számlatömböt."
                : "A Számlázz.hu összekötéshez add meg a Számla Agent kulcsot." });
        }

        await db.SaveChangesAsync(ct);
        AppSettingsService.Invalidate(settings.CompanyId);
        return Ok(ToDto(settings, company));
    }

    // Kapcsolatpróba a beírt kulccsal (ha üres, az eltárolttal). Billingónál a számlatömbök listáját is visszaadja.
    [HttpPost("invoicing/test")]
    public async Task<ActionResult<Invoicing.ProviderTestResult>> TestInvoicing(TestInvoicingRequest request, CancellationToken ct)
    {
        var stored = await settingsService.GetAsync(ct);
        var probe = new AppSettings
        {
            BillingoApiKey = string.IsNullOrWhiteSpace(request.BillingoApiKey) ? stored.BillingoApiKey : request.BillingoApiKey.Trim(),
            SzamlazzAgentKey = string.IsNullOrWhiteSpace(request.SzamlazzAgentKey) ? stored.SzamlazzAgentKey : request.SzamlazzAgentKey.Trim(),
        };
        var provider = providers.ByName(request.Provider);
        if (provider is null) return BadRequest(new { message = "Ismeretlen számlázó." });
        if (request.Provider == Invoicing.InvoicingProviders.Billingo && string.IsNullOrWhiteSpace(probe.BillingoApiKey))
            return Ok(new Invoicing.ProviderTestResult(false, "Add meg a Billingo API-kulcsot."));
        if (request.Provider == Invoicing.InvoicingProviders.Szamlazzhu && string.IsNullOrWhiteSpace(probe.SzamlazzAgentKey))
            return Ok(new Invoicing.ProviderTestResult(false, "Add meg a Számla Agent kulcsot."));
        return Ok(await provider.TestAsync(probe, ct));
    }

    private static CompanySettingsDto ToDto(AppSettings s, Company company) => new(
        s.UtilityContactEmail, s.UtilityDeadlineDay,
        s.IssuerName, s.IssuerAddress, s.IssuerTaxId, s.IssuerBankAccount, s.IssuerEmail,
        s.InvoiceEmailSubject, s.InvoiceEmailBody,
        company.IsBusiness, s.InvoiceProvider, null, s.BillingoBlockId, null, s.SzamlazzInvoicePrefix,
        s.DefaultRentVat, s.DefaultUtilityVat,
        !string.IsNullOrWhiteSpace(s.BillingoApiKey), !string.IsNullOrWhiteSpace(s.SzamlazzAgentKey));
}
