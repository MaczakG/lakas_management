using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Documents;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Dokumentumok az ingatlan, bérlő és tulajdonos adatlapján: feltöltés, lista, letöltés (előnézethez is),
// szerkesztés (név, kategória, megjegyzés, kapcsolatok) és törlés.
[Authorize]
[ApiController]
[Route("api/documents")]
public class DocumentsController(LakaskezeloDbContext db, DocumentStorage storage, ICompanyContext companyContext) : ControllerBase
{
    // A multipart törzs a fájlon felül a mezőket és a határolókat is tartalmazza.
    private const long MaxRequestBytes = DocumentStorage.MaxBytes + 512 * 1024;

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

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<DocumentDto>> Upload([FromForm] IFormFile? file, [FromForm] string? name, [FromForm] string? category,
        [FromForm] string? note, [FromForm] Guid? propertyId, [FromForm] Guid? tenantId, [FromForm] Guid? ownerId, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Nincs kiválasztva fájl, vagy üres." });
        if (file.Length > DocumentStorage.MaxBytes) return BadRequest(new { message = "A fájl legfeljebb 15 MB lehet." });

        var fileName = CleanFileName(file.FileName);
        if (!DocumentFileTypes.IsAllowedExtension(fileName))
        {
            return BadRequest(new { message = $"Ez a fájltípus nem tölthető fel. Engedélyezett: {DocumentFileTypes.AllowedList}." });
        }

        byte[] data;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, ct);
            data = buffer.ToArray();
        }
        var contentType = DocumentFileTypes.Detect(fileName, data);
        if (contentType is null)
        {
            return BadRequest(new { message = "A fájl tartalma nem egyezik a kiterjesztésével (pl. sérült vagy átnevezett fájl)." });
        }

        var displayName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fileName) : name.Trim();
        if (string.IsNullOrWhiteSpace(name) && displayName.Length > 200) displayName = displayName[..200];
        var error = Validate(displayName, category ?? DocumentCategories.Other, note) ?? await ValidateLinksAsync(propertyId, tenantId, ownerId, ct);
        if (error is not null) return BadRequest(new { message = error });

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            CompanyId = companyContext.CompanyId ?? throw new InvalidOperationException("Nincs cég a kérésben."),
            Name = displayName,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = data.LongLength,
            Category = category ?? DocumentCategories.Other,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            PropertyId = propertyId,
            TenantId = tenantId,
            OwnerId = ownerId,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = User.GetUserId(),
        };
        try
        {
            await storage.StoreAsync(doc, data, ct);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "A fájlt nem sikerült a tárhelyre menteni. Próbáld újra később." });
        }
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);

        var saved = await Query().FirstAsync(d => d.Id == doc.Id, ct);
        return Ok(ToDto(saved));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> Update(Guid id, UpdateDocumentRequest request, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        var name = (request.Name ?? "").Trim();
        var error = Validate(name, request.Category, request.Note)
            ?? await ValidateLinksAsync(request.PropertyId, request.TenantId, request.OwnerId, ct);
        if (error is not null) return BadRequest(new { message = error });

        doc.Name = name;
        doc.Category = request.Category;
        doc.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        doc.PropertyId = request.PropertyId;
        doc.TenantId = request.TenantId;
        doc.OwnerId = request.OwnerId;
        await db.SaveChangesAsync(ct);

        var saved = await Query().FirstAsync(d => d.Id == id, ct);
        return Ok(ToDto(saved));
    }

    // A fájl tartalma — letöltéshez és a felületi előnézethez (azt a böngésző fetch-csel kéri, a Bearer
    // token miatt). Mindig csatolmányként, sandbox CSP-vel adjuk, hogy közvetlenül megnyitva se futhasson
    // semmi a tartalmából a mi originünkön.
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        var data = await storage.ReadAsync(doc, ct);
        if (data is null) return NotFound(new { message = "A dokumentum fájlja nem található a tárhelyen." });

        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(data, doc.ContentType, doc.FileName);
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

    private IQueryable<Document> Query() => db.Documents.AsNoTracking()
        .Include(d => d.Property).Include(d => d.Tenant).Include(d => d.Owner).Include(d => d.UploadedBy);

    private static string? Validate(string name, string? category, string? note)
    {
        if (name.Length is 0 or > 200) return "A név 1–200 karakter lehet.";
        if (category is null || !DocumentCategories.All.Contains(category)) return "Ismeretlen kategória.";
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
        d.Id, d.Name, d.FileName, d.ContentType, d.SizeBytes, d.Category, d.Note,
        d.PropertyId, d.Property?.Name, d.TenantId, d.Tenant?.Name, d.OwnerId, d.Owner?.Name,
        d.UploadedAt, d.UploadedBy?.FullName);
}
