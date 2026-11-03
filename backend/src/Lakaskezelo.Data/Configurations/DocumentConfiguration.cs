using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lakaskezelo.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(255);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.Category).IsRequired().HasMaxLength(30);
        builder.Property(d => d.Note).HasMaxLength(1000);
        builder.Property(d => d.StorageKey).HasMaxLength(300);

        // Az ingatlan, bérlő vagy tulajdonos törlésekor csak a kapcsolat szűnik meg; a dokumentum, ha más
        // adatlaphoz sem kapcsolódik már, a törlő végpontokon külön törlődik (DocumentStorage.DeleteOrphansAsync).
        builder.HasOne(d => d.Property).WithMany().HasForeignKey(d => d.PropertyId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.Tenant).WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.Owner).WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.UploadedBy).WithMany().HasForeignKey(d => d.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.Content).WithOne().HasForeignKey<DocumentContent>(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.PropertyId);
        builder.HasIndex(d => d.TenantId);
        builder.HasIndex(d => d.OwnerId);
    }
}

public class DocumentContentConfiguration : IEntityTypeConfiguration<DocumentContent>
{
    public void Configure(EntityTypeBuilder<DocumentContent> builder)
    {
        builder.HasKey(c => c.DocumentId);
        builder.Property(c => c.Data).IsRequired();
    }
}
