using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Az egyenlegszámítás közös része a bérlők (TenantsController) és a Vezérlőpult (DashboardController) között.
public static class TenantBalances
{
    // Az utólagos rezsitételek, az ingatlan jelenlegi bérlőjéhez rendelve — ő kapja a következő bizonylaton. Utólagos
    // a tétel, ha a hónapjára (csúszással fizetőnél a következő hónapra) az ingatlannak már van érvényes bizonylata,
    // vagyis egy már kiszámlázott hónaphoz került fel utólag: ez már tartozás, beleszámít az egyenlegbe. Csak a következő
    // bizonylatra kerülhetők számítanak: nem kizárt, legfeljebb 36 hónapos, van összege, és nem „tovább számlázandó”.
    // A még ki nem számlázott hónapok (pl. az előre kitöltött 24 hónap) tételei nem tartozás, ezért be sem töltjük őket:
    // a lekérdezés a legkésőbbi kiszámlázott hónapnál megáll.
    public static async Task<List<(Guid TenantId, UtilityCostEntry Entry)>> LateUtilitiesAsync(LakaskezeloDbContext db, Guid? tenantId, CancellationToken ct)
    {
        var now = BudapestClock.Now;
        var today = DateOnly.FromDateTime(now);
        var current = (await db.Tenants
                .Where(t => t.PropertyId != null && (t.MoveOutDate == null || t.MoveOutDate >= today))
                .Select(t => new { t.Id, PropertyId = t.PropertyId!.Value, t.MoveOutDate, t.MoveInDate, t.UtilityBillingTiming })
                .ToListAsync(ct))
            .GroupBy(t => t.PropertyId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.MoveOutDate == null).ThenByDescending(t => t.MoveInDate).First());
        var propertyIds = current.Where(kv => tenantId == null || kv.Value.Id == tenantId).Select(kv => kv.Key).ToList();
        if (propertyIds.Count == 0) return [];
        var invoiced = (await db.Invoices
                .Where(i => propertyIds.Contains(i.PropertyId) && i.Status != InvoiceStatus.Failed && i.CancelledAt == null && i.CancelsInvoiceId == null)
                .Select(i => new { i.PropertyId, i.PeriodYear, i.PeriodMonth })
                .Distinct().ToListAsync(ct))
            .Select(i => (i.PropertyId, i.PeriodYear, i.PeriodMonth)).ToHashSet();
        if (invoiced.Count == 0) return [];
        var (fromYear, fromMonth) = InvoiceGenerationService.BillingWindowStart(now.Year, now.Month);
        var fromKey = fromYear * 12 + fromMonth;
        var toKey = invoiced.Max(i => i.PeriodYear * 12 + i.PeriodMonth);
        var entries = await db.UtilityCostEntries
            .Where(u => propertyIds.Contains(u.PropertyId) && u.InvoiceId == null && !u.ExcludedFromBilling && u.Amount != 0 && !u.ForwardBilled
                && u.Year * 12 + u.Month >= fromKey && u.Year * 12 + u.Month <= toKey)
            .ToListAsync(ct);
        return [.. entries
            .Where(u => IsLate(u, invoiced, current[u.PropertyId].UtilityBillingTiming == UtilityBillingTiming.PreviousMonth ? 1 : 0))
            .Select(u => (current[u.PropertyId].Id, u))];
    }

    // Utólagos a tétel, ha már van érvényes (nem sikertelen, nem sztornózott) bizonylata annak a hónapnak, amelynek a
    // bizonylata ezt a rezsit vinné: alapból a tétel hónapja, egy hónap csúszással fizető bérlőnél (lag = 1) a
    // következő hónap. (A csúszással fizetőnél a tárgyhavi rezsi a következő bizonylatig nem tartozás.)
    public static bool IsLate(UtilityCostEntry entry, IReadOnlySet<(Guid PropertyId, int Year, int Month)> invoicedMonths, int lag = 0)
    {
        var carrier = new DateOnly(entry.Year, entry.Month, 1).AddMonths(lag);
        return invoicedMonths.Contains((entry.PropertyId, carrier.Year, carrier.Month));
    }
}
