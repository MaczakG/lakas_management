using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.HasKey(s => s.Id);
        // Egy sor, fix Id-val — a service ezt a sort olvassa/hozza létre, ha még nem létezik.
        // Cégenként legfeljebb egy beállítás-sor.
        builder.HasIndex(s => s.CompanyId).IsUnique();
        builder.HasData(new AppSettings
        {
            Id = AppSettings.LegacySingletonId,
            CompanyId = Company.DefaultCompanyId,
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
