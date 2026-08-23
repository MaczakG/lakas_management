using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Settings;

// A Beállítások oldal (Mailgun, Google Drive, rezsi kontakt, számla-kibocsátó) egyetlen DB-sorban
// tárolt, futásidőben szerkeszthető konfigurációja — az IOptions<T> minta (Flotta) helyett, mert
// ezeket az értékeket a felhasználó a felületen módosítja, nem appsettings/env-ben.
//
// Egyszerű, rövid életű (pár másodperces) in-memory cache: a scheduler-ek percenként/óránként
// hívják, nem indokolt minden hívásnál adatbázist ütni, de a Beállítások mentése után a következő
// GET-nek már a friss értéket kell látnia — ezért Invalidate()-et hív a SettingsController mentés
// után.
public class AppSettingsService(LakaskezeloDbContext db)
{
    private static AppSettings? _cached;
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        await Lock.WaitAsync(ct);
        try
        {
            if (_cached is not null) return _cached;

            var settings = await db.AppSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == AppSettings.SingletonId, ct);
            settings ??= new AppSettings { Id = AppSettings.SingletonId, UpdatedAt = DateTime.UtcNow };
            _cached = settings;
            return settings;
        }
        finally
        {
            Lock.Release();
        }
    }

    public static void Invalidate() => _cached = null;
}
