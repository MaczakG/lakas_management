using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// A hónap rezsijének előkészítése az ingatlan rezsi tételeiből (UtilityItem), és a hónap rezsi-állapota.
//
// Előkészítéskor minden rezsi tételből, amelynek a hónapban még nincs tétele, készül egy havi tétel: fix tételnél a
// megadott összeggel, nem fixnél 0 összeggel („kitöltendő”). Fut gombnyomásra (force: a hiányzókat mindig pótolja),
// a rezsi ablak megnyitásakor (ha biztonságos, ld. ShouldAutoPrepare), a rezsi-határnapon és a bizonylat
// kiállításakor (automatikusan: hónaponként csak egyszer, hogy a kézzel törölt tétel ne jöjjön vissza). Az utólag felvett egyedi tételek ettől függetlenül ugyanúgy bizonylatra kerülnek.
public static class UtilityPeriods
{
    public const string Ok = "Ok";           // minden rendben: nincs teendő a bizonylat előtt
    public const string Pending = "Pending"; // van kitöltendő (nem fix, még összeg nélküli) tétel
    public const string Missing = "Missing"; // nincs rezsi tétel és nincs rögzített tétel sem

    // A létrehozott tételek száma. A mentést (SaveChanges) a hívó végzi.
    public static async Task<int> PrepareAsync(LakaskezeloDbContext db, Guid propertyId, int year, int month, bool force, CancellationToken ct)
    {
        var items = await db.UtilityItems.Where(i => i.PropertyId == propertyId).OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt).ToListAsync(ct);
        // Rezsi tételek nélkül nincs mit előkészíteni (és a hónap sem számít előkészítettnek: a rezsi kézzel megy).
        if (items.Count == 0) return 0;

        var period = await db.UtilityPeriods.FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.Year == year && p.Month == month, ct);
        if (period is not null && !force) return 0;

        var existing = await db.UtilityCostEntries
            .Where(u => u.PropertyId == propertyId && u.Year == year && u.Month == month && u.UtilityItemId != null)
            .Select(u => u.UtilityItemId!.Value).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var created = 0;
        foreach (var item in items.Where(i => !existing.Contains(i.Id)))
        {
            db.UtilityCostEntries.Add(new UtilityCostEntry
            {
                Id = Guid.NewGuid(),
                PropertyId = propertyId,
                Year = year,
                Month = month,
                Label = item.Label,
                Amount = item.IsFixed ? item.Amount ?? 0 : 0,
                ForwardBilled = item.ForwardBilled,
                UtilityItemId = item.Id,
                // A tételek a rezsi tételek sorrendjében kövessék egymást.
                CreatedAt = now.AddMilliseconds(created),
            });
            created++;
        }
        if (period is null)
        {
            db.UtilityPeriods.Add(new UtilityPeriod { Id = Guid.NewGuid(), PropertyId = propertyId, Year = year, Month = month, PreparedAt = now });
        }
        return created;
    }

    // A rezsi ablak megnyitásakor a hónap magától előkészül-e a rezsi tételekből (hogy a tételek „alapból ott
    // legyenek”). Csak akkor, ha a megnyitás nem okozhat meglepetést:
    //  - a hónapban még nincs egyetlen tétel sem (a kézzel felvitt tételek mellé nem tesz másikat);
    //  - a hónapra még nincs érvényes bizonylat (különben a fix tétel utólagos tételként még egyszer számlázódna);
    //  - a hónapban volt bérlője az ingatlannak (bérlő előtti hónap tételei nem terhelhetők a mostani bérlőre);
    //  - a hónap nem későbbi a következő bizonylat hónapjánál, és belül van a visszamenőleges rögzítés ablakán.
    // Az időszakok év × 12 + hónap alakban jönnek. Hogy a hónap már elő volt-e készítve, azt a PrepareAsync nézi.
    public static bool ShouldAutoPrepare(bool hasEntries, bool hasActiveInvoice, bool tenantInMonth, int period, int nextBillingPeriod, int windowStart) =>
        !hasEntries && !hasActiveInvoice && tenantInMonth && period <= nextBillingPeriod && period >= windowStart;

    // Egy hónap rezsi-állapota a bizonylat előtt.
    //  - Van tétele vagy már elő van készítve: kitöltendő tétel esetén Pending, különben Ok.
    //  - Még nincs előkészítve, de vannak rezsi tételek: a fixek maguktól bekerülnek (Ok); ha nem fix is van, azt
    //    majd ki kell tölteni (Pending).
    //  - Se rezsi tétel, se rögzített tétel: Missing (a rezsit kézzel kell felvenni).
    public static string State(int entryCount, int pendingCount, bool prepared, int itemCount, int nonFixedItemCount)
    {
        if (entryCount > 0 || prepared) return pendingCount > 0 ? Pending : Ok;
        if (itemCount == 0) return Missing;
        return nonFixedItemCount > 0 ? Pending : Ok;
    }

    // Több ingatlan egy-egy hónapjának állapota egyszerre (a kulcs: ingatlan, év, hónap).
    public static async Task<Dictionary<(Guid PropertyId, int Year, int Month), string>> StatesAsync(
        LakaskezeloDbContext db, IReadOnlyCollection<(Guid PropertyId, int Year, int Month)> periods, CancellationToken ct)
    {
        var result = new Dictionary<(Guid, int, int), string>();
        if (periods.Count == 0) return result;
        var ids = periods.Select(p => p.PropertyId).Distinct().ToList();
        var keys = periods.Select(p => p.Year * 100 + p.Month).Distinct().ToList();

        var entries = (await db.UtilityCostEntries
                .Where(u => ids.Contains(u.PropertyId) && keys.Contains(u.Year * 100 + u.Month))
                .GroupBy(u => new { u.PropertyId, u.Year, u.Month })
                .Select(g => new { g.Key.PropertyId, g.Key.Year, g.Key.Month, Count = g.Count(), Pending = g.Count(u => u.Amount == 0) })
                .ToListAsync(ct))
            .ToDictionary(e => (e.PropertyId, e.Year, e.Month));
        var prepared = (await db.UtilityPeriods
                .Where(p => ids.Contains(p.PropertyId) && keys.Contains(p.Year * 100 + p.Month))
                .Select(p => new { p.PropertyId, p.Year, p.Month }).ToListAsync(ct))
            .Select(p => (p.PropertyId, p.Year, p.Month)).ToHashSet();
        var items = (await db.UtilityItems.Where(i => ids.Contains(i.PropertyId))
                .GroupBy(i => i.PropertyId)
                .Select(g => new { PropertyId = g.Key, Count = g.Count(), NonFixed = g.Count(i => !i.IsFixed) })
                .ToListAsync(ct))
            .ToDictionary(i => i.PropertyId);

        foreach (var key in periods)
        {
            entries.TryGetValue(key, out var e);
            items.TryGetValue(key.PropertyId, out var i);
            result[key] = State(e?.Count ?? 0, e?.Pending ?? 0, prepared.Contains(key), i?.Count ?? 0, i?.NonFixed ?? 0);
        }
        return result;
    }
}
