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
        builder.Property(i => i.PaidAmount).HasColumnType("numeric(12,2)");
        builder.Property(i => i.Provider).HasMaxLength(20);
        builder.Property(i => i.ProviderDocumentId).HasMaxLength(100);
        builder.HasOne(i => i.Property).WithMany(p => p.Invoices).HasForeignKey(i => i.PropertyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Tenant).WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(i => i.CancelsInvoice).WithMany().HasForeignKey(i => i.CancelsInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(i => i.IsStorno);
        // Egy ingatlanhoz egy adott (év, hónap) időszakra csak egy AKTÍV (nem sztornózott, nem sztornó) számla lehet —
        // ez a scheduler idempotenciájának a záloga (ld. BillingSchedulerService). Sztornózás után új készülhet.
        builder.HasIndex(i => new { i.PropertyId, i.PeriodYear, i.PeriodMonth }).IsUnique()
            .HasFilter("\"CancelledAt\" IS NULL AND \"CancelsInvoiceId\" IS NULL");
    }
}
