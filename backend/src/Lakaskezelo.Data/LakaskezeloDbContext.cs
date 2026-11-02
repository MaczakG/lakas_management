using System.Linq.Expressions;
using System.Reflection;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Data;

// Több-cégesség (SaaS): minden ICompanyOwned entitásra globális szűrő a CompanyContext cégére, és
// mentéskor a CompanyId automatikus kitöltése/ellenőrzése — így egy elfelejtett Where sem tud másik
// cég adatát kiolvasni vagy felülírni. Ahol szándékosan minden cég adata kell (bejelentkezés,
// háttér-ütemezők), ott IgnoreQueryFilters() van, és a cég-kontextust utána kifejezetten beállítják.
public class LakaskezeloDbContext(DbContextOptions<LakaskezeloDbContext> options, ICompanyContext? companyContext = null)
    : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<TwoFactorChallenge> TwoFactorChallenges => Set<TwoFactorChallenge>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyOwner> PropertyOwners => Set<PropertyOwner>();
    public DbSet<UtilityCostEntry> UtilityCostEntries => Set<UtilityCostEntry>();
    public DbSet<UtilityReminderLog> UtilityReminderLogs => Set<UtilityReminderLog>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();

    // A szűrők ezt olvassák lekérdezésenként (nem a modell építésekor), így minden DbContext-példány a
    // saját kérése cégére szűr.
    public Guid? CurrentCompanyId => companyContext?.CompanyId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LakaskezeloDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ICompanyOwned).IsAssignableFrom(t.ClrType)).ToList())
        {
            var builder = modelBuilder.Entity(entityType.ClrType);
            builder.HasIndex(nameof(ICompanyOwned.CompanyId));
            builder.HasOne(typeof(Company)).WithMany().HasForeignKey(nameof(ICompanyOwned.CompanyId)).OnDelete(DeleteBehavior.Restrict);

            // e => e.CompanyId == this.CurrentCompanyId
            var e = Expression.Parameter(entityType.ClrType, "e");
            var companyId = Expression.Convert(Expression.Property(e, nameof(ICompanyOwned.CompanyId)), typeof(Guid?));
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentCompanyId));
            builder.HasQueryFilter(Expression.Lambda(Expression.Equal(companyId, current), e));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampCompany();
        ValidateReferencesAsync(sync: true, CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampCompany();
        await ValidateReferencesAsync(sync: false, cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Új cégadatnál a CompanyId a kontextusból jön; meglévőnél és kitöltöttnél egyeznie kell vele.
    // Kontextus nélkül (pl. bejelentkezés előtti művelet) csak kifejezetten kitöltött CompanyId menthető.
    private void StampCompany()
    {
        var current = CurrentCompanyId;
        foreach (var entry in ChangeTracker.Entries<ICompanyOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            if (entry.State == EntityState.Added && entry.Entity.CompanyId == Guid.Empty)
            {
                entry.Entity.CompanyId = current
                    ?? throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} nem menthető cég-kontextus nélkül.");
                continue;
            }

            if (current is { } c && entry.Entity.CompanyId != c)
            {
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name}: másik cég adata nem módosítható.");
            }
            if (entry.State == EntityState.Modified && entry.Property(nameof(ICompanyOwned.CompanyId)).IsModified)
            {
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name}: a cég (CompanyId) utólag nem módosítható.");
            }
        }
    }

    // Új vagy átírt hivatkozás (pl. Tenant.PropertyId, PropertyOwner.OwnerId) csak a saját cég adatára
    // mutathat: a szűrő az olvasást védi, de egy kívülről kapott azonosítóval az FK-t másik cég sorára is
    // be lehetne állítani. Ezt itt, minden mentésnél, általánosan zárjuk ki (a kontrollerek ezen felül
    // barátságos 400-at adnak).
    private async Task ValidateReferencesAsync(bool sync, CancellationToken ct)
    {
        var entries = ChangeTracker.Entries<ICompanyOwned>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
        foreach (var entry in entries)
        {
            foreach (var fk in entry.Metadata.GetForeignKeys())
            {
                var principalType = fk.PrincipalEntityType.ClrType;
                if (!typeof(ICompanyOwned).IsAssignableFrom(principalType) || fk.Properties.Count != 1) continue;

                var property = entry.Property(fk.Properties[0].Name);
                if (entry.State == EntityState.Modified && !property.IsModified) continue;
                if (property.CurrentValue is not Guid id || id == Guid.Empty) continue;

                var keyName = fk.PrincipalKey.Properties[0].Name;
                var tracked = entries.FirstOrDefault(p => p.Metadata.ClrType == principalType
                    && p.Property(keyName).CurrentValue is Guid pid && pid == id);
                Guid? principalCompany = tracked?.Entity.CompanyId;
                if (tracked is null)
                {
                    var task = (Task<List<Guid>>)CompanyOfMethod.MakeGenericMethod(principalType)
                        .Invoke(this, [keyName, id, sync, ct])!;
                    var found = await task;
                    principalCompany = found.Count == 0 ? null : found[0];
                }

                // Nem létező sorra mutató FK-t az adatbázis amúgy is elutasítja; itt csak a cégközi hivatkozás a kérdés.
                if (principalCompany is { } pc && pc != entry.Entity.CompanyId)
                {
                    throw new InvalidOperationException(
                        $"{entry.Metadata.ClrType.Name}.{property.Metadata.Name}: másik cég adatára nem hivatkozhat.");
                }
            }
        }
    }

    private static readonly MethodInfo CompanyOfMethod =
        typeof(LakaskezeloDbContext).GetMethod(nameof(CompanyOfAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private Task<List<Guid>> CompanyOfAsync<T>(string keyName, Guid id, bool sync, CancellationToken ct) where T : class
    {
        var query = Set<T>().IgnoreQueryFilters()
            .Where(e => EF.Property<Guid>(e, keyName) == id)
            .Select(e => EF.Property<Guid>(e, nameof(ICompanyOwned.CompanyId)));
        return sync ? Task.FromResult(query.ToList()) : query.ToListAsync(ct);
    }
}
