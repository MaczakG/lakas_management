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
public class PlatformController(LakaskezeloDbContext db, PlatformSettingsService platform, ICompanyContext companyContext) : ControllerBase
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
        if (r.MonthlyPricePerProperty is < 0 or > 1_000_000) return BadRequest(new { message = "Érvénytelen havi díj." });
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
        s.MonthlyPricePerProperty = r.MonthlyPricePerProperty;
        s.PricesIncludeVat = r.PricesIncludeVat;
        s.YearlyDiscountPercent = r.YearlyDiscountPercent;
        s.TrialDays = r.TrialDays;
        s.GraceDays = r.GraceDays;
        s.VatPercent = r.VatPercent;
        s.VatExemption = exemption;
        s.BarionEnvironment = r.BarionEnvironment;
        s.BarionPayeeEmail = string.IsNullOrWhiteSpace(r.BarionPayeeEmail) ? null : r.BarionPayeeEmail.Trim();
        s.BillingoBlockId = r.BillingoBlockId;
        if (r.Operator is { } o)
        {
            static string? Clean(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()[..Math.Min(v.Trim().Length, max)];
            s.OperatorName = Clean(o.Name, 200);
            s.OperatorAddress = Clean(o.Address, 300);
            s.OperatorTaxNumber = Clean(o.TaxNumber, 20);
            s.OperatorRegistrationNumber = Clean(o.RegistrationNumber, 50);
            s.OperatorEmail = Clean(o.Email, 256);
            s.OperatorPhone = Clean(o.Phone, 50);
        }
        if (!string.IsNullOrWhiteSpace(r.TermsVersion))
        {
            if (!DateOnly.TryParseExact(r.TermsVersion.Trim(), "yyyy-MM-dd", out _))
                return BadRequest(new { message = "A jogi dokumentumok hatálybalépése ÉÉÉÉ-HH-NN formátumú dátum." });
            s.TermsVersion = r.TermsVersion.Trim();
        }
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
        s.BillingEnabled, s.MonthlyPricePerProperty, s.PricesIncludeVat, s.YearlyDiscountPercent, s.TrialDays, s.GraceDays, s.VatPercent, s.VatExemption,
        s.BarionEnvironment, !string.IsNullOrEmpty(s.BarionPosKey), s.BarionPayeeEmail, !string.IsNullOrEmpty(s.BillingoApiKey), s.BillingoBlockId,
        new OperatorInfoDto(s.OperatorName, s.OperatorAddress, s.OperatorTaxNumber, s.OperatorRegistrationNumber, s.OperatorEmail, s.OperatorPhone),
        s.TermsVersion);

    // Díjmentes napok egy cégnek (pl. reklamáció, bevezető kedvezmény): próbaidőben a próbaidőt, fizetőnél a
    // kifizetett időszakot hosszabbítja; zárolt cégnél új próbaidőt indít — mindhárom feloldja a zárolást.
    [HttpPost("companies/{id:guid}/extend")]
    public async Task<IActionResult> Extend(Guid id, ExtendRequest request, CancellationToken ct)
    {
        if (request.Days is < 1 or > 366) return BadRequest(new { message = "1 és 366 közötti napszámot adj meg." });
        if (platform.IsOperator(id)) return BadRequest(new { message = "Az üzemeltető cég nem fizet." });
        if (!await db.Companies.AnyAsync(c => c.Id == id, ct)) return NotFound();

        var now = DateTime.UtcNow;
        var s = await db.Subscriptions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == id, ct);
        if (s is null)
        {
            s = new Subscription { Id = Guid.NewGuid(), CompanyId = id, Status = SubscriptionStatus.Trial, TrialEndsAt = now, CreatedAt = now };
            db.Subscriptions.Add(s);
        }
        switch (s.Status)
        {
            case SubscriptionStatus.Trial:
                s.TrialEndsAt = (s.TrialEndsAt > now ? s.TrialEndsAt : now).AddDays(request.Days);
                break;
            // Fizető (vagy fizetett, de zárolt) cég mentett kártyával: a kifizetett időszak hosszabbodik.
            case SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Locked
                when s.PaidUntil is not null && (s.Status != SubscriptionStatus.Locked || s.BarionRecurrenceId is not null):
                s.PaidUntil = (s.PaidUntil > now ? s.PaidUntil.Value : now).AddDays(request.Days);
                s.Status = SubscriptionStatus.Active;
                s.GraceEndsAt = null;
                s.LockedAt = null;
                break;
            default:
                s.Status = SubscriptionStatus.Trial;
                s.TrialEndsAt = now.AddDays(request.Days);
                s.TrialReminderSentAt = null;
                s.LockedAt = null;
                break;
        }
        s.UpdatedAt = now;
        // Platform-művelet más cég adatán: a mentés a cél cég kontextusában fut (a DbContext ellenőrzi).
        companyContext.Set(id);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = $"+{request.Days} nap jóváírva." });
    }

    [HttpGet("payments")]
    public async Task<ActionResult<List<PlatformPaymentDto>>> Payments(CancellationToken ct)
    {
        var names = await db.Companies.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var payments = await db.SubscriptionPayments.IgnoreQueryFilters().OrderByDescending(p => p.CreatedAt).Take(200).ToListAsync(ct);
        return Ok(payments.Select(p => new PlatformPaymentDto(p.Id, names.GetValueOrDefault(p.CompanyId, "?"), p.Kind, p.Status, p.Interval,
            p.PropertyCount, p.GrossAmount, p.CreatedAt, p.ErrorMessage, p.BillingoPublicUrl, p.BillingoError)).ToList());
    }
}
