using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class UtilityReminderLogConfiguration : IEntityTypeConfiguration<UtilityReminderLog>
{
    public void Configure(EntityTypeBuilder<UtilityReminderLog> builder)
    {
        builder.HasKey(l => l.Id);
        builder.HasOne(l => l.Property).WithMany().HasForeignKey(l => l.PropertyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => new { l.PropertyId, l.Year, l.Month }).IsUnique();
    }
}
