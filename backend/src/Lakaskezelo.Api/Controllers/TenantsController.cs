using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/tenants")]
public class TenantsController(LakaskezeloDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TenantDto>>> List(CancellationToken ct)
    {
        var tenants = await db.Tenants.Include(t => t.Property).OrderBy(t => t.Name).ToListAsync(ct);
        return Ok(tenants.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantDto>> Get(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants.Include(t => t.Property).FirstOrDefaultAsync(t => t.Id == id, ct);
        return tenant is null ? NotFound() : Ok(ToDto(tenant));
    }

    // A bérlő adatlapjához: a neki kiállított számlák.
    [HttpGet("{id:guid}/invoices")]
    public async Task<ActionResult<List<InvoiceDto>>> Invoices(Guid id, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return NotFound();
        var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .Where(i => i.TenantId == id)
            .OrderByDescending(i => i.PeriodYear).ThenByDescending(i => i.PeriodMonth)
            .ToListAsync(ct);
        return Ok(invoices.Select(InvoicesController.ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<TenantDto>> Create(UpsertTenantRequest request, CancellationToken ct)
    {
        if (request.PropertyId is { } propertyId && !await db.Properties.AnyAsync(p => p.Id == propertyId, ct))
        {
            return BadRequest(new { message = "A megadott ingatlan nem található." });
        }
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            TaxId = request.TaxId,
            PropertyId = request.PropertyId,
            MoveInDate = request.MoveInDate,
            MoveOutDate = request.MoveOutDate,
            Notes = request.Notes,
            RentCurrency = request.RentCurrency,
            CreatedAt = DateTime.UtcNow,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        await db.Entry(tenant).Reference(t => t.Property).LoadAsync(ct);
        return Ok(ToDto(tenant));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TenantDto>> Update(Guid id, UpsertTenantRequest request, CancellationToken ct)
    {
        var tenant = await db.Tenants.Include(t => t.Property).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();
        if (request.PropertyId is { } propertyId && !await db.Properties.AnyAsync(p => p.Id == propertyId, ct))
        {
            return BadRequest(new { message = "A megadott ingatlan nem található." });
        }

        tenant.Name = request.Name.Trim();
        tenant.Email = request.Email;
        tenant.Phone = request.Phone;
        tenant.Address = request.Address;
        tenant.TaxId = request.TaxId;
        tenant.PropertyId = request.PropertyId;
        tenant.MoveInDate = request.MoveInDate;
        tenant.MoveOutDate = request.MoveOutDate;
        tenant.Notes = request.Notes;
        tenant.RentCurrency = request.RentCurrency;
        await db.SaveChangesAsync(ct);

        await db.Entry(tenant).Reference(t => t.Property).LoadAsync(ct);
        return Ok(ToDto(tenant));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant is null) return NotFound();
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static TenantDto ToDto(Tenant tenant) => new(
        tenant.Id, tenant.Name, tenant.Email, tenant.Phone, tenant.Address, tenant.TaxId,
        tenant.PropertyId, tenant.Property?.Name,
        tenant.MoveInDate, tenant.MoveOutDate, tenant.Notes, tenant.RentCurrency);
}
