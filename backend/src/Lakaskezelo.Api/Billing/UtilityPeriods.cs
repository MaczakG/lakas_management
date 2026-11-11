using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// A hónap rezsijének előkészítése az ingatlan rezsi tételeiből (UtilityItem), és a hónap rezsi-állapota.
//
// Előkészítéskor minden rezsi tételből, amelynek a hónapban még nincs tétele, készül egy havi tétel: fix tételnél a
// megadott összeggel, nem fixnél 0 összeggel („kitöltendő”). Az aktuális és a következő év hónapjai előre, maguktól
// elkészülnek (ld. PrepareAheadAsync). Ezen felül fut gombnyomásra (force: a hiányzókat mindig pótolja),
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
        var created = AddEntries(db, propertyId, year, month, items.Where(i => !existing.Contains(i.Id)), now);
        if (period is null)
        {
            db.UtilityPeriods.Add(new UtilityPeriod { Id = Guid.NewGuid(), PropertyId = propertyId, Year = year, Month = month, PreparedAt = now });
        }
        return created;
    }

    private static int AddEntries(LakaskezeloDbContext db, Guid propertyId, int year, int month, IEnumerable<UtilityItem> items, DateTime now)
    {
        var created = 0;
        foreach (var item in items)
        {
            db.UtilityCostEntries.Add(new UtilityCostEntry
            {
                Id = Guid.NewGuid(),
                PropertyId = propertyId,
                Year = year,
                Month = month,
                Label = item.Label,
                Amount = ExpectedAmount(item.IsFixed, item.Amount),
                ForwardBilled = item.ForwardBilled,
                UtilityItemId = item.Id,
                // A tételek a rezsi tételek sorrendjében kövessék egymást.
                CreatedAt = now.AddMilliseconds(created),
            });
            created++;
        }
        return created;
    }

    // Ingatlanonként a jelenlegi bérlő rezsi-csúszása hónapban: 1, ha a bizonylat az előző hónap rezsijét viszi
    // (Tenant.UtilityBillingTiming), különben 0 (az ilyen ingatlan nem szerepel a szótárban).
    public static async Task<Dictionary<Guid, int>> LagByPropertyAsync(LakaskezeloDbContext db, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(BudapestClock.Now);
        var tenants = await db.Tenants
            .Where(t => t.PropertyId != null && (t.MoveOutDate == null || t.MoveOutDate >= today))
            .Select(t => new { PropertyId = t.PropertyId!.Value, t.MoveOutDate, t.MoveInDate, t.UtilityBillingTiming })
            .ToListAsync(ct);
        return tenants.GroupBy(t => t.PropertyId)
            .Select(g => (g.Key, Current: g.OrderByDescending(t => t.MoveOutDate == null).ThenByDescending(t => t.MoveInDate).First()))
            .Where(x => x.Current.UtilityBillingTiming == Domain.Enums.UtilityBillingTiming.PreviousMonth)
            .ToDictionary(x => x.Key, _ => 1);
    }

    // Egy bizonylat-időszak rezsi-hónapja a csúszással (0 vagy 1 hónappal korábbi).
    public static (int Year, int Month) UtilityPeriodOf(DateOnly invoicePeriod, int lag)
    {
        var p = invoicePeriod.AddMonths(-lag);
        return (p.Year, p.Month);
    }

    // A rezsi tételből készülő havi tétel összege: fix tételnél a megadott összeg, nem fixnél 0 („kitöltendő”).
    public static decimal ExpectedAmount(bool isFixed, decimal? amount) => isFixed ? amount ?? 0 : 0;

    // ---------- Előre kitöltés: az aktuális és a következő év (24 hónap) ----------
    //
    // Az ingatlan rezsije az aktuális és a következő év minden hónapjára magától elkészül a rezsi tételekből, hogy a
    // rezsi táblázat mindig ki legyen töltve (évváltáskor a következő év is). Fut a rezsi lekérdezésekor és naponta
    // (UtilityReminderService). Egy hónap akkor készül el, ha (ld. ShouldPrepareAhead):
    //  - még nem volt előkészítve, és nincs benne tétel (a kézzel törölt tétel nem jön vissza);
    //  - nincs érvényes bizonylat arra a hónapra, amelynek a bizonylata ezt a rezsit viszi (alapból ugyanaz a hónap,
    //    egy hónap csúszásnál a következő) — különben a fix tétel utólagos tételként még egyszer számlázódna;
    //  - a hónapban van bérlője az ingatlannak;
    //  - nem korábbi az ingatlan első bizonylatának hónapjánál — ha még nincs bizonylata, a következő bizonylat
    //    hónapjánál. Így a program bevezetése előtti, soha ki nem számlázott hónapok nem töltődnek fel maguktól
    //    (azok tételei a következő bizonylatra kerülnének). Ilyen hónap a rezsi ablak megnyitásával tölthető fel.
    public static async Task<int> PrepareAheadAsync(LakaskezeloDbContext db, Property property, DateTime nowLocal, CancellationToken ct)
    {
        var items = await db.UtilityItems.Where(i => i.PropertyId == property.Id).OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt).ToListAsync(ct);
        if (items.Count == 0) return 0;

        var fromKey = nowLocal.Year * 12 + 1;
        var toKey = (nowLocal.Year + 1) * 12 + 12;
        var prepared = (await db.UtilityPeriods.Where(p => p.PropertyId == property.Id && p.Year * 12 + p.Month >= fromKey && p.Year * 12 + p.Month <= toKey)
            .Select(p => p.Year * 12 + p.Month).ToListAsync(ct)).ToHashSet();
        if (prepared.Count == toKey - fromKey + 1) return 0;

        var withEntries = (await db.UtilityCostEntries.Where(u => u.PropertyId == property.Id && u.Year * 12 + u.Month >= fromKey && u.Year * 12 + u.Month <= toKey)
            .Select(u => u.Year * 12 + u.Month).Distinct().ToListAsync(ct)).ToHashSet();
        var invoiced = (await db.Invoices
            .Where(i => i.PropertyId == property.Id && i.Status != Domain.Enums.InvoiceStatus.Failed && i.CancelledAt == null && i.CancelsInvoiceId == null)
            .Select(i => i.PeriodYear * 12 + i.PeriodMonth).Distinct().ToListAsync(ct)).ToHashSet();
        var tenants = await db.Tenants.Where(t => t.PropertyId == property.Id).Select(t => new { t.MoveInDate, t.MoveOutDate }).ToListAsync(ct);
        var next = BillingCalendar.NextBilling(nowLocal, property.BillingDayOfMonth, property.BillingHour, property.BillingMinute).Period;
        var floor = invoiced.Count > 0 ? invoiced.Min() : next.Year * 12 + next.Month;
        var (windowYear, windowMonth) = InvoiceGenerationService.BillingWindowStart(nowLocal.Year, nowLocal.Month);
        var lag = (await LagByPropertyAsync(db, ct)).GetValueOrDefault(property.Id);

        var now = DateTime.UtcNow;
        var created = 0;
        for (var key = fromKey; key <= toKey; key++)
        {
            var (year, month) = ((key - 1) / 12, (key - 1) % 12 + 1);
            var first = new DateOnly(year, month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            var tenantInMonth = tenants.Any(t => (t.MoveInDate == null || t.MoveInDate <= last) && (t.MoveOutDate == null || t.MoveOutDate >= first));
            if (!ShouldPrepareAhead(prepared.Contains(key), withEntries.Contains(key), invoiced.Contains(key + lag), tenantInMonth, key, floor, windowYear * 12 + windowMonth)) continue;
            created += AddEntries(db, property.Id, year, month, items, now);
            db.UtilityPeriods.Add(new UtilityPeriod { Id = Guid.NewGuid(), PropertyId = property.Id, Year = year, Month = month, PreparedAt = now });
        }
        return created;
    }

    // Az előre kitöltés mentéssel együtt. Egyszerre csak egy fut (a párhuzamos lekérdezések ugyanazokat a hónapokat
    // készítenék el, és az egyedi index miatt ütköznének); ha a mentés mégis ütközik (pl. másik példány közben
    // elkészítette), a változás eldobható: a hónapok már megvannak.
    private static readonly SemaphoreSlim AheadLock = new(1, 1);

    public static async Task PrepareAheadAndSaveAsync(LakaskezeloDbContext db, Property property, DateTime nowLocal, CancellationToken ct)
    {
        await AheadLock.WaitAsync(ct);
        try
        {
            if (await PrepareAheadAsync(db, property, nowLocal, ct) > 0) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException || ex.InnerException is DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }
        finally
        {
            AheadLock.Release();
        }
    }

    // Az időszakok év × 12 + hónap alakban jönnek; floor: az első hónap, amelytől az előre kitöltés indulhat.
    public static bool ShouldPrepareAhead(bool prepared, bool hasEntries, bool hasActiveInvoice, bool tenantInMonth, int period, int floor, int windowStart) =>
        !prepared && !hasEntries && !hasActiveInvoice && tenantInMonth && period >= floor && period >= windowStart;

    // ---------- A rezsi tétel változásának átvezetése az előre kitöltött hónapokra ----------
    //
    // Az előre kitöltött hónapok tételei a rezsi tételt követik, amíg senki nem nyúlt hozzájuk: a következő bizonylat
    // hónapjától kezdve az „érintetlen” havi tétel (még nincs bizonylaton, nincs megjegyzése és csatolmánya, a
    // megnevezése, összege és a „tovább számlázandó” jelölése a rezsi tétel korábbi állapotával egyezik) a rezsi
    // tétel módosításakor frissül, törlésekor törlődik. A kézzel átírt vagy kitöltött tétel változatlan marad.
    public static bool IsUntouched(UtilityCostEntry entry, bool hasAttachments, string oldLabel, decimal oldAmount, bool oldForwardBilled) =>
        entry.InvoiceId is null && !entry.ExcludedFromBilling && string.IsNullOrWhiteSpace(entry.Note) && !hasAttachments
        && entry.Label == oldLabel && entry.Amount == oldAmount && entry.ForwardBilled == oldForwardBilled;

    // A következő bizonylat rezsi-hónapja (év × 12 + hónap): innentől követik az előre kitöltött tételek a rezsi tételt.
    private static async Task<int> NextUtilityKeyAsync(LakaskezeloDbContext db, Property property, DateTime nowLocal, CancellationToken ct)
    {
        var next = BillingCalendar.NextBilling(nowLocal, property.BillingDayOfMonth, property.BillingHour, property.BillingMinute).Period;
        return next.Year * 12 + next.Month - (await LagByPropertyAsync(db, ct)).GetValueOrDefault(property.Id);
    }

    private static async Task<List<UtilityCostEntry>> UntouchedAsync(LakaskezeloDbContext db, Property property, Guid itemId,
        string oldLabel, decimal oldAmount, bool oldForwardBilled, DateTime nowLocal, CancellationToken ct)
    {
        var fromKey = await NextUtilityKeyAsync(db, property, nowLocal, ct);
        var entries = await db.UtilityCostEntries.Include(u => u.Attachments)
            .Where(u => u.PropertyId == property.Id && u.UtilityItemId == itemId && u.InvoiceId == null && u.Year * 12 + u.Month >= fromKey)
            .ToListAsync(ct);
        return [.. entries.Where(u => IsUntouched(u, u.Attachments.Count > 0, oldLabel, oldAmount, oldForwardBilled))];
    }

    // Módosított rezsi tétel: az érintetlen, előre kitöltött havi tételek az új állapotot kapják. A mentést a hívó végzi.
    public static async Task<int> ApplyItemChangeAsync(LakaskezeloDbContext db, Property property, UtilityItem item,
        string oldLabel, decimal oldAmount, bool oldForwardBilled, DateTime nowLocal, CancellationToken ct)
    {
        var entries = await UntouchedAsync(db, property, item.Id, oldLabel, oldAmount, oldForwardBilled, nowLocal, ct);
        foreach (var u in entries)
        {
            u.Label = item.Label;
            u.Amount = ExpectedAmount(item.IsFixed, item.Amount);
            u.ForwardBilled = item.ForwardBilled;
        }
        return entries.Count;
    }

    // Törölt rezsi tétel: az érintetlen, előre kitöltött havi tételei is törlődnek. A mentést a hívó végzi.
    public static async Task<int> RemoveItemEntriesAsync(LakaskezeloDbContext db, Property property, UtilityItem item, DateTime nowLocal, CancellationToken ct)
    {
        var entries = await UntouchedAsync(db, property, item.Id, item.Label, ExpectedAmount(item.IsFixed, item.Amount), item.ForwardBilled, nowLocal, ct);
        db.UtilityCostEntries.RemoveRange(entries);
        return entries.Count;
    }

    // Új rezsi tétel: a már előre kitöltött hónapokba is bekerül (a következő bizonylat hónapjától, ahol még nincs
    // érvényes bizonylat). A még elő nem készített hónapokba az előre kitöltés teszi be. A mentést a hívó végzi.
    public static async Task<int> AddItemEntriesAsync(LakaskezeloDbContext db, Property property, UtilityItem item, DateTime nowLocal, CancellationToken ct)
    {
        var fromKey = await NextUtilityKeyAsync(db, property, nowLocal, ct);
        var prepared = await db.UtilityPeriods.Where(p => p.PropertyId == property.Id && p.Year * 12 + p.Month >= fromKey)
            .Select(p => new { p.Year, p.Month }).ToListAsync(ct);
        if (prepared.Count == 0) return 0;
        var invoiced = (await db.Invoices
            .Where(i => i.PropertyId == property.Id && i.Status != Domain.Enums.InvoiceStatus.Failed && i.CancelledAt == null && i.CancelsInvoiceId == null
                && i.PeriodYear * 12 + i.PeriodMonth >= fromKey)
            .Select(i => i.PeriodYear * 12 + i.PeriodMonth).ToListAsync(ct)).ToHashSet();
        var now = DateTime.UtcNow;
        var created = 0;
        foreach (var p in prepared.Where(p => !invoiced.Contains(p.Year * 12 + p.Month)))
        {
            // Az új tétel a hónap meglévő tételei után álljon.
            created += AddEntries(db, property.Id, p.Year, p.Month, [item], now);
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
