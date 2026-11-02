using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Subscriptions;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// A cég előfizetése. Az állapotot (a felület figyelmeztető sávjához) mindenki látja; a részleteket,
// a számlázási adatokat, a fizetést és a lemondást csak adminisztrátor.
[Authorize]
[ApiController]
[Route("api/subscription")]
public class SubscriptionController(
    LakaskezeloDbContext db, SubscriptionService service, PlatformSettingsService platform, ICompanyContext companyContext) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<SubscriptionStatusDto>> Status(CancellationToken ct) => Ok(await StatusAsync(ct));

    [Authorize(Roles = UsersController.AdminRole)]
    [HttpGet]
    public async Task<ActionResult<SubscriptionDetailsDto>> Get(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        var subscription = await service.GetOrCreateAsync(ct);
        var active = await service.ActivePropertyCountAsync(ct);
        var payments = await db.SubscriptionPayments.OrderByDescending(p => p.CreatedAt).Take(50).ToListAsync(ct);
        return Ok(new SubscriptionDetailsDto(
            ToStatus(subscription, settings),
            subscription.Interval,
            subscription.BarionRecurrenceId is not null,
            new BillingDetailsDto(subscription.BillingName, subscription.BillingTaxNumber, subscription.BillingPostCode,
                subscription.BillingCity, subscription.BillingStreet, subscription.BillingEmail),
            active,
            settings.MonthlyPricePerProperty,
            settings.PricesIncludeVat,
            settings.YearlyDiscountPercent,
            ToDto(Pricing.Quote(settings, active, BillingInterval.Monthly)),
            ToDto(Pricing.Quote(settings, active, BillingInterval.Yearly)),
            settings.BillingEnabled && BarionClient.IsConfigured(settings),
            payments.Select(p => new SubscriptionPaymentDto(p.Id, p.Kind, p.Status, p.Interval, p.PropertyCount, p.GrossAmount,
                p.PeriodStart, p.PeriodEnd, p.CreatedAt, p.ErrorMessage, p.BillingoPublicUrl,
                p.Status == SubscriptionPaymentStatus.Succeeded && p.BillingoDocumentId is null)).ToList()));
    }

    [Authorize(Roles = UsersController.AdminRole)]
    [HttpPut("billing-details")]
    public async Task<IActionResult> UpdateBillingDetails(BillingDetailsDto request, CancellationToken ct)
    {
        string? Clean(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()[..Math.Min(v.Trim().Length, max)];
        var name = Clean(request.Name, 200);
        var tax = Clean(request.TaxNumber, 20);
        var postCode = Clean(request.PostCode, 10);
        var city = Clean(request.City, 100);
        var street = Clean(request.Street, 200);
        var email = Clean(request.Email, 256)?.ToLowerInvariant();

        if (name is null || postCode is null || city is null || street is null)
            return BadRequest(new { message = "A számlázási név és a teljes cím (irányítószám, település, utca/házszám) kötelező." });
        if (!BillingoClient.IsPostCode(postCode)) return BadRequest(new { message = "Az irányítószám négy számjegy (pl. 2890)." });
        if (tax is not null && !BillingoClient.IsHungarianTaxNumber(tax))
            return BadRequest(new { message = "Az adószám formátuma: 12345678-1-12. Magánszemélyként hagyd üresen." });
        if (email is not null && !AuthController.LooksLikeEmail(email)) return BadRequest(new { message = "Érvénytelen e-mail cím." });

        var subscription = await service.GetOrCreateAsync(ct);
        subscription.BillingName = name;
        subscription.BillingTaxNumber = tax;
        subscription.BillingPostCode = postCode;
        subscription.BillingCity = city;
        subscription.BillingStreet = street;
        subscription.BillingEmail = email;
        subscription.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "A számlázási adatok elmentve." });
    }

    // Gyakoriság váltása (a következő terheléstől) és lemondás / visszavonás.
    [Authorize(Roles = UsersController.AdminRole)]
    [HttpPut("plan")]
    public async Task<IActionResult> UpdatePlan(UpdatePlanRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Interval)) return BadRequest(new { message = "Érvénytelen gyakoriság." });
        var subscription = await service.GetOrCreateAsync(ct);
        subscription.Interval = request.Interval;
        subscription.CancelAtPeriodEnd = request.CancelAtPeriodEnd;
        subscription.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = request.CancelAtPeriodEnd
            ? "Lemondtad az előfizetést: a kifizetett időszak végéig használható, utána nem terhelünk."
            : "Elmentve." });
    }

    [Authorize(Roles = UsersController.AdminRole)]
    [EnableRateLimiting(AuthController.RateLimitPolicy)]
    [HttpPost("checkout")]
    public async Task<ActionResult<CheckoutResponse>> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Interval)) return BadRequest(new { message = "Érvénytelen gyakoriság." });
        if (!request.AcceptImmediatePerformance)
            return BadRequest(new { message = "A fizetéshez fogadd el az ÁSZF-et, és kérd a szolgáltatás azonnali megkezdését." });
        var me = await db.Users.FindAsync([User.GetUserId()], ct);
        var result = await service.StartCheckoutAsync(request.Interval, me?.Email ?? "", ct);
        return result.Success ? Ok(new CheckoutResponse(result.GatewayUrl!)) : BadRequest(new { message = result.Error });
    }

    // A Barionról visszatérve a böngésző ezzel kéri a fizetés frissítését (a callback mellett).
    [HttpPost("sync")]
    public async Task<ActionResult<SubscriptionPaymentDto>> Sync(SyncPaymentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PaymentId) || request.PaymentId.Length > 100) return BadRequest();
        var payment = await service.SyncPaymentAsync(request.PaymentId.Trim(), ct);
        if (payment is null) return NotFound(new { message = "Ismeretlen fizetés." });
        return Ok(new SubscriptionPaymentDto(payment.Id, payment.Kind, payment.Status, payment.Interval, payment.PropertyCount,
            payment.GrossAmount, payment.PeriodStart, payment.PeriodEnd, payment.CreatedAt, payment.ErrorMessage,
            payment.BillingoPublicUrl, payment.Status == SubscriptionPaymentStatus.Succeeded && payment.BillingoDocumentId is null));
    }

    private async Task<SubscriptionStatusDto> StatusAsync(CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (!settings.BillingEnabled || platform.IsOperator(companyContext.CompanyId))
        {
            var placeholder = await db.Subscriptions.SingleOrDefaultAsync(ct);
            return new SubscriptionStatusDto(settings.BillingEnabled, platform.IsOperator(companyContext.CompanyId),
                placeholder?.Status ?? SubscriptionStatus.Trial, false, placeholder?.TrialEndsAt ?? DateTime.UtcNow,
                placeholder?.PaidUntil, placeholder?.GraceEndsAt, placeholder?.CancelAtPeriodEnd ?? false);
        }
        return ToStatus(await service.GetOrCreateAsync(ct), settings);
    }

    private SubscriptionStatusDto ToStatus(Subscription s, PlatformSettings settings)
    {
        var isOperator = platform.IsOperator(companyContext.CompanyId);
        return new SubscriptionStatusDto(settings.BillingEnabled, isOperator, s.Status,
            SubscriptionRules.IsLocked(s, settings, isOperator, DateTime.UtcNow), s.TrialEndsAt, s.PaidUntil, s.GraceEndsAt, s.CancelAtPeriodEnd);
    }

    private static QuoteDto ToDto(PriceQuote q) =>
        new(q.Interval, q.PropertyCount, q.UnitPrice, q.UnitPriceIncludesVat, q.NetAmount, q.VatAmount, q.GrossAmount, q.VatPercent, q.VatExemption);
}

