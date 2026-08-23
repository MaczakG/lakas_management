using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Data;

public class LakaskezeloDbContext(DbContextOptions<LakaskezeloDbContext> options) : DbContext(options)
{
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LakaskezeloDbContext).Assembly);
    }
}
