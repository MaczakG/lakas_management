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
        SyncOwners(property, request.OwnerIds);
        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);
        return await Get(property.Id, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PropertyDetailDto>> Update(Guid id, UpsertPropertyRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.PropertyOwners).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();

        Apply(property, request);
        SyncOwners(property, request.OwnerIds);
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
        var lockedPeriods = await LockedPeriodsAsync(id, ct);
        return Ok(entries.Select(e => ToDto(e, lockedPeriods.Contains((e.Year, e.Month)))));
    }

    [HttpPost("{id:guid}/utility-costs")]
    public async Task<ActionResult<UtilityCostEntryDto>> AddUtilityCost(Guid id, UpsertUtilityCostEntryRequest request, CancellationToken ct)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (await IsPeriodLockedAsync(id, request.Year, request.Month, ct))
        {
            return Conflict(new { message = "Ez az időszak már számlázva lett, a rezsi tételek nem módosíthatók." });
        }

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
        return Ok(ToDto(entry, isLocked: false));
    }

    [HttpDelete("{id:guid}/utility-costs/{entryId:guid}")]
    public async Task<IActionResult> DeleteUtilityCost(Guid id, Guid entryId, CancellationToken ct)
    {
        var entry = await db.UtilityCostEntries.FirstOrDefaultAsync(u => u.Id == entryId && u.PropertyId == id, ct);
        if (entry is null) return NotFound();
        if (await IsPeriodLockedAsync(id, entry.Year, entry.Month, ct))
        {
            return Conflict(new { message = "Ez az időszak már számlázva lett, a rezsi tételek nem módosíthatók." });
        }

        db.UtilityCostEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Egy (év, hónap) rezsi-időszak akkor számít lezártnak, ha ahhoz az ingatlanhoz már van
    // sikeresen legenerált vagy kiküldött számla — a Failed státuszú próbálkozás szándékosan nem
    // zár le semmit, hogy a hiányzó/hibás adat javítható és a generálás újrapróbálható legyen.
    private async Task<bool> IsPeriodLockedAsync(Guid propertyId, int year, int month, CancellationToken ct) =>
        await db.Invoices.AnyAsync(i => i.PropertyId == propertyId && i.PeriodYear == year && i.PeriodMonth == month
            && (i.Status == Domain.Enums.InvoiceStatus.Sent || i.Status == Domain.Enums.InvoiceStatus.Generated), ct);

    private async Task<HashSet<(int Year, int Month)>> LockedPeriodsAsync(Guid propertyId, CancellationToken ct) =>
        (await db.Invoices.Where(i => i.PropertyId == propertyId
                && (i.Status == Domain.Enums.InvoiceStatus.Sent || i.Status == Domain.Enums.InvoiceStatus.Generated))
            .Select(i => new { i.PeriodYear, i.PeriodMonth })
            .ToListAsync(ct))
        .Select(p => (p.PeriodYear, p.PeriodMonth)).ToHashSet();

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
        property.InvoicePrefix = string.IsNullOrWhiteSpace(request.InvoicePrefix) ? null : request.InvoicePrefix.Trim().ToUpperInvariant();
        property.BillingDayOfMonth = Math.Clamp(request.BillingDayOfMonth, 1, 28);
        property.BillingHour = Math.Clamp(request.BillingHour, 0, 23);
        property.BillingMinute = Math.Clamp(request.BillingMinute, 0, 59);
        property.IsActive = request.IsActive;
    }

    // Csak a ténylegesen eltávolított/hozzáadott (PropertyId, OwnerId) párokat érinti — nem az
    // egészet törli és hozza létre újra. Utóbbi egy megmaradó tulajdonos esetén ugyanazt a párt
    // törölné és szúrná be egy tranzakción belül, ami az egyedi indexbe (PropertyOwnerConfiguration)
    // ütközik, és a teljes mentést meghiúsítja.
    //
    // Az új sorokat db.PropertyOwners.Add(...)-dal vesszük fel, NEM property.PropertyOwners.Add(...)-
    // dal: utóbbi csak a navigációs listát bővítené, és mivel az Id-t előre, kézzel adjuk meg (nem
    // DB-generált), az EF change tracker snapshot-alapú felismerése ilyenkor tévesen Modified-nek
    // (UPDATE) jelölte az új entitást Added (INSERT) helyett — ez okozta, hogy egy tulajdonos
    // hozzáadása "0 sort érintett" hibával elszállt. A db.PropertyOwners.Add(...) explicit Added
    // állapotba teszi, és az EF a property.PropertyOwners listát is automatikusan frissíti a
    // kapcsolat alapján (kézzel odaadva duplikációt okozna a következő lekérdezésnél).
    private void SyncOwners(Property property, List<Guid> requestedOwnerIds)
    {
        var currentOwnerIds = property.PropertyOwners.Select(po => po.OwnerId).ToHashSet();
        var toRemove = property.PropertyOwners.Where(po => !requestedOwnerIds.Contains(po.OwnerId)).ToList();
        var toAdd = requestedOwnerIds.Where(id => !currentOwnerIds.Contains(id)).ToList();

        foreach (var po in toRemove) property.PropertyOwners.Remove(po);
        foreach (var ownerId in toAdd)
        {
            db.PropertyOwners.Add(new PropertyOwner { Id = Guid.NewGuid(), PropertyId = property.Id, OwnerId = ownerId });
        }
    }

    private static PropertyDetailDto ToDetailDto(Property property) => new(
        property.Id, property.Name, property.RentAmount, property.DriveFolderId, property.InvoicePrefix,
        property.BillingDayOfMonth, property.BillingHour, property.BillingMinute, property.IsActive,
        [.. property.PropertyOwners.Select(po => new PropertyOwnerDto(po.OwnerId, po.Owner!.Name))],
        [.. property.Tenants.Select(t => new TenantDto(t.Id, t.Name, t.Email, t.Phone, t.Address, t.TaxId, t.PropertyId, property.Name, t.MoveInDate, t.MoveOutDate, t.Notes, t.RentCurrency))]);

    private static UtilityCostEntryDto ToDto(UtilityCostEntry entry, bool isLocked) => new(entry.Id, entry.Year, entry.Month, entry.Label, entry.Amount, entry.CreatedAt, isLocked);
}