// A Barion szerver-oldali értesítése (CallbackUrl): POST, "PaymentId" mezővel. Bejelentkezés nélküli,
// de csak egy azonosítót fogad el — az állapotot mindig a Bariontól kérdezzük le (SyncPaymentAsync),
// így hamis hívással semmit nem lehet elérni. Mindig 200-at ad, különben a Barion ismételget.
[ApiController]
[Route("api/public/barion")]
public class BarionCallbackController(SubscriptionService service, ILogger<BarionCallbackController> logger) : ControllerBase
{
    [EnableRateLimiting(AuthController.RateLimitPolicy)]
    [HttpPost("callback")]
    public async Task<IActionResult> Callback(CancellationToken ct)
    {
        string? paymentId = Request.Query["paymentId"].FirstOrDefault() ?? Request.Query["PaymentId"].FirstOrDefault();
        if (paymentId is null && Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            paymentId = form["PaymentId"].FirstOrDefault() ?? form["paymentId"].FirstOrDefault();
        }
        if (string.IsNullOrWhiteSpace(paymentId) || paymentId.Length > 100) return Ok();

        try
        {
            await service.SyncPaymentAsync(paymentId.Trim(), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Barion callback handling failed for {PaymentId}", paymentId);
        }
        return Ok();
    }
}
