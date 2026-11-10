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
        builder.Property(d => d.Note).HasMaxLength(1000);

        // Az ingatlan, bérlő vagy tulajdonos törlésekor csak a kapcsolat szűnik meg; a dokumentum, ha más
        // adatlaphoz sem kapcsolódik már, a törlő végpontokon külön törlődik (DocumentStorage.DeleteExclusiveAsync).
        builder.HasOne(d => d.Property).WithMany().HasForeignKey(d => d.PropertyId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.Tenant).WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.Owner).WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.SetNull);
        // A rezsitétel törlésekor a csatolmányai a törlő végponton törlődnek; ez csak biztonsági háló.
        builder.HasOne(d => d.UtilityCostEntry).WithMany(u => u.Attachments).HasForeignKey(d => d.UtilityCostEntryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.DocumentType).WithMany().HasForeignKey(d => d.DocumentTypeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(d => d.UploadedBy).WithMany().HasForeignKey(d => d.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(d => d.Versions).WithOne(v => v.Document).HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(d => d.Events).WithOne().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.PropertyId);
        builder.HasIndex(d => d.TenantId);
        builder.HasIndex(d => d.OwnerId);
        builder.HasIndex(d => d.UtilityCostEntryId);
    }
}

public class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.FileName).IsRequired().HasMaxLength(255);
        builder.Property(v => v.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(v => v.StorageKey).HasMaxLength(300);
        builder.HasOne(v => v.UploadedBy).WithMany().HasForeignKey(v => v.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(v => v.Content).WithOne().HasForeignKey<DocumentContent>(c => c.VersionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => new { v.DocumentId, v.Number }).IsUnique();
    }
}

public class DocumentContentConfiguration : IEntityTypeConfiguration<DocumentContent>
{
    public void Configure(EntityTypeBuilder<DocumentContent> builder)
    {
        builder.HasKey(c => c.VersionId);
        builder.Property(c => c.Data).IsRequired();
    }
}

public class DocumentEventConfiguration : IEntityTypeConfiguration<DocumentEvent>
{
    public void Configure(EntityTypeBuilder<DocumentEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).IsRequired().HasMaxLength(30);
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => new { e.DocumentId, e.At });
    }
}

public class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(60);
    }
}
