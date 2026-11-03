using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class TenantPaymentConfiguration : IEntityTypeConfiguration<TenantPayment>
{
    public void Configure(EntityTypeBuilder<TenantPayment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasColumnType("numeric(12,2)");
        builder.Property(p => p.Note).HasMaxLength(500);
        builder.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.TenantId, p.Date });
        builder.HasOne(p => p.Invoice).WithMany().HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.SetNull);
    }
}
