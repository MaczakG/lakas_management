using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class UtilityItemConfiguration : IEntityTypeConfiguration<UtilityItem>
{
    public void Configure(EntityTypeBuilder<UtilityItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Label).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Amount).HasColumnType("numeric(12,2)");
        builder.HasOne(i => i.Property).WithMany().HasForeignKey(i => i.PropertyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => i.PropertyId);
    }
}

public class UtilityPeriodConfiguration : IEntityTypeConfiguration<UtilityPeriod>
{
    public void Configure(EntityTypeBuilder<UtilityPeriod> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasOne(p => p.Property).WithMany().HasForeignKey(p => p.PropertyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.PropertyId, p.Year, p.Month }).IsUnique();
    }
}
