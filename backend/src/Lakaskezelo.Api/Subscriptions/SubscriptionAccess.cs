using Lakaskezelo.Data;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Subscriptions;

// Zárolt-e (csak olvasható-e) egy cég — az író kérések (SubscriptionWriteGuard) és a háttérben
// kiküldött bérlői számlák/emlékeztetők előtt.
public class SubscriptionAccess(LakaskezeloDbContext db, PlatformSettingsService platform)
{
    public async Task<bool> IsLockedAsync(Guid companyId, CancellationToken ct)
    {
        var settings = await platform.GetAsync(ct);
        if (!settings.BillingEnabled || platform.IsOperator(companyId)) return false;
        var subscription = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == companyId, ct);
        return SubscriptionRules.IsLocked(subscription, settings, isOperator: false, DateTime.UtcNow);
    }
}

// Zárolt cégnél minden módosító API-kérés 402-t kap (az adatok olvashatók és letölthetők maradnak),
// kivéve a belépést és az előfizetés rendezését.
public class SubscriptionWriteGuard(RequestDelegate next)
{
    private static readonly string[] AllowedPrefixes = ["/api/auth", "/api/subscription", "/api/public", "/api/platform"];

    public async Task InvokeAsync(HttpContext context, ICompanyContext companyContext, SubscriptionAccess access)
    {
        var request = context.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
            && request.Path.StartsWithSegments("/api")
            && !AllowedPrefixes.Any(p => request.Path.StartsWithSegments(p))
            && companyContext.CompanyId is { } companyId
            && await access.IsLockedAsync(companyId, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "A fiók csak olvasható, mert nincs érvényes előfizetés. Az Előfizetés oldalon újraindíthatod.",
            }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
