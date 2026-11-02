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
public class AppSettingsService(LakaskezeloDbContext db, ICompanyContext companyContext, IConfiguration configuration)
{
    private static readonly ConcurrentDictionary<Guid, AppSettings> Cache = new();

    public Task<AppSettings> GetAsync(CancellationToken ct = default) =>
        GetForCompanyAsync(companyContext.CompanyId
            ?? throw new InvalidOperationException("A beállítások cég-kontextus nélkül nem olvashatók."), ct);

    // Kifejezetten megadott cég beállításai, a kérés cég-kontextusától függetlenül — a rendszerlevelek
    // (SystemEmailSender) a platform üzemeltetőjének e-mail beállításaival mennek ki.
    public async Task<AppSettings> GetForCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(companyId, out var cached)) return cached;

        var settings = await db.AppSettings.IgnoreQueryFilters().AsNoTracking()
                           .SingleOrDefaultAsync(s => s.CompanyId == companyId, ct)
            ?? new AppSettings { Id = Guid.NewGuid(), CompanyId = companyId, UpdatedAt = DateTime.UtcNow };
        Cache[companyId] = settings;
        return settings;
    }

    // A platform kézbesítési beállításai (e-mail küldés: Mailgun/SMTP, számla-tárhely: S3) — mindig az
    // üzemeltető cég (Platform:EmailCompanyId, alapból az első cég) sorából, a Platform admin felületen
    // szerkeszthetők. A cégek a saját soruk e mezőit nem használják.
    public Guid PlatformCompanyId => configuration.GetValue("Platform:EmailCompanyId", Company.DefaultCompanyId);

    public Task<AppSettings> GetPlatformAsync(CancellationToken ct = default) => GetForCompanyAsync(PlatformCompanyId, ct);

    public static void Invalidate(Guid companyId) => Cache.TryRemove(companyId, out _);
}
