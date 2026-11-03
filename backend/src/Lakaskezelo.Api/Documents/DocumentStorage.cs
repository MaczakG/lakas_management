using System.Linq.Expressions;
using Lakaskezelo.Api.Storage;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Documents;

// A dokumentumok tartalmának tárolása: S3-ban, ha a platformon be van állítva tárhely (a számla-PDF-ek
// mellett, companies/{cégId}/documents/ előtag alatt), különben az adatbázisban (DocumentContents).
public class DocumentStorage(LakaskezeloDbContext db, S3InvoiceStorage s3, ILogger<DocumentStorage> logger)
{
    public const long MaxBytes = 15 * 1024 * 1024;

    public static string StorageKey(Document doc) =>
        $"companies/{doc.CompanyId}/documents/{doc.Id:N}{Path.GetExtension(doc.FileName).ToLowerInvariant()}";

    // A dokumentum (Id, CompanyId, FileName kitöltve) tartalmát elhelyezi; a mentést (SaveChanges) a hívó végzi.
    public async Task StoreAsync(Document doc, byte[] data, CancellationToken ct)
    {
        if (await s3.IsConfiguredAsync(ct))
        {
            var key = StorageKey(doc);
            await s3.UploadAsync(key, data, doc.ContentType, ct);
            doc.StorageKey = key;
        }
        else
        {
            doc.Content = new DocumentContent { DocumentId = doc.Id, CompanyId = doc.CompanyId, Data = data };
        }
    }

    public async Task<byte[]?> ReadAsync(Document doc, CancellationToken ct)
    {
        if (doc.StorageKey is { } key) return await s3.DownloadAsync(key, ct);
        return await db.DocumentContents.Where(c => c.DocumentId == doc.Id).Select(c => c.Data).FirstOrDefaultAsync(ct);
    }

    // Törli a dokumentumot (az adatbázisban a tartalmával együtt, S3-ban külön). Az S3-törlés hibája nem
    // akasztja meg a törlést, csak naplózzuk (legfeljebb egy elérhetetlen objektum marad a tárhelyen).
    public async Task DeleteAsync(Document doc, CancellationToken ct)
    {
        if (doc.StorageKey is { } key)
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
