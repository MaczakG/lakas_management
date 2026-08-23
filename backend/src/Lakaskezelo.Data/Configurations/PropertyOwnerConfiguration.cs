using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class PropertyOwnerConfiguration : IEntityTypeConfiguration<PropertyOwner>
{
    public void Configure(EntityTypeBuilder<PropertyOwner> builder)
    {
        builder.HasKey(po => po.Id);
        builder.HasOne(po => po.Property).WithMany(p => p.PropertyOwners).HasForeignKey(po => po.PropertyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(po => po.Owner).WithMany(o => o.PropertyOwners).HasForeignKey(po => po.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(po => new { po.PropertyId, po.OwnerId }).IsUnique();
    }
}
