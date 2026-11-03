using System.Linq.Expressions;
using Lakaskezelo.Api.Storage;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Documents;

// A dokumentum-verziók tartalmának tárolása: S3-ban, ha a platformon be van állítva tárhely (a számla-PDF-ek
// mellett, companies/{cégId}/documents/ előtag alatt), különben az adatbázisban (DocumentContents).
public class DocumentStorage(LakaskezeloDbContext db, S3InvoiceStorage s3, ILogger<DocumentStorage> logger)
{
    public const long MaxBytes = 15 * 1024 * 1024;

    public static string StorageKey(DocumentVersion v) =>
        $"companies/{v.CompanyId}/documents/{v.DocumentId:N}/v{v.Number}{Path.GetExtension(v.FileName).ToLowerInvariant()}";

    // A verzió (Id, CompanyId, DocumentId, Number, FileName, ContentType kitöltve) tartalmát elhelyezi; a mentést
    // (SaveChanges) a hívó végzi.
    public async Task StoreAsync(DocumentVersion version, byte[] data, CancellationToken ct)
    {
        if (await s3.IsConfiguredAsync(ct))
        {
            var key = StorageKey(version);
            await s3.UploadAsync(key, data, version.ContentType, ct);
            version.StorageKey = key;
        }
        else
        {
            version.Content = new DocumentContent { VersionId = version.Id, CompanyId = version.CompanyId, Data = data };
        }
    }

    public async Task<byte[]?> ReadAsync(DocumentVersion version, CancellationToken ct)
    {
        if (version.StorageKey is { } key) return await s3.DownloadAsync(key, ct);
        return await db.DocumentContents.Where(c => c.VersionId == version.Id).Select(c => c.Data).FirstOrDefaultAsync(ct);
    }

    // Törli a dokumentumot minden verziójával (az adatbázisban a tartalmakkal és a naplóval együtt, S3-ban
    // külön). Az S3-törlés hibája nem akasztja meg a törlést, csak naplózzuk.
    public async Task DeleteAsync(Document doc, CancellationToken ct)
    {
        var keys = await db.DocumentVersions.Where(v => v.DocumentId == doc.Id && v.StorageKey != null)
            .Select(v => v.StorageKey!).ToListAsync(ct);
        foreach (var key in keys)
        {
            try { await s3.DeleteAsync(key, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete document object {Key}", key); }
        }
        db.Documents.Remove(doc);
    }

    // Ingatlan, bérlő vagy tulajdonos törlése előtt: azok a dokumentumok, amelyek csak hozzá kapcsolódnak,
    // vele együtt törlődnek (a többiről a kapcsolat az adatbázisban szűnik meg, ON DELETE SET NULL).
    public async Task DeleteExclusiveAsync(Expression<Func<Document, bool>> onlyLinkedTo, CancellationToken ct)
    {
        var docs = await db.Documents.Where(onlyLinkedTo).ToListAsync(ct);
        foreach (var doc in docs) await DeleteAsync(doc, ct);
    }
}
