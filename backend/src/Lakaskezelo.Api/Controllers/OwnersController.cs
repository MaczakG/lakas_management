using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/owners")]
public class OwnersController(LakaskezeloDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<OwnerDto>>> List(CancellationToken ct)
    {
        var owners = await db.Owners
            .Include(o => o.PropertyOwners).ThenInclude(po => po.Property)
            .OrderBy(o => o.Name)
            .ToListAsync(ct);
        return Ok(owners.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OwnerDto>> Get(Guid id, CancellationToken ct)
    {
        var owner = await db.Owners.Include(o => o.PropertyOwners).ThenInclude(po => po.Property)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        return owner is null ? NotFound() : Ok(ToDto(owner));
    }

    [HttpPost]
    public async Task<ActionResult<OwnerDto>> Create(UpsertOwnerRequest request, CancellationToken ct)
    {
        var owner = new Owner
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Email = request.Email,
            Phone = request.Phone,
            TaxId = request.TaxId,
            BankAccount = request.BankAccount,
            Address = request.Address,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
        };
        db.Owners.Add(owner);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(owner));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OwnerDto>> Update(Guid id, UpsertOwnerRequest request, CancellationToken ct)
    {
        var owner = await db.Owners.Include(o => o.PropertyOwners).ThenInclude(po => po.Property).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (owner is null) return NotFound();

        owner.Name = request.Name.Trim();
        owner.Email = request.Email;
        owner.Phone = request.Phone;
        owner.TaxId = request.TaxId;
        owner.BankAccount = request.BankAccount;
        owner.Address = request.Address;
        owner.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(owner));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var owner = await db.Owners.FindAsync([id], ct);
        if (owner is null) return NotFound();
        db.Owners.Remove(owner);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static OwnerDto ToDto(Owner owner) => new(
        owner.Id, owner.Name, owner.Email, owner.Phone, owner.TaxId, owner.BankAccount, owner.Address, owner.Notes,
        [.. owner.PropertyOwners.Select(po => po.Property!.Name)]);
}
