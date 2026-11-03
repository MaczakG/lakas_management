using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Number).IsRequired().HasMaxLength(50);
        builder.Property(i => i.AmountTotal).HasColumnType("numeric(12,2)");
        builder.HasOne(i => i.Property).WithMany(p => p.Invoices).HasForeignKey(i => i.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Tenant).WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.SetNull);
        // Egy ingatlanhoz egy adott (év, hónap) időszakra csak egy AKTÍV (nem archivált) számla lehet — ez a
        // scheduler idempotenciájának a záloga (ld. BillingSchedulerService). Archiválás után új készülhet.
        builder.HasIndex(i => new { i.PropertyId, i.PeriodYear, i.PeriodMonth }).IsUnique().HasFilter("\"ArchivedAt\" IS NULL");
    }
}
