using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Subscriptions;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// A platform üzemeltetője: díjak és fizetési/számlázási kulcsok, valamint a cégek áttekintése.
[Authorize]
[PlatformOperator]
[ApiController]
[Route("api/platform")]
public class PlatformController(LakaskezeloDbContext db, PlatformSettingsService platform) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<PlatformSettingsDto>> GetSettings(CancellationToken ct)
    {
        var s = await db.PlatformSettings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new PlatformSettings { Id = PlatformSettings.SingletonId };
        return Ok(ToDto(s));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<PlatformSettingsDto>> UpdateSettings(UpdatePlatformSettingsRequest r, CancellationToken ct)
    {
        if (r.MonthlyNetPricePerProperty is < 0 or > 1_000_000) return BadRequest(new { message = "Érvénytelen havi díj." });
        if (r.YearlyDiscountPercent is < 0 or > 90) return BadRequest(new { message = "Az éves kedvezmény 0–90% lehet." });
        if (r.TrialDays is < 0 or > 365 || r.GraceDays is < 0 or > 60) return BadRequest(new { message = "Érvénytelen próbaidő vagy türelmi idő." });
        if (r.VatPercent is < 0 or > 100) return BadRequest(new { message = "Érvénytelen ÁFA-kulcs." });
        var exemption = string.IsNullOrWhiteSpace(r.VatExemption) ? null : r.VatExemption.Trim().ToUpperInvariant();
        if (exemption is not null && exemption is not ("AAM" or "TAM" or "KBAET" or "EUFAD37" or "HO" or "ATK"))
            return BadRequest(new { message = "Ismeretlen ÁFA-mentességi kód (pl. AAM — alanyi adómentes)." });
        if (r.BarionEnvironment is not ("Test" or "Prod")) return BadRequest(new { message = "A Barion környezet Test vagy Prod lehet." });

        var s = await db.PlatformSettings.SingleOrDefaultAsync(ct);
        if (s is null)
        {
            s = new PlatformSettings { Id = PlatformSettings.SingletonId };
            db.PlatformSettings.Add(s);
        }
        s.BillingEnabled = r.BillingEnabled;
        s.MonthlyNetPricePerProperty = r.MonthlyNetPricePerProperty;
        s.YearlyDiscountPercent = r.YearlyDiscountPercent;
        s.TrialDays = r.TrialDays;
        s.GraceDays = r.GraceDays;
        s.VatPercent = r.VatPercent;
        s.VatExemption = exemption;
        s.BarionEnvironment = r.BarionEnvironment;
        s.BarionPayeeEmail = string.IsNullOrWhiteSpace(r.BarionPayeeEmail) ? null : r.BarionPayeeEmail.Trim();
        s.BillingoBlockId = r.BillingoBlockId;
        if (!string.IsNullOrWhiteSpace(r.BarionPosKey)) s.BarionPosKey = r.BarionPosKey.Trim();
        if (!string.IsNullOrWhiteSpace(r.BillingoApiKey)) s.BillingoApiKey = r.BillingoApiKey.Trim();
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        PlatformSettingsService.Invalidate();
        return Ok(ToDto(s));
    }

    [HttpGet("companies")]
    public async Task<ActionResult<List<PlatformCompanyDto>>> Companies(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        var companies = await db.Companies.OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var users = await db.Users.IgnoreQueryFilters().GroupBy(u => u.CompanyId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var properties = await db.Properties.IgnoreQueryFilters().Where(p => p.IsActive).GroupBy(p => p.CompanyId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var subscriptions = await db.Subscriptions.IgnoreQueryFilters().ToDictionaryAsync(s => s.CompanyId, ct);
        var paid = await db.SubscriptionPayments.IgnoreQueryFilters().Where(p => p.Status == SubscriptionPaymentStatus.Succeeded)
            .GroupBy(p => p.CompanyId).Select(g => new { g.Key, Sum = g.Sum(p => p.GrossAmount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

        return Ok(companies.Select(c =>
        {
            subscriptions.TryGetValue(c.Id, out var s);
            var isOperator = platform.IsOperator(c.Id);
            // A tényleges (számított) állapot: a lejárt próbaidő akkor is zárolt, ha az ütemező még nem írta át.
            var status = s is null ? (SubscriptionStatus?)null
                : SubscriptionRules.IsLocked(s, settings, isOperator, DateTime.UtcNow) ? SubscriptionStatus.Locked : s.Status;
            return new PlatformCompanyDto(c.Id, c.Name, c.CreatedAt, users.GetValueOrDefault(c.Id), properties.GetValueOrDefault(c.Id),
                status, s?.TrialEndsAt, s?.PaidUntil, s?.Interval, paid.GetValueOrDefault(c.Id), isOperator);
        }).ToList());
    }

    private static PlatformSettingsDto ToDto(PlatformSettings s) => new(
        s.BillingEnabled, s.MonthlyNetPricePerProperty, s.YearlyDiscountPercent, s.TrialDays, s.GraceDays, s.VatPercent, s.VatExemption,
        s.BarionEnvironment, !string.IsNullOrEmpty(s.BarionPosKey), s.BarionPayeeEmail, !string.IsNullOrEmpty(s.BillingoApiKey), s.BillingoBlockId);
}
