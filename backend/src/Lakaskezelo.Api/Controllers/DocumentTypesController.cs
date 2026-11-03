using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Cégenként szerkeszthető dokumentumtípusok. Az alapértelmezett típusok (Szerződés, Rezsi számla…) az első
// lekéréskor jönnek létre; törlés után nem jönnek vissza. Törölt típusú dokumentum „típus nélküli” lesz.
[Authorize]
[ApiController]
[Route("api/document-types")]
public class DocumentTypesController(LakaskezeloDbContext db, ICompanyContext companyContext) : ControllerBase
{
    private const int MaxNameLength = 60;

    [HttpGet]
    public async Task<ActionResult<List<DocumentTypeDto>>> List(CancellationToken ct)
    {
        var companyId = companyContext.CompanyId ?? throw new InvalidOperationException("Nincs cég a kérésben.");
        var company = await db.Companies.SingleAsync(c => c.Id == companyId, ct);
        if (!company.DocumentTypesInitialized)
        {
            if (!await db.DocumentTypes.AnyAsync(ct)) db.DocumentTypes.AddRange(DocumentType.DefaultsFor(companyId, DateTime.UtcNow));
            company.DocumentTypesInitialized = true;
            await db.SaveChangesAsync(ct);
        }
        return Ok(await db.DocumentTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new DocumentTypeDto(t.Id, t.Name, t.SortOrder, db.Documents.Count(d => d.DocumentTypeId == t.Id)))
            .ToListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<DocumentTypeDto>> Create(UpsertDocumentTypeRequest request, CancellationToken ct)
    {
        var name = (request.Name ?? "").Trim();
        if (await ValidateAsync(name, null, ct) is { } error) return BadRequest(new { message = error });
        var maxOrder = await db.DocumentTypes.Select(t => (int?)t.SortOrder).MaxAsync(ct) ?? -1;
        var type = new DocumentType { Id = Guid.NewGuid(), Name = name, SortOrder = maxOrder + 1, CreatedAt = DateTime.UtcNow };
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return Ok(new DocumentTypeDto(type.Id, type.Name, type.SortOrder, 0));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentTypeDto>> Rename(Guid id, UpsertDocumentTypeRequest request, CancellationToken ct)
    {
        var type = await db.DocumentTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound();
        var name = (request.Name ?? "").Trim();
        if (await ValidateAsync(name, id, ct) is { } error) return BadRequest(new { message = error });
        type.Name = name;
        await db.SaveChangesAsync(ct);
        return Ok(new DocumentTypeDto(type.Id, type.Name, type.SortOrder, await db.Documents.CountAsync(d => d.DocumentTypeId == id, ct)));
    }

    // Új sorrend: a típusok azonosítói a kívánt sorrendben.
    [HttpPut("order")]
    public async Task<IActionResult> Reorder(List<Guid> ids, CancellationToken ct)
    {
        var types = await db.DocumentTypes.ToListAsync(ct);
        for (var i = 0; i < ids.Count; i++)
        {
            if (types.FirstOrDefault(t => t.Id == ids[i]) is { } type) type.SortOrder = i;
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var type = await db.DocumentTypes.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (type is null) return NotFound();
        db.DocumentTypes.Remove(type);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<string?> ValidateAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        if (name.Length is 0 or > MaxNameLength) return $"A típus neve 1–{MaxNameLength} karakter lehet.";
        var lower = name.ToLower();
        if (await db.DocumentTypes.AnyAsync(t => t.Id != exceptId && t.Name.ToLower() == lower, ct)) return "Ilyen nevű típus már van.";
        return null;
    }
}
