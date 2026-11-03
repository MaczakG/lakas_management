using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Documents;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Dokumentumok az ingatlan, bérlő és tulajdonos adatlapján, és a dokumentum saját adatlapja: feltöltés, új
// verzió, lista, részletek (verziók, napló), letöltés és előnézet, szerkesztés, törlés.
[Authorize]
[ApiController]
[Route("api/documents")]
public class DocumentsController(LakaskezeloDbContext db, DocumentStorage storage, ICompanyContext companyContext) : ControllerBase
{
    // A multipart törzs a fájlon felül a mezőket és a határolókat is tartalmazza.
    private const long MaxRequestBytes = DocumentStorage.MaxBytes + 512 * 1024;
    // Ugyanannak a felhasználónak ugyanarra a verzióra eső megtekintései ennyi időn belül egy naplósornak számítanak.
    private static readonly TimeSpan ViewDedupWindow = TimeSpan.FromMinutes(30);

    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> List([FromQuery] Guid? propertyId, [FromQuery] Guid? tenantId,
        [FromQuery] Guid? ownerId, CancellationToken ct)
    {
        if (new[] { propertyId, tenantId, ownerId }.Count(x => x.HasValue) != 1)
        {
            return BadRequest(new { message = "Pontosan egy szűrő kell: propertyId, tenantId vagy ownerId." });
        }
        var docs = await Query()
            .Where(d => (propertyId != null && d.PropertyId == propertyId)
                || (tenantId != null && d.TenantId == tenantId)
                || (ownerId != null && d.OwnerId == ownerId))
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(ct);
        return Ok(docs.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var doc = await Query().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        var versions = await db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == id).OrderByDescending(v => v.Number)
            .Select(v => new DocumentVersionDto(v.Number, v.FileName, v.ContentType, v.SizeBytes, v.UploadedAt, v.UploadedBy != null ? v.UploadedBy.FullName : null))
            .ToListAsync(ct);
        var events = await db.DocumentEvents.AsNoTracking().Where(e => e.DocumentId == id).OrderByDescending(e => e.At).Take(200)
            .Select(e => new DocumentEventDto(e.Type, e.VersionNumber, e.User != null ? e.User.FullName : null, e.At))
            .ToListAsync(ct);
        return Ok(new DocumentDetailDto(ToDto(doc), versions, events));
    }

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<DocumentDto>> Upload([FromForm] IFormFile? file, [FromForm] string? name, [FromForm] Guid? documentTypeId,
        [FromForm] string? note, [FromForm] Guid? propertyId, [FromForm] Guid? tenantId, [FromForm] Guid? ownerId, CancellationToken ct)
    {
        var (upload, uploadError) = await ReadUploadAsync(file, ct);
        if (upload is null) return BadRequest(new { message = uploadError });

        var displayName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(upload.FileName) : name.Trim();
        if (string.IsNullOrWhiteSpace(name) && displayName.Length > 200) displayName = displayName[..200];
        var error = await ValidateAsync(displayName, documentTypeId, note, ct) ?? await ValidateLinksAsync(propertyId, tenantId, ownerId, ct);
        if (error is not null) return BadRequest(new { message = error });

        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            CompanyId = companyContext.CompanyId ?? throw new InvalidOperationException("Nincs cég a kérésben."),
            Name = displayName,
            DocumentTypeId = documentTypeId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            PropertyId = propertyId,
            TenantId = tenantId,
            OwnerId = ownerId,
            FileName = upload.FileName,
            ContentType = upload.ContentType,
            SizeBytes = upload.Data.LongLength,
            CurrentVersion = 1,
            UploadedAt = now,
            UploadedByUserId = userId,
        };
        var version = NewVersion(doc, 1, upload, userId, now);
        if (!await TryStoreAsync(version, upload.Data, ct)) return StorageFailed();
        db.Documents.Add(doc);
        db.DocumentVersions.Add(version);
        db.DocumentEvents.Add(NewEvent(doc, DocumentEventTypes.Uploaded, 1, userId, now));
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(await Query().FirstAsync(d => d.Id == doc.Id, ct)));
    }

    // Új verzió feltöltése: a dokumentum adatai (név, típus, kapcsolatok) maradnak, a fájl az új lesz; a korábbi
    // verziók megmaradnak, a dokumentum adatlapján visszanézhetők és letölthetők.
    [HttpPost("{id:guid}/versions")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<DocumentDto>> AddVersion(Guid id, [FromForm] IFormFile? file, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        var (upload, uploadError) = await ReadUploadAsync(file, ct);
        if (upload is null) return BadRequest(new { message = uploadError });

        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var number = (await db.DocumentVersions.Where(v => v.DocumentId == id).MaxAsync(v => (int?)v.Number, ct) ?? 0) + 1;
        var version = NewVersion(doc, number, upload, userId, now);
        if (!await TryStoreAsync(version, upload.Data, ct)) return StorageFailed();
        db.DocumentVersions.Add(version);
        doc.CurrentVersion = number;
        doc.FileName = upload.FileName;
        doc.ContentType = upload.ContentType;
        doc.SizeBytes = upload.Data.LongLength;
        db.DocumentEvents.Add(NewEvent(doc, DocumentEventTypes.NewVersion, number, userId, now));
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(await Query().FirstAsync(d => d.Id == id, ct)));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> Update(Guid id, UpdateDocumentRequest request, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        var name = (request.Name ?? "").Trim();
        var error = await ValidateAsync(name, request.DocumentTypeId, request.Note, ct)
            ?? await ValidateLinksAsync(request.PropertyId, request.TenantId, request.OwnerId, ct);
        if (error is not null) return BadRequest(new { message = error });

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var changed = doc.Name != name || doc.DocumentTypeId != request.DocumentTypeId || doc.Note != note
            || doc.PropertyId != request.PropertyId || doc.TenantId != request.TenantId || doc.OwnerId != request.OwnerId;
        doc.Name = name;
        doc.DocumentTypeId = request.DocumentTypeId;
        doc.Note = note;
        doc.PropertyId = request.PropertyId;
        doc.TenantId = request.TenantId;
        doc.OwnerId = request.OwnerId;
        if (changed) db.DocumentEvents.Add(NewEvent(doc, DocumentEventTypes.Updated, null, User.GetUserId(), DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        return Ok(ToDto(await Query().FirstAsync(d => d.Id == id, ct)));
    }

    // A fájl tartalma — letöltéshez (purpose=download) és a felületi előnézethez (purpose=view); a böngésző
    // fetch-csel kéri, a Bearer token miatt. Mindig csatolmányként, sandbox CSP-vel adjuk, hogy közvetlenül
    // megnyitva se futhasson semmi a tartalmából a mi originünkön. A megtekintés és a letöltés a naplóba kerül.
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, [FromQuery] int? version, [FromQuery] string? purpose, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        var number = version ?? doc.CurrentVersion;
        var v = await db.DocumentVersions.FirstOrDefaultAsync(x => x.DocumentId == id && x.Number == number, ct);
        if (v is null) return NotFound(new { message = "Nincs ilyen verzió." });
        var data = await storage.ReadAsync(v, ct);
        if (data is null) return NotFound(new { message = "A dokumentum fájlja nem található a tárhelyen." });

        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        if (purpose == "download")
        {
            db.DocumentEvents.Add(NewEvent(doc, DocumentEventTypes.Downloaded, number, userId, now));
            await db.SaveChangesAsync(ct);
        }
        else if (purpose == "view")
        {
            var since = now - ViewDedupWindow;
            if (!await db.DocumentEvents.AnyAsync(e => e.DocumentId == id && e.Type == DocumentEventTypes.Viewed
                    && e.VersionNumber == number && e.UserId == userId && e.At > since, ct))
            {
                db.DocumentEvents.Add(NewEvent(doc, DocumentEventTypes.Viewed, number, userId, now));
                await db.SaveChangesAsync(ct);
            }
        }

        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(data, v.ContentType, v.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        await storage.DeleteAsync(doc, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- segédek ----------

    private record UploadedFile(string FileName, string ContentType, byte[] Data);

    private static async Task<(UploadedFile? Upload, string? Error)> ReadUploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return (null, "Nincs kiválasztva fájl, vagy üres.");
        if (file.Length > DocumentStorage.MaxBytes) return (null, "A fájl legfeljebb 15 MB lehet.");
        var fileName = CleanFileName(file.FileName);
        if (!DocumentFileTypes.IsAllowedExtension(fileName))
        {
            return (null, $"Ez a fájltípus nem tölthető fel. Engedélyezett: {DocumentFileTypes.AllowedList}.");
        }
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var data = buffer.ToArray();
        var contentType = DocumentFileTypes.Detect(fileName, data);
        return contentType is null
            ? (null, "A fájl tartalma nem egyezik a kiterjesztésével (pl. sérült vagy átnevezett fájl).")
            : (new UploadedFile(fileName, contentType, data), null);
    }

    private static DocumentVersion NewVersion(Document doc, int number, UploadedFile upload, Guid userId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = doc.CompanyId,
        DocumentId = doc.Id,
        Number = number,
        FileName = upload.FileName,
        ContentType = upload.ContentType,
        SizeBytes = upload.Data.LongLength,
        UploadedAt = now,
        UploadedByUserId = userId,
    };

    private static DocumentEvent NewEvent(Document doc, string type, int? version, Guid userId, DateTime now) => new()
    {
        Id = Guid.NewGuid(), CompanyId = doc.CompanyId, DocumentId = doc.Id, Type = type, VersionNumber = version, UserId = userId, At = now,
    };

    private async Task<bool> TryStoreAsync(DocumentVersion version, byte[] data, CancellationToken ct)
    {
        try
        {
            await storage.StoreAsync(version, data, ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ObjectResult StorageFailed() =>
        StatusCode(StatusCodes.Status502BadGateway, new { message = "A fájlt nem sikerült a tárhelyre menteni. Próbáld újra később." });

    private IQueryable<Document> Query() => db.Documents.AsNoTracking()
        .Include(d => d.Property).Include(d => d.Tenant).Include(d => d.Owner).Include(d => d.UploadedBy).Include(d => d.DocumentType);

    private async Task<string?> ValidateAsync(string name, Guid? documentTypeId, string? note, CancellationToken ct)
    {
        if (name.Length is 0 or > 200) return "A név 1–200 karakter lehet.";
        if (documentTypeId is { } t && !await db.DocumentTypes.AnyAsync(x => x.Id == t, ct)) return "A dokumentumtípus nem található.";
        if (note is { Length: > 1000 }) return "A megjegyzés legfeljebb 1000 karakter lehet.";
        return null;
    }

    private async Task<string?> ValidateLinksAsync(Guid? propertyId, Guid? tenantId, Guid? ownerId, CancellationToken ct)
    {
        if (propertyId is null && tenantId is null && ownerId is null)
        {
            return "A dokumentumot legalább egy ingatlanhoz, bérlőhöz vagy tulajdonoshoz kapcsolni kell.";
        }
        if (propertyId is { } p && !await db.Properties.AnyAsync(x => x.Id == p, ct)) return "Az ingatlan nem található.";
        if (tenantId is { } t && !await db.Tenants.AnyAsync(x => x.Id == t, ct)) return "A bérlő nem található.";
        if (ownerId is { } o && !await db.Owners.AnyAsync(x => x.Id == o, ct)) return "A tulajdonos nem található.";
        return null;
    }

    private static string CleanFileName(string raw)
    {
        var name = Path.GetFileName((raw ?? "").Replace('\\', '/')).Trim();
        name = new string(name.Where(c => !char.IsControl(c) && !"/\\:*?\"<>|".Contains(c)).ToArray());
        if (name.Length == 0) name = "dokumentum";
        if (name.Length > 255)
        {
            var ext = Path.GetExtension(name);
            name = name[..(255 - ext.Length)] + ext;
        }
        return name;
    }

    private static DocumentDto ToDto(Document d) => new(
        d.Id, d.Name, d.FileName, d.ContentType, d.SizeBytes, d.DocumentTypeId, d.DocumentType?.Name, d.Note, d.CurrentVersion,
        d.PropertyId, d.Property?.Name, d.TenantId, d.Tenant?.Name, d.OwnerId, d.Owner?.Name,
        d.UploadedAt, d.UploadedBy?.FullName);
}
