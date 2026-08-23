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

    [HttpPost]
    public async Task<ActionResult<TenantDto>> Create(UpsertTenantRequest request, CancellationToken ct)
    {
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
