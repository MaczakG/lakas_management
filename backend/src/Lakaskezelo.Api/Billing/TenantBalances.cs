using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Az egyenlegszámítás közös része a bérlők (TenantsController) és a Vezérlőpult (DashboardController) között.
public static class TenantBalances
{
    // A még bizonylatra nem került (nyitott) rezsitételek, az ingatlan jelenlegi bérlőjéhez rendelve — ő kapja a következő
    // bizonylaton. Csak a következő bizonylatra kerülhetők számítanak: nem kizárt, legfeljebb 36 hónapos, van összege, és nem
    // „tovább számlázandó” (azt a bérleti díj tartalmazza, nem a bérlő tartozása).
    public static async Task<List<(Guid TenantId, UtilityCostEntry Entry)>> OpenUtilitiesAsync(LakaskezeloDbContext db, Guid? tenantId, CancellationToken ct)
    {
        var now = BudapestClock.Now;
        var today = DateOnly.FromDateTime(now);
        var current = (await db.Tenants
                .Where(t => t.PropertyId != null && (t.MoveOutDate == null || t.MoveOutDate >= today))
                .Select(t => new { t.Id, PropertyId = t.PropertyId!.Value, t.MoveOutDate, t.MoveInDate })
                .ToListAsync(ct))
            .GroupBy(t => t.PropertyId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.MoveOutDate == null).ThenByDescending(t => t.MoveInDate).First().Id);
        var propertyIds = current.Where(kv => tenantId == null || kv.Value == tenantId).Select(kv => kv.Key).ToList();
        if (propertyIds.Count == 0) return [];
        var (fromYear, fromMonth) = InvoiceGenerationService.BillingWindowStart(now.Year, now.Month);
        var fromKey = fromYear * 12 + fromMonth;
        var entries = await db.UtilityCostEntries
            .Where(u => propertyIds.Contains(u.PropertyId) && u.InvoiceId == null && !u.ExcludedFromBilling && u.Amount != 0 && !u.ForwardBilled
                && u.Year * 12 + u.Month >= fromKey)
            .ToListAsync(ct);
        return [.. entries.Select(u => (current[u.PropertyId], u))];
    }
}
