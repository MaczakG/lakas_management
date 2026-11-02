using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Accounts;

// A 7 napnál régebbi, soha meg nem erősített regisztrációkat (cég + egyetlen, meg nem erősített
// adminisztrátora) törli — ezekhez se belépés, se adat nem tartozhat. Hatóránként fut.
public class RegistrationCleanupService(IServiceScopeFactory scopeFactory, ILogger<RegistrationCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Registration cleanup failed");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> companyIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
            var cutoff = DateTime.UtcNow.Subtract(MaxAge);
            companyIds = await db.Companies
                .Where(c => c.Id != Company.DefaultCompanyId && c.CreatedAt < cutoff)
                .Where(c => db.Users.IgnoreQueryFilters().Count(u => u.CompanyId == c.Id) == 1
                    && db.Users.IgnoreQueryFilters().Any(u => u.CompanyId == c.Id && u.EmailVerifiedAt == null && u.Role == UserRole.Admin))
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        foreach (var companyId in companyIds)
        {
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
            var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
            // Újraellenőrzés a törlés előtt: közben megerősíthette a címét.
            var users = await db.Users.ToListAsync(ct);
            if (users is not [{ EmailVerifiedAt: null }]) continue;
            db.Users.RemoveRange(users);
            await db.SaveChangesAsync(ct);
            db.Companies.Remove(await db.Companies.SingleAsync(c => c.Id == companyId, ct));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Removed abandoned registration {CompanyId}", companyId);
        }
    }
}
