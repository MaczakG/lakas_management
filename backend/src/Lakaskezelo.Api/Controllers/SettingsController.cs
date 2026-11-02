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
public class SettingsController(LakaskezeloDbContext db, AppSettingsService settingsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CompanySettingsDto>> Get(CancellationToken ct) => Ok(ToDto(await settingsService.GetAsync(ct)));

    [HttpPut]
    public async Task<ActionResult<CompanySettingsDto>> Update(CompanySettingsDto dto, CancellationToken ct)
    {
        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var issuerEmail = Clean(dto.IssuerEmail)?.ToLowerInvariant();
        if (issuerEmail is not null && !AuthController.LooksLikeEmail(issuerEmail))
            return BadRequest(new { message = "Érvénytelen válaszcím (e-mail)." });
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
        settings.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        AppSettingsService.Invalidate(settings.CompanyId);
        return Ok(ToDto(settings));
    }

    private static CompanySettingsDto ToDto(AppSettings s) => new(
        s.UtilityContactEmail, s.UtilityDeadlineDay,
        s.IssuerName, s.IssuerAddress, s.IssuerTaxId, s.IssuerBankAccount, s.IssuerEmail,
        s.InvoiceEmailSubject, s.InvoiceEmailBody);
}
