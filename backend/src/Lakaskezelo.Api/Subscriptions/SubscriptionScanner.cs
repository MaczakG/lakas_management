using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Subscriptions;

// Félóránként végigmegy a cégeken (az üzemeltetőn kívül), ha a díjszedés be van kapcsolva:
//  - elmaradt Barion-visszajelzések pótlása (függő fizetések lekérdezése),
//  - el nem készült Billingo-számlák újrapróbálása,
//  - próbaidő lejárta előtti emlékeztető, lejártakor zárolás,
//  - esedékes megújítás terhelése; sikertelennél türelmi idő, utána zárolás; lemondottnál zárolás.
// Cégenként külön scope és try/catch — egy cég hibája nem akasztja meg a többit.
public class SubscriptionScanner(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<SubscriptionScanner> logger) : BackgroundService
{
    // Subscriptions:ScanIntervalSeconds csak helyi teszthez rövidítendő.
    private TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Subscriptions:ScanIntervalSeconds", 1800)));
    private static readonly TimeSpan PendingSyncAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryRenewalAfter = TimeSpan.FromHours(23);
    private static readonly TimeSpan TrialReminderBefore = TimeSpan.FromDays(3);
    private const int MaxBillingoAttempts = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Interval < TimeSpan.FromMinutes(1) ? Interval : TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Subscription scan failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> companyIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var platform = scope.ServiceProvider.GetRequiredService<PlatformSettingsService>();
            PlatformSettingsService.Invalidate();
            var settings = await platform.GetAsync(ct);
            if (!settings.BillingEnabled) return;
            var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
            companyIds = await db.Companies.Where(c => c.Id != platform.OperatorCompanyId).Select(c => c.Id).ToListAsync(ct);
        }

        foreach (var companyId in companyIds)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
                await ProcessCompanyAsync(scope.ServiceProvider, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Subscription scan failed for company {CompanyId}", companyId);
            }
        }
    }

    private static async Task ProcessCompanyAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<LakaskezeloDbContext>();
        var service = services.GetRequiredService<SubscriptionService>();
        var emails = services.GetRequiredService<SubscriptionEmails>();
        var now = DateTime.UtcNow;

        // Meg nem erősített regisztrációhoz nem kezdünk próbaidőt (azt a RegistrationCleanupService törli).
        if (!await db.Users.AnyAsync(u => u.EmailVerifiedAt != null, ct)) return;

        var pending = await db.SubscriptionPayments
            .Where(p => p.Status == SubscriptionPaymentStatus.Pending && p.BarionPaymentId != null && p.CreatedAt < now - PendingSyncAfter)
            .Select(p => p.BarionPaymentId!).ToListAsync(ct);
        foreach (var id in pending) await service.SyncPaymentAsync(id, ct);

        var uninvoiced = await db.SubscriptionPayments
            .Where(p => p.Status == SubscriptionPaymentStatus.Succeeded && p.BillingoDocumentId == null && p.BillingoAttempts < MaxBillingoAttempts)
            .ToListAsync(ct);
        foreach (var payment in uninvoiced) await service.IssueInvoiceAsync(payment, ct);

        var s = await service.GetOrCreateAsync(ct);
        switch (s.Status)
        {
            case SubscriptionStatus.Trial when now > s.TrialEndsAt:
                await LockAsync(db, emails, s, "Az ingyenes próbaidő lejárt.", ct);
                break;
            case SubscriptionStatus.Trial when s.TrialReminderSentAt is null && s.TrialEndsAt - now < TrialReminderBefore:
                s.TrialReminderSentAt = now;
                await db.SaveChangesAsync(ct);
                await emails.TrialEndingAsync(s, ct);
                break;
            case SubscriptionStatus.Active when s.PaidUntil is { } until && until <= now:
                if (s.CancelAtPeriodEnd || s.BarionRecurrenceId is null)
                {
                    await LockAsync(db, emails, s, s.CancelAtPeriodEnd ? "Az előfizetést lemondtad, és a kifizetett időszak véget ért." : "Az előfizetés lejárt.", ct);
                }
                else if (!await RenewalAttemptedRecentlyAsync(db, now, ct))
                {
                    await service.ChargeRenewalAsync(s, ct);
                }
                break;
            case SubscriptionStatus.PastDue when s.GraceEndsAt is { } grace && now > grace:
                await LockAsync(db, emails, s, "A megújító fizetés nem sikerült, és a türelmi idő lejárt.", ct);
                break;
            case SubscriptionStatus.PastDue when !s.CancelAtPeriodEnd && s.BarionRecurrenceId is not null:
                if (!await RenewalAttemptedRecentlyAsync(db, now, ct)) await service.ChargeRenewalAsync(s, ct);
                break;
        }
    }

    private static Task<bool> RenewalAttemptedRecentlyAsync(LakaskezeloDbContext db, DateTime now, CancellationToken ct) =>
        db.SubscriptionPayments.AnyAsync(p => p.Kind == SubscriptionPaymentKind.Renewal && p.CreatedAt > now - RetryRenewalAfter, ct);

    private static async Task LockAsync(LakaskezeloDbContext db, SubscriptionEmails emails, Subscription s, string reason, CancellationToken ct)
    {
        s.Status = SubscriptionStatus.Locked;
        s.LockedAt = DateTime.UtcNow;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await emails.LockedAsync(s, reason, ct);
    }
}
