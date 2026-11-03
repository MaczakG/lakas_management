using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/properties")]
public class PropertiesController(LakaskezeloDbContext db, Lakaskezelo.Api.Documents.DocumentStorage documents) : ControllerBase
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

        // A rezsi-jelzés ingatlanonként a következő kiküldés időszakát nézi (nem az aktuális hónapot): pl. ha a
        // következő számla dec. 5-én megy ki, akkor a decemberi rezsinek kell meglennie — akár már november végén.
        var next = properties.ToDictionary(p => p.Id,
            p => BillingCalendar.NextBilling(now, p.BillingDayOfMonth, p.BillingHour, p.BillingMinute));
        var periods = next.Values.Select(n => n.Period.Year * 100 + n.Period.Month).Distinct().ToList();
        var periodsWithEntries = (await db.UtilityCostEntries
                .Where(u => periods.Contains(u.Year * 100 + u.Month))
                .Select(u => new { u.PropertyId, u.Year, u.Month })
                .Distinct()
                .ToListAsync(ct))
            .Select(u => (u.PropertyId, u.Year, u.Month))
            .ToHashSet();

        return Ok(properties.Select(p => new PropertyListItemDto(
            p.Id, p.Name, p.RentAmount,
            p.Tenants.FirstOrDefault(t => t.MoveOutDate == null)?.RentCurrency ?? Domain.Enums.RentCurrency.HUF,
            p.IsActive,
            string.Join(", ", p.PropertyOwners.Select(po => po.Owner!.Name)),
            string.Join(", ", p.Tenants.Where(t => t.MoveOutDate == null).Select(t => t.Name)),
            periodsWithEntries.Contains((p.Id, next[p.Id].Period.Year, next[p.Id].Period.Month)),
            p.BillingDayOfMonth, p.BillingHour, p.BillingMinute,
            next[p.Id].At, next[p.Id].Period.Year, next[p.Id].Period.Month)));
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
        if (ValidateShares(request.Owners) is { } shareError) return BadRequest(new { message = shareError });
        if (ValidateSheet(request.Sheet) is { } sheetError) return BadRequest(new { message = sheetError });
        if (!await OwnersExistAsync(request.Owners, ct)) return BadRequest(new { message = "A megadott tulajdonos nem található." });

        var property = new Property { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        Apply(property, request);
        SyncOwners(property, request.Owners);
        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);
        return await Get(property.Id, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PropertyDetailDto>> Update(Guid id, UpsertPropertyRequest request, CancellationToken ct)
    {
        if (ValidateShares(request.Owners) is { } shareError) return BadRequest(new { message = shareError });
        if (ValidateSheet(request.Sheet) is { } sheetError) return BadRequest(new { message = sheetError });
        if (!await OwnersExistAsync(request.Owners, ct)) return BadRequest(new { message = "A megadott tulajdonos nem található." });

        var property = await db.Properties.Include(p => p.PropertyOwners).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();

        Apply(property, request);
        SyncOwners(property, request.Owners);
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var property = await db.Properties.FindAsync([id], ct);
        if (property is null) return NotFound();
        // A csak ehhez kapcsolódó dokumentumok vele együtt törlődnek (a tárhelyről is).
        await documents.DeleteExclusiveAsync(d => d.PropertyId == id && d.TenantId == null && d.OwnerId == null, ct);
        db.Properties.Remove(property);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------- Rezsi tételek ----------

    [HttpGet("{id:guid}/utility-costs")]
    public async Task<ActionResult<List<UtilityCostEntryDto>>> ListUtilityCosts(Guid id, CancellationToken ct)
    {
        var entries = await db.UtilityCostEntries.Where(u => u.PropertyId == id)
            .Include(u => u.Invoice)
            .OrderByDescending(u => u.Year).ThenByDescending(u => u.Month).ThenBy(u => u.Label)
            .ToListAsync(ct);
        return Ok(entries.Select(ToDto));
    }

    [HttpPost("{id:guid}/utility-costs")]
    public async Task<ActionResult<UtilityCostEntryDto>> AddUtilityCost(Guid id, UpsertUtilityCostEntryRequest request, CancellationToken ct)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (request.Month is < 1 or > 12) return BadRequest(new { message = "Érvénytelen hónap." });
        // Utólag legfeljebb 36 hónapra visszamenőleg lehet tételt felvenni (az aktuális hónappal együtt): a már
        // bizonylatolt hónapra felvett tétel nem zárolt, és a következő bizonylatra kerül, a hónapjával jelölve.
        var now = Billing.BudapestClock.Now;
        var (fromYear, fromMonth) = Billing.InvoiceGenerationService.BillingWindowStart(now.Year, now.Month);
        if (request.Year * 12 + request.Month < fromYear * 12 + fromMonth)
        {
            return BadRequest(new { message = $"Legfeljebb {Billing.InvoiceGenerationService.LateUtilityMonths} hónapra visszamenőleg lehet rezsitételt felvenni ({fromYear}. {fromMonth:D2}. hónaptól)." });
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
        return Ok(ToDto(entry));
    }

    private static string LockedMessage(UtilityCostEntry entry) => entry.Invoice is { } invoice
        ? $"Ez a tétel már a(z) {invoice.Number} bizonylaton szerepel, ezért nem módosítható. Ha javítani kell, sztornózd a bizonylatot, utána a tétel szerkeszthető."
        : "Ez a tétel egy korábban lezárt időszakhoz tartozik, ezért nem módosítható.";

    // Meglévő tétel javítása (megnevezés, összeg) — csak ha még nincs aktív bizonylaton.
    [HttpPut("{id:guid}/utility-costs/{entryId:guid}")]
    public async Task<ActionResult<UtilityCostEntryDto>> UpdateUtilityCost(Guid id, Guid entryId, UpdateUtilityCostEntryRequest request, CancellationToken ct)
    {
        var entry = await db.UtilityCostEntries.Include(u => u.Invoice).FirstOrDefaultAsync(u => u.Id == entryId && u.PropertyId == id, ct);
        if (entry is null) return NotFound();
        if (IsLocked(entry)) return Conflict(new { message = LockedMessage(entry) });
        var label = (request.Label ?? "").Trim();
        if (label.Length is 0 or > 200) return BadRequest(new { message = "Add meg a tétel megnevezését." });
        if (request.Amount < 0) return BadRequest(new { message = "Az összeg nem lehet negatív." });
        entry.Label = label;
        entry.Amount = request.Amount;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(entry));
    }

    [HttpDelete("{id:guid}/utility-costs/{entryId:guid}")]
    public async Task<IActionResult> DeleteUtilityCost(Guid id, Guid entryId, CancellationToken ct)
    {
        var entry = await db.UtilityCostEntries.Include(u => u.Invoice).FirstOrDefaultAsync(u => u.Id == entryId && u.PropertyId == id, ct);
        if (entry is null) return NotFound();
        if (IsLocked(entry)) return Conflict(new { message = LockedMessage(entry) });

        db.UtilityCostEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Egy rezsitétel akkor zárolt, ha egy aktív (nem sztornózott) bizonylaton szerepel — sztornózáskor a kapcsolat
    // megszűnik. A bevezetés előtti, bizonylat nélkül lezárt korábbi időszakok tételei (ExcludedFromBilling) is zároltak.
    private static bool IsLocked(UtilityCostEntry entry) => entry.InvoiceId is not null || entry.ExcludedFromBilling;

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
        property.InvoicePrefix = string.IsNullOrWhiteSpace(request.InvoicePrefix) ? null : request.InvoicePrefix.Trim().ToUpperInvariant();
        property.BillingDayOfMonth = Math.Clamp(request.BillingDayOfMonth, 1, 28);
        property.BillingHour = Math.Clamp(request.BillingHour, 0, 23);
        property.BillingMinute = Math.Clamp(request.BillingMinute, 0, 59);
        property.IsActive = request.IsActive;

        if (request.Sheet is { } s)
        {
            property.PropertyType = Clean(s.PropertyType);
            property.PostalCode = Clean(s.PostalCode);
            property.City = Clean(s.City);
            property.Street = Clean(s.Street);
            property.FloorDoor = Clean(s.FloorDoor);
            property.Hrsz = Clean(s.Hrsz);
            property.FloorArea = s.FloorArea;
            property.Rooms = s.Rooms;
            property.BuildYear = s.BuildYear;
            property.ElectricityMeter = Clean(s.ElectricityMeter);
            property.GasMeter = Clean(s.GasMeter);
            property.WaterMeter = Clean(s.WaterMeter);
            property.Deposit = s.Deposit;
            property.Notes = Clean(s.Notes);
            property.RentVat = Clean(s.RentVat);
            property.UtilityVat = Clean(s.UtilityVat);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ValidateSheet(PropertySheetDto? s)
    {
        if (s is null) return null;
        if (s.FloorArea is < 0 or > 100_000) return "Az alapterület 0 és 100 000 m² között lehet.";
        if (s.Rooms is < 0 or > 100) return "A szobák száma 0 és 100 között lehet.";
        if (s.BuildYear is { } y && (y < 1500 || y > DateTime.UtcNow.Year + 5)) return "Érvénytelen építési év.";
        if (s.Deposit is < 0) return "A kaució nem lehet negatív.";
        if ((s.Notes?.Length ?? 0) > 4000) return "A megjegyzés legfeljebb 4000 karakter lehet.";
        if ((!string.IsNullOrWhiteSpace(s.RentVat) && !Lakaskezelo.Domain.VatCodes.IsValid(s.RentVat.Trim()))
            || (!string.IsNullOrWhiteSpace(s.UtilityVat) && !Lakaskezelo.Domain.VatCodes.IsValid(s.UtilityVat.Trim())))
            return "Érvénytelen áfakulcs.";
        return null;
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
    private void SyncOwners(Property property, List<PropertyOwnerInput> requestedOwners)
    {
        var requested = requestedOwners
            .GroupBy(o => o.OwnerId)
            .ToDictionary(g => g.Key, g => NormalizeShare(g.First().Share));
        var currentOwnerIds = property.PropertyOwners.Select(po => po.OwnerId).ToHashSet();
        var toRemove = property.PropertyOwners.Where(po => !requested.ContainsKey(po.OwnerId)).ToList();

        foreach (var po in toRemove) property.PropertyOwners.Remove(po);
        foreach (var po in property.PropertyOwners) po.Share = requested[po.OwnerId];
        foreach (var (ownerId, share) in requested.Where(r => !currentOwnerIds.Contains(r.Key)))
        {
            db.PropertyOwners.Add(new PropertyOwner { Id = Guid.NewGuid(), PropertyId = property.Id, OwnerId = ownerId, Share = share });
        }
    }

    // Csak a saját cég tulajdonosai választhatók (a szűrő miatt a másik cégé "nem létezik").
    private async Task<bool> OwnersExistAsync(List<PropertyOwnerInput> owners, CancellationToken ct)
    {
        var ids = owners.Select(o => o.OwnerId).Distinct().ToList();
        return ids.Count == 0 || await db.Owners.CountAsync(o => ids.Contains(o.Id), ct) == ids.Count;
    }

    private static string? NormalizeShare(string? share) =>
        string.IsNullOrWhiteSpace(share) ? null : share.Replace(" ", "");

    // A hányad "számláló/nevező" alakú tört. Vagy minden tulajdonosnál meg kell adni (és akkor az
    // összegüknek pontosan 1-nek kell lennie), vagy egyiknél sem (a számla ilyenkor 1/n-t ír) —
    // vegyesen a hiányzók 1/n-je ellentmondásos arányt adna a számlán.
    private static string? ValidateShares(List<PropertyOwnerInput> owners)
    {
        var withShare = owners.Count(o => NormalizeShare(o.Share) is not null);
        if (withShare == 0) return null;
        if (withShare != owners.Count)
        {
            return "A tulajdoni hányadot vagy minden tulajdonosnál add meg, vagy egyiknél se.";
        }

        long sumNumerator = 0, sumDenominator = 1;
        foreach (var owner in owners)
        {
            var share = NormalizeShare(owner.Share)!;

            var parts = share.Split('/');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var n) || !long.TryParse(parts[1], out var d) || n <= 0 || d <= 0 || n > d)
            {
                return $"Érvénytelen tulajdoni hányad: „{owner.Share}”. Tört alakban add meg, pl. 1/2.";
            }
            sumNumerator = sumNumerator * d + n * sumDenominator;
            sumDenominator *= d;
        }
        return sumNumerator != sumDenominator
            ? "A tulajdoni hányadok összege nem 1 (pl. 1/2 + 1/2, vagy 1/2 + 1/4 + 1/4)."
            : null;
    }

    private static PropertyDetailDto ToDetailDto(Property property)
    {
        var next = BillingCalendar.NextBilling(BudapestClock.Now, property.BillingDayOfMonth, property.BillingHour, property.BillingMinute);
        return new(
        property.Id, property.Name, property.RentAmount, property.InvoicePrefix,
        property.BillingDayOfMonth, property.BillingHour, property.BillingMinute, property.IsActive,
        [.. property.PropertyOwners.Select(po => new PropertyOwnerDto(po.OwnerId, po.Owner!.Name, po.Share))],
        [.. property.Tenants.Select(t => new TenantDto(t.Id, t.Name, t.Email, t.Phone, t.Address, t.TaxId, t.PropertyId, property.Name, t.MoveInDate, t.MoveOutDate, t.Notes, t.RentCurrency, t.RentPaymentTiming, t.TenantPaysSzja, 0))],
        new PropertySheetDto(property.PropertyType, property.PostalCode, property.City, property.Street, property.FloorDoor, property.Hrsz,
            property.FloorArea, property.Rooms, property.BuildYear, property.ElectricityMeter, property.GasMeter, property.WaterMeter,
            property.Deposit, property.Notes, property.RentVat, property.UtilityVat),
        next.At, next.Period.Year, next.Period.Month);
    }

    private static UtilityCostEntryDto ToDto(UtilityCostEntry entry) => new(entry.Id, entry.Year, entry.Month, entry.Label, entry.Amount, entry.CreatedAt,
        IsLocked(entry), entry.InvoiceId, entry.Invoice?.Number);
}
