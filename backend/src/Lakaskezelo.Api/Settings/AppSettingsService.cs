using System.Collections.Concurrent;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Settings;

// Az aktuális cég (ICompanyContext) Beállítások oldalon szerkeszthető konfigurációja (e-mail, S3
// tárhely, rezsi kontakt, számla-kibocsátó) — cégenként egy DB-sor.
//
// Cégenkénti in-memory cache: az ütemezők percenként/óránként hívják, nem indokolt minden hívásnál
// adatbázist ütni; a Beállítások mentése után a SettingsController Invalidate()-tel üríti a cég sorát.
public class AppSettingsService(LakaskezeloDbContext db, ICompanyContext companyContext)
{
    private static readonly ConcurrentDictionary<Guid, AppSettings> Cache = new();

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        var companyId = companyContext.CompanyId
            ?? throw new InvalidOperationException("A beállítások cég-kontextus nélkül nem olvashatók.");
        if (Cache.TryGetValue(companyId, out var cached)) return cached;

        var settings = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(ct)
            ?? new AppSettings { Id = Guid.NewGuid(), CompanyId = companyId, UpdatedAt = DateTime.UtcNow };
        Cache[companyId] = settings;
        return settings;
    }

    public static void Invalidate(Guid companyId) => Cache.TryRemove(companyId, out _);
}
