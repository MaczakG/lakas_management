using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Subscriptions;

// Az előfizetés életciklusa: próbaidő → kártyás fizetés a Barion oldalán (a kártya regisztrálásával)
// → automatikus megújítás a regisztrált kártyára → sikertelen terhelésnél türelmi idő → zárolás.
// Minden sikeres fizetésről Billingo-számla készül. A Barion-állapotot soha nem a böngészőtől vagy a
// callback törzséből vesszük át: mindig a Bariontól kérdezzük le (GetPaymentState) a fizetés
// azonosítója alapján, így a végpontok nem hamisíthatók.
public class SubscriptionService(
    LakaskezeloDbContext db, ICompanyContext companyContext, PlatformSettingsService platform,
    BarionClient barion, BillingoClient billingo, SubscriptionEmails emails, IConfiguration configuration,
    ILogger<SubscriptionService> logger)
{
    private string ProductName => configuration["App:ProductName"] is { Length: > 0 } n ? n : "Lakáskezelő";
    private string FrontendBase => configuration["Frontend:BaseUrl"] ?? "";

    public static string IntervalLabel(BillingInterval interval) => interval == BillingInterval.Yearly ? "éves" : "havi";

    // A kérés cégének előfizetése; első alkalommal próbaidővel jön létre.
    public async Task<Subscription> GetOrCreateAsync(CancellationToken ct)
    {
        var existing = await db.Subscriptions.SingleOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var settings = await platform.GetAsync(ct);
        var now = DateTime.UtcNow;
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatus.Trial,
            TrialEndsAt = now.AddDays(Math.Max(0, settings.TrialDays)),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Subscriptions.Add(subscription);
        try
        {
            await db.SaveChangesAsync(ct);
            return subscription;
        }
        catch (DbUpdateException)
        {
            // Párhuzamos kérés közben létrehozta (egyedi index a CompanyId-n).
            db.Entry(subscription).State = EntityState.Detached;
            return await db.Subscriptions.SingleAsync(ct);
        }
    }

    public Task<int> ActivePropertyCountAsync(CancellationToken ct) => db.Properties.CountAsync(p => p.IsActive, ct);

    public static string? BillingDetailsMissing(Subscription s)
    {
        if (string.IsNullOrWhiteSpace(s.BillingName) || string.IsNullOrWhiteSpace(s.BillingPostCode)
            || string.IsNullOrWhiteSpace(s.BillingCity) || string.IsNullOrWhiteSpace(s.BillingStreet))
        {
            return "Előbb add meg a számlázási adatokat (név és cím).";
        }
        return null;
    }

    public record CheckoutResult(bool Success, string? GatewayUrl, string? Error);

    public async Task<CheckoutResult> StartCheckoutAsync(BillingInterval interval, string payerEmail, CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (!settings.BillingEnabled) return new(false, null, "Az előfizetés jelenleg nem érhető el.");
        if (!BarionClient.IsConfigured(settings)) return new(false, null, "A kártyás fizetés még nincs beállítva. Kérjük, próbáld később.");

        var subscription = await GetOrCreateAsync(ct);
        if (BillingDetailsMissing(subscription) is { } missing) return new(false, null, missing);

        var now = DateTime.UtcNow;
        if (subscription.Status == SubscriptionStatus.Active && subscription.PaidUntil > now && subscription.BarionRecurrenceId is not null)
        {
            return new(false, null, "Az előfizetésed aktív, a megújítás automatikus.");
        }

        // Próbaidő alatt fizetve a próbaidő megmarad: a fizetett időszak a végétől indul.
        var start = subscription.Status == SubscriptionStatus.Trial && subscription.TrialEndsAt > now ? subscription.TrialEndsAt : now;
        var quote = Pricing.Quote(settings, await ActivePropertyCountAsync(ct), interval);
        var payment = NewPayment(SubscriptionPaymentKind.Checkout, quote, start, Guid.NewGuid().ToString("N"));
        payment.ImmediatePerformanceConsentAt = now;
        db.SubscriptionPayments.Add(payment);
        await db.SaveChangesAsync(ct);

        var result = await barion.StartPaymentAsync(settings, payment.Id.ToString("N"), payerEmail, quote.GrossAmount,
            ItemName(quote, payment), quote.PropertyCount, "ingatlan", Math.Round(quote.GrossAmount / quote.PropertyCount, 2),
            redirectUrl: $"{FrontendBase}/subscription.html", callbackUrl: $"{FrontendBase}/api/public/barion/callback",
            recurrenceId: payment.RecurrenceId!, initiateRecurrence: true, traceId: null, ct);

        if (!result.Success || string.IsNullOrEmpty(result.GatewayUrl))
        {
            payment.Status = SubscriptionPaymentStatus.Failed;
            payment.ErrorMessage = Truncate(result.ErrorMessage ?? "A Barion nem adott vissza fizetőoldalt.");
            payment.CompletedAt = now;
            await db.SaveChangesAsync(ct);
            return new(false, null, "Nem sikerült elindítani a fizetést: " + payment.ErrorMessage);
        }

        payment.BarionPaymentId = result.PaymentId;
        await db.SaveChangesAsync(ct);
        return new(true, result.GatewayUrl, null);
    }

    // A fizetés állapotának frissítése a Bariontól (callback, visszairányítás vagy ütemező). Cég-kontextus
    // nélkül is hívható: a fizetés cégére áll.
    public async Task<SubscriptionPayment?> SyncPaymentAsync(string barionPaymentId, CancellationToken ct)
    {
        var payment = await db.SubscriptionPayments.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.BarionPaymentId == barionPaymentId, ct);
        if (payment is null) return null;
        if (companyContext.CompanyId is { } current && current != payment.CompanyId) return null;
        companyContext.Set(payment.CompanyId);
        if (payment.Status != SubscriptionPaymentStatus.Pending) return payment;

        var settings = await platform.GetAsync(ct);
        var state = await barion.GetPaymentStateAsync(settings, barionPaymentId, ct);
        if (!state.Success) return payment;

        if (BarionClient.IsSucceeded(state.Status)) await CompletePaymentAsync(payment, state.TraceId, ct);
        else if (BarionClient.IsFinalFailure(state.Status)) await FailPaymentAsync(payment, $"A fizetés nem sikerült ({state.Status}).", ct);
        return payment;
    }

    // Automatikus megújítás a regisztrált kártyára (cég-kontextusban).
    public async Task ChargeRenewalAsync(Subscription subscription, CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (!BarionClient.IsConfigured(settings) || subscription.BarionRecurrenceId is null) return;

        var now = DateTime.UtcNow;
        var start = subscription.PaidUntil is { } until && until > now.AddDays(-settings.GraceDays) ? until : now;
        var quote = Pricing.Quote(settings, await ActivePropertyCountAsync(ct), subscription.Interval);
        var payment = NewPayment(SubscriptionPaymentKind.Renewal, quote, start, subscription.BarionRecurrenceId);
        db.SubscriptionPayments.Add(payment);
        await db.SaveChangesAsync(ct);

        var payer = subscription.BillingEmail ?? await FirstAdminEmailAsync(ct) ?? "";
        var result = await barion.StartPaymentAsync(settings, payment.Id.ToString("N"), payer, quote.GrossAmount,
            ItemName(quote, payment), quote.PropertyCount, "ingatlan", Math.Round(quote.GrossAmount / quote.PropertyCount, 2),
            redirectUrl: $"{FrontendBase}/subscription.html", callbackUrl: $"{FrontendBase}/api/public/barion/callback",
            recurrenceId: subscription.BarionRecurrenceId, initiateRecurrence: false, traceId: subscription.BarionTraceId, ct);

        if (!result.Success || result.PaymentId is null)
        {
            await FailPaymentAsync(payment, result.ErrorMessage ?? "A Barion elutasította a terhelést.", ct);
            return;
        }
        payment.BarionPaymentId = result.PaymentId;
        await db.SaveChangesAsync(ct);
        await SyncPaymentAsync(result.PaymentId, ct);
    }

    private async Task CompletePaymentAsync(SubscriptionPayment payment, string? traceId, CancellationToken ct)
    {
        // Feltételes átállítás: ha a callback és a visszairányítás egyszerre érkezik, csak az egyik dolgozza fel.
        var now = DateTime.UtcNow;
        var claimed = await db.SubscriptionPayments.IgnoreQueryFilters()
            .Where(p => p.Id == payment.Id && p.Status == SubscriptionPaymentStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, SubscriptionPaymentStatus.Succeeded).SetProperty(p => p.CompletedAt, now), ct);
        await db.Entry(payment).ReloadAsync(ct);
        if (claimed == 0) return;

        var subscription = await GetOrCreateAsync(ct);
        subscription.Status = SubscriptionStatus.Active;
        subscription.PaidUntil = subscription.PaidUntil is { } until && until > payment.PeriodEnd ? until : payment.PeriodEnd;
        subscription.GraceEndsAt = null;
        subscription.LockedAt = null;
        subscription.UpdatedAt = now;
        if (payment.Kind == SubscriptionPaymentKind.Checkout)
        {
            subscription.BarionRecurrenceId = payment.RecurrenceId;
            subscription.BarionTraceId = traceId;
            subscription.CardRegisteredAt = now;
            subscription.Interval = payment.Interval;
            subscription.CancelAtPeriodEnd = false;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Subscription payment {PaymentId} succeeded for company {CompanyId}", payment.Id, payment.CompanyId);

        await IssueInvoiceAsync(payment, ct);
        await emails.PaymentSucceededAsync(payment, ct);
    }

    private async Task FailPaymentAsync(SubscriptionPayment payment, string message, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var claimed = await db.SubscriptionPayments.IgnoreQueryFilters()
            .Where(p => p.Id == payment.Id && p.Status == SubscriptionPaymentStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, SubscriptionPaymentStatus.Failed)
                .SetProperty(p => p.CompletedAt, now).SetProperty(p => p.ErrorMessage, Truncate(message)), ct);
        await db.Entry(payment).ReloadAsync(ct);
        if (claimed == 0 || payment.Kind != SubscriptionPaymentKind.Renewal) return;

        var settings = await platform.GetAsync(ct);
        var subscription = await GetOrCreateAsync(ct);
        if (subscription.Status != SubscriptionStatus.Locked)
        {
            var notifyFirst = subscription.Status != SubscriptionStatus.PastDue;
            subscription.Status = SubscriptionStatus.PastDue;
            subscription.GraceEndsAt ??= (subscription.PaidUntil ?? now).AddDays(settings.GraceDays);
            subscription.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            if (notifyFirst) await emails.PaymentFailedAsync(subscription, message, ct);
        }
        logger.LogWarning("Subscription renewal {PaymentId} failed for company {CompanyId}: {Message}", payment.Id, payment.CompanyId, message);
    }

    // Billingo-számla egy sikeres fizetésről (újrapróbálható: ha már van dokumentuma, nem csinál semmit).
    public async Task IssueInvoiceAsync(SubscriptionPayment payment, CancellationToken ct)
    {
        if (payment.Status != SubscriptionPaymentStatus.Succeeded || payment.BillingoDocumentId is not null) return;
        var settings = await platform.GetAsync(ct);
        if (!BillingoClient.IsConfigured(settings))
        {
            payment.BillingoError = "A Billingo még nincs beállítva — a számla a beállítás után készül el.";
            await db.SaveChangesAsync(ct);
            return;
        }

        var subscription = await GetOrCreateAsync(ct);
        var company = await db.Companies.SingleAsync(c => c.Id == payment.CompanyId, ct);
        var buyer = new BillingoBuyer(
            subscription.BillingName ?? company.Name,
            subscription.BillingEmail ?? await FirstAdminEmailAsync(ct),
            string.IsNullOrWhiteSpace(subscription.BillingTaxNumber) ? null : subscription.BillingTaxNumber,
            subscription.BillingPostCode ?? "", subscription.BillingCity ?? "", subscription.BillingStreet ?? "",
            subscription.BillingoPartnerId);
        var line = new BillingoLine(
            $"{ProductName} előfizetés, {IntervalLabel(payment.Interval)} díj ({payment.PeriodStart:yyyy.MM.dd.}–{payment.PeriodEnd.AddDays(-1):yyyy.MM.dd.})",
            payment.PropertyCount, "ingatlan", payment.UnitPrice, payment.UnitPriceIncludesVat, payment.VatPercent, payment.VatExemption);

        var result = await billingo.IssuePaidInvoiceAsync(settings, buyer, [line], DateOnly.FromDateTime(payment.CompletedAt ?? DateTime.UtcNow),
            $"Barion fizetés: {payment.BarionPaymentId}", ct);
        payment.BillingoAttempts += 1;
        if (result.PartnerId is { } partnerId) subscription.BillingoPartnerId = partnerId;
        if (result.Success)
        {
            payment.BillingoDocumentId = result.DocumentId;
            payment.BillingoPublicUrl = result.PublicUrl;
            payment.BillingoError = null;
        }
        else
        {
            payment.BillingoError = Truncate(result.ErrorMessage ?? "Ismeretlen Billingo-hiba.");
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> FirstAdminEmailAsync(CancellationToken ct) =>
        await db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive && u.EmailVerifiedAt != null)
            .OrderBy(u => u.CreatedAt).Select(u => u.Email).FirstOrDefaultAsync(ct);

    private static SubscriptionPayment NewPayment(SubscriptionPaymentKind kind, PriceQuote quote, DateTime start, string recurrenceId) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Status = SubscriptionPaymentStatus.Pending,
        Interval = quote.Interval,
        PropertyCount = quote.PropertyCount,
        UnitPrice = quote.UnitPrice,
        UnitPriceIncludesVat = quote.UnitPriceIncludesVat,
        NetAmount = quote.NetAmount,
        VatAmount = quote.VatAmount,
        GrossAmount = quote.GrossAmount,
        VatPercent = quote.VatPercent,
        VatExemption = quote.VatExemption,
        PeriodStart = start,
        PeriodEnd = Pricing.PeriodEnd(start, quote.Interval),
        RecurrenceId = recurrenceId,
        CreatedAt = DateTime.UtcNow,
    };

    private string ItemName(PriceQuote quote, SubscriptionPayment payment) =>
        $"{ProductName} előfizetés ({IntervalLabel(quote.Interval)}, {quote.PropertyCount} ingatlan)";

    private static string Truncate(string value) => value.Length <= 1000 ? value : value[..1000];
}
