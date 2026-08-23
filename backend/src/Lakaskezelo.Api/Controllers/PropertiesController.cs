using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.GoogleDrive;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/properties")]
public class PropertiesController(LakaskezeloDbContext db, GoogleDriveService driveService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PropertyListItemDto>>> List(CancellationToken ct)
    {
        var now = Billing.BudapestClock.Now;

        var properties = await db.Properties
            .Include(p => p.PropertyOwners).ThenInclude(po => po.Owner)
            .Include(p => p.Tenants)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var propertiesWithEntries = await db.UtilityCostEntries
            .Where(u => u.Year == now.Year && u.Month == now.Month)
            .Select(u => u.PropertyId)
            .Distinct()
            .ToListAsync(ct);

        return Ok(properties.Select(p => new PropertyListItemDto(
            p.Id, p.Name, p.RentAmount,
            p.Tenants.FirstOrDefault(t => t.MoveOutDate == null)?.RentCurrency ?? Domain.Enums.RentCurrency.HUF,
            p.IsActive,
            string.Join(", ", p.PropertyOwners.Select(po => po.Owner!.Name)),
            string.Join(", ", p.Tenants.Where(t => t.MoveOutDate == null).Select(t => t.Name)),
            propertiesWithEntries.Contains(p.Id),
            p.BillingDayOfMonth, p.BillingHour, p.BillingMinute)));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PropertyDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var property = await db.Properties
            .Include(p => p.PropertyOwners).ThenInclude(po => po.Owner)
            .Include(p => p.Tenants)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        return property is null ? NotFound() : Ok(ToDetailDto(property));
    }

    [HttpPost]
    public async Task<ActionResult<PropertyDetailDto>> Create(UpsertPropertyRequest request, CancellationToken ct)
    {
        var property = new Property { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        Apply(property, request);
        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);
        return await Get(property.Id, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PropertyDetailDto>> Update(Guid id, UpsertPropertyRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.PropertyOwners).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();

        db.PropertyOwners.RemoveRange(property.PropertyOwners);
        Apply(property, request);
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var property = await db.Properties.FindAsync([id], ct);
        if (property is null) return NotFound();
        db.Properties.Remove(property);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- Rezsi tételek ----------

    [HttpGet("{id:guid}/utility-costs")]
    public async Task<ActionResult<List<UtilityCostEntryDto>>> ListUtilityCosts(Guid id, CancellationToken ct)
    {
        var entries = await db.UtilityCostEntries.Where(u => u.PropertyId == id)
            .OrderByDescending(u => u.Year).ThenByDescending(u => u.Month).ThenBy(u => u.Label)
            .ToListAsync(ct);
        return Ok(entries.Select(ToDto));
    }

    [HttpPost("{id:guid}/utility-costs")]
    public async Task<ActionResult<UtilityCostEntryDto>> AddUtilityCost(Guid id, UpsertUtilityCostEntryRequest request, CancellationToken ct)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == id, ct)) return NotFound();

        var entry = new UtilityCostEntry
        {
            Id = Guid.NewGuid(),
            PropertyId = id,
            Year = request.Year,
            Month = request.Month,
            Label = request.Label.Trim(),
            Amount = request.Amount,
            CreatedByUserId = User.GetUserId(),
            CreatedAt = DateTime.UtcNow,
        };
        db.UtilityCostEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(entry));
    }

    [HttpDelete("{id:guid}/utility-costs/{entryId:guid}")]
    public async Task<IActionResult> DeleteUtilityCost(Guid id, Guid entryId, CancellationToken ct)
    {
        var entry = await db.UtilityCostEntries.FirstOrDefaultAsync(u => u.Id == entryId && u.PropertyId == id, ct);
        if (entry is null) return NotFound();
        db.UtilityCostEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- Dokumentumok (élő Drive-listázás) ----------

    [HttpGet("{id:guid}/documents")]
    public async Task<ActionResult<List<DriveDocumentDto>>> ListDocuments(Guid id, CancellationToken ct)
    {
        var property = await db.Properties.FindAsync([id], ct);
        if (property is null) return NotFound();
        if (string.IsNullOrWhiteSpace(property.DriveFolderId)) return Ok(new List<DriveDocumentDto>());

        var files = await driveService.ListFilesAsync(property.DriveFolderId, ct);
        return Ok(files.Select(f => new DriveDocumentDto(f.Id, f.Name, f.WebViewLink, f.CreatedAt, f.SizeBytes)));
    }

    // ---------- Számlák ----------

    [HttpGet("{id:guid}/invoices")]
    public async Task<ActionResult<List<InvoiceDto>>> ListInvoices(Guid id, CancellationToken ct)
    {
        var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .Where(i => i.PropertyId == id)
            .OrderByDescending(i => i.PeriodYear).ThenByDescending(i => i.PeriodMonth)
            .ToListAsync(ct);
        return Ok(invoices.Select(InvoicesController.ToDto));
    }

    private static void Apply(Property property, UpsertPropertyRequest request)
    {
        property.Name = request.Name.Trim();
        property.RentAmount = request.RentAmount;
        property.DriveFolderId = string.IsNullOrWhiteSpace(request.DriveFolderId) ? null : request.DriveFolderId.Trim();
        property.BillingDayOfMonth = Math.Clamp(request.BillingDayOfMonth, 1, 28);
        property.BillingHour = Math.Clamp(request.BillingHour, 0, 23);
        property.BillingMinute = Math.Clamp(request.BillingMinute, 0, 59);
        property.IsActive = request.IsActive;
        property.PropertyOwners = [.. request.OwnerIds.Select(ownerId => new PropertyOwner
        {
            Id = Guid.NewGuid(),
            PropertyId = property.Id,
            OwnerId = ownerId,
        })];
    }

    private static PropertyDetailDto ToDetailDto(Property property) => new(
        property.Id, property.Name, property.RentAmount, property.DriveFolderId,
        property.BillingDayOfMonth, property.BillingHour, property.BillingMinute, property.IsActive,
        [.. property.PropertyOwners.Select(po => new PropertyOwnerDto(po.OwnerId, po.Owner!.Name))],
        [.. property.Tenants.Select(t => new TenantDto(t.Id, t.Name, t.Email, t.Phone, t.PropertyId, property.Name, t.MoveInDate, t.MoveOutDate, t.Notes, t.RentCurrency))]);

    private static UtilityCostEntryDto ToDto(UtilityCostEntry entry) => new(entry.Id, entry.Year, entry.Month, entry.Label, entry.Amount, entry.CreatedAt);
}
