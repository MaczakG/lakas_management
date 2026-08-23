using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class UtilityCostEntryConfiguration : IEntityTypeConfiguration<UtilityCostEntry>
{
    public void Configure(EntityTypeBuilder<UtilityCostEntry> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Label).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Amount).HasColumnType("numeric(12,2)");
        builder.HasOne(u => u.Property).WithMany(p => p.UtilityCostEntries).HasForeignKey(u => u.PropertyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(u => new { u.PropertyId, u.Year, u.Month });
    }
}
