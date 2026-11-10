using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(300);
        builder.Property(p => p.RentAmount).HasColumnType("numeric(12,2)");
        builder.Property(p => p.PropertyType).HasMaxLength(50);
        builder.Property(p => p.PostalCode).HasMaxLength(10);
        builder.Property(p => p.City).HasMaxLength(100);
        builder.Property(p => p.Street).HasMaxLength(200);
        builder.Property(p => p.FloorDoor).HasMaxLength(50);
        builder.Property(p => p.Hrsz).HasMaxLength(50);
        builder.Property(p => p.FloorArea).HasColumnType("numeric(9,2)");
        builder.Property(p => p.Rooms).HasColumnType("numeric(4,1)");
        builder.Property(p => p.ElectricityMeter).HasMaxLength(50);
        builder.Property(p => p.GasMeter).HasMaxLength(50);
        builder.Property(p => p.WaterMeter).HasMaxLength(50);
        builder.Property(p => p.Deposit).HasColumnType("numeric(12,2)");
        builder.Property(p => p.Notes).HasMaxLength(4000);
        builder.Property(p => p.MoveInInfo).HasMaxLength(5000);
        builder.Property(p => p.MoveInInfoSendable).HasDefaultValue(true);
    }
}
