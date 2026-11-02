using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class PlatformAdminConfiguration : IEntityTypeConfiguration<PlatformAdmin>
{
    public void Configure(EntityTypeBuilder<PlatformAdmin> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(a => a.Email).IsUnique();
        builder.Property(a => a.FullName).HasMaxLength(200);
        builder.Property(a => a.SecurityStamp).IsRequired().HasMaxLength(64);
    }
}

public class PlatformAdminTokenConfiguration : IEntityTypeConfiguration<PlatformAdminToken>
{
    public void Configure(EntityTypeBuilder<PlatformAdminToken> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Hash).IsRequired().HasMaxLength(128);
        builder.HasIndex(t => t.Hash);
        builder.HasOne(t => t.Admin).WithMany().HasForeignKey(t => t.AdminId).OnDelete(DeleteBehavior.Cascade);
    }
}
