using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Subscriptions;

// A platform beállítás-sora (díjak, Barion, Billingo) rövid gyorsítótárral — minden író kérés
// zárolás-ellenőrzése olvassa. Mentés után Invalidate().
public class PlatformSettingsService(LakaskezeloDbContext db, IConfiguration configuration)
{
    private static PlatformSettings? _cached;
    private static DateTime _cachedAt;
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public Guid OperatorCompanyId => configuration.GetValue("Platform:OperatorCompanyId", Company.DefaultCompanyId);

    public bool IsOperator(Guid? companyId) => companyId == OperatorCompanyId;

    public async Task<PlatformSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cached is { } c && DateTime.UtcNow - _cachedAt < CacheFor) return c;
        var settings = await db.PlatformSettings.AsNoTracking().SingleOrDefaultAsync(ct)
            ?? new PlatformSettings { Id = PlatformSettings.SingletonId, UpdatedAt = DateTime.UtcNow };
        _cached = settings;
        _cachedAt = DateTime.UtcNow;
        return settings;
    }

    public static void Invalidate() => _cached = null;
}
