using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.CompanyId).IsUnique();
        builder.Property(s => s.BarionRecurrenceId).HasMaxLength(100);
        builder.Property(s => s.BarionTraceId).HasMaxLength(200);
        builder.Property(s => s.BillingName).HasMaxLength(200);
        builder.Property(s => s.BillingTaxNumber).HasMaxLength(20);
        builder.Property(s => s.BillingPostCode).HasMaxLength(10);
        builder.Property(s => s.BillingCity).HasMaxLength(100);
        builder.Property(s => s.BillingStreet).HasMaxLength(200);
        builder.Property(s => s.BillingEmail).HasMaxLength(256);
    }
}

public class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.BarionPaymentId).IsUnique();
        builder.HasIndex(p => new { p.Status, p.CreatedAt });
        builder.Property(p => p.UnitPrice).HasPrecision(12, 2);
        builder.Property(p => p.NetAmount).HasPrecision(12, 2);
        builder.Property(p => p.VatAmount).HasPrecision(12, 2);
        builder.Property(p => p.GrossAmount).HasPrecision(12, 2);
        builder.Property(p => p.VatPercent).HasPrecision(5, 2);
        builder.Property(p => p.VatExemption).HasMaxLength(10);
        builder.Property(p => p.BarionPaymentId).HasMaxLength(100);
        builder.Property(p => p.RecurrenceId).HasMaxLength(100);
        builder.Property(p => p.ErrorMessage).HasMaxLength(1000);
        builder.Property(p => p.BillingoPublicUrl).HasMaxLength(1000);
        builder.Property(p => p.BillingoError).HasMaxLength(1000);
    }
}

public class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.MonthlyPricePerProperty).HasPrecision(12, 2);
        builder.Property(s => s.YearlyDiscountPercent).HasPrecision(5, 2);
        builder.Property(s => s.VatPercent).HasPrecision(5, 2);
        builder.Property(s => s.VatExemption).HasMaxLength(10);
        builder.Property(s => s.BarionEnvironment).HasMaxLength(10);
        builder.Property(s => s.BarionPosKey).HasMaxLength(200);
        builder.Property(s => s.BarionPayeeEmail).HasMaxLength(256);
        builder.Property(s => s.BillingoApiKey).HasMaxLength(200);
        builder.HasData(new PlatformSettings
        {
            Id = PlatformSettings.SingletonId,
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
