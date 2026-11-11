using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// Az egyenlegszámítás közös része a bérlők (TenantsController) és a Vezérlőpult (DashboardController) között.
public static class TenantBalances
{
    // A még bizonylatra nem került (nyitott) rezsitételek, az ingatlan jelenlegi bérlőjéhez rendelve — ő kapja a következő
    // bizonylaton. Csak a következő bizonylatra kerülhetők számítanak: nem kizárt, legfeljebb 36 hónapos, van összege, és
    // nem „tovább számlázandó” (azt a bérleti díj tartalmazza, nem a bérlő tartozása).
    // Late (utólagos tétel): a tétel hónapjára az ingatlannak már van érvényes bizonylata, vagyis a tétel egy már
    // kiszámlázott hónaphoz utólag került fel. Ez már tartozás: beleszámít az egyenlegbe. A többi nyitott tétel (a még
    // ki nem számlázott hónapé, pl. az előre behúzott rezsi) nem — az csak tájékoztató összeg.
    public static async Task<List<(Guid TenantId, UtilityCostEntry Entry, bool Late)>> OpenUtilitiesAsync(LakaskezeloDbContext db, Guid? tenantId, CancellationToken ct)
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
        var (fromYear, fromMonth) = InvoiceGenerationService.BillingWindowStart(now.Year, now.Month);
        var fromKey = fromYear * 12 + fromMonth;
        var entries = await db.UtilityCostEntries
            .Where(u => propertyIds.Contains(u.PropertyId) && u.InvoiceId == null && !u.ExcludedFromBilling && u.Amount != 0 && !u.ForwardBilled
                && u.Year * 12 + u.Month >= fromKey)
            .ToListAsync(ct);
        if (entries.Count == 0) return [];
        var entryProperties = entries.Select(u => u.PropertyId).Distinct().ToList();
        var invoiced = (await db.Invoices
                .Where(i => entryProperties.Contains(i.PropertyId) && i.Status != InvoiceStatus.Failed && i.CancelledAt == null && i.CancelsInvoiceId == null)
                .Select(i => new { i.PropertyId, i.PeriodYear, i.PeriodMonth })
                .Distinct().ToListAsync(ct))
            .Select(i => (i.PropertyId, i.PeriodYear, i.PeriodMonth)).ToHashSet();
        return [.. entries.Select(u => (current[u.PropertyId].Id, u,
            IsLate(u, invoiced, current[u.PropertyId].UtilityBillingTiming == UtilityBillingTiming.PreviousMonth ? 1 : 0)))];
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
