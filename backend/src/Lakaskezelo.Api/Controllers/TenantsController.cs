using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/tenants")]
public class TenantsController(LakaskezeloDbContext db, Lakaskezelo.Api.Documents.DocumentStorage documents) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TenantDto>>> List(CancellationToken ct)
    {
        var tenants = await db.Tenants.Include(t => t.Property).OrderBy(t => t.Name).ToListAsync(ct);
        var balances = await BalancesAsync(null, ct);
        return Ok(tenants.Select(t => ToDto(t, balances.GetValueOrDefault(t.Id))));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantDto>> Get(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants.Include(t => t.Property).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();
        var balances = await BalancesAsync(id, ct);
        return Ok(ToDto(tenant, balances.GetValueOrDefault(id)));
    }

    // ---------- Egyenleg ----------

    // A bérlő folyószámlája időrendben: kiállított bizonylatok (a sikertelen nem számít; a sztornó negatív terhelés)
    // és befizetések, futó egyenleggel. Az egyenlegbe csak az számít, ami bizonylatra került: a felvett (vagy a rezsi
    // tételekből előre behúzott), de még ki nem számlázott rezsi nem tartozás — az összege csak tájékoztatásul megy
    // a válaszban (UnbilledTotal).
    [HttpGet("{id:guid}/ledger")]
    public async Task<ActionResult<TenantLedgerDto>> Ledger(Guid id, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return NotFound();
        var invoices = await ChargedInvoices().Where(i => i.TenantId == id).ToListAsync(ct);
        var payments = await db.TenantPayments.Include(p => p.Invoice).Where(p => p.TenantId == id).ToListAsync(ct);
        var openUtilities = (await Billing.TenantBalances.OpenUtilitiesAsync(db, id, ct)).Select(o => o.Entry).ToList();

        var rows = invoices.Select(i => new
            {
                Sort = (Date: DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(i.IssuedAt, DateTimeKind.Utc), Billing.BudapestClock.Zone)), At: i.IssuedAt),
                Entry = new TenantLedgerEntryDto("invoice", i.Id, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(i.IssuedAt, DateTimeKind.Utc), Billing.BudapestClock.Zone)),
                    i.CancelsInvoiceId is null ? $"Bizonylat — {i.PeriodYear}. {i.PeriodMonth:D2}." : $"Sztornó — {i.PeriodYear}. {i.PeriodMonth:D2}.",
                    i.AmountTotal, 0, i.Id, i.Number, i.CancelsInvoiceId is not null, i.CancelledAt is not null, null),
            })
            .Concat(payments.Select(p => new
            {
                Sort = (Date: p.Date, At: p.CreatedAt),
                Entry = new TenantLedgerEntryDto("payment", p.Id, p.Date, p.Invoice is null ? "Befizetés" : "Befizetés a bizonylatra",
                    -p.Amount, 0, p.InvoiceId, p.Invoice?.Number, false, false, p.Note),
            }))
            .OrderBy(r => r.Sort.Date).ThenBy(r => r.Sort.At)
            .ToList();

        var running = 0m;
        var entries = new List<TenantLedgerEntryDto>(rows.Count);
        foreach (var r in rows)
        {
            running += r.Entry.Amount;
            entries.Add(r.Entry with { Balance = running });
        }
        entries.Reverse(); // a legfrissebb elöl
        var charged = invoices.Sum(i => i.AmountTotal);
        var unbilled = openUtilities.Sum(u => u.Amount);
        var paid = payments.Sum(p => p.Amount);
        return Ok(new TenantLedgerDto(charged - paid, charged, paid, entries, unbilled));
    }

    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<TenantPaymentDto>> AddPayment(Guid id, CreateTenantPaymentRequest request, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return NotFound();
        if (request.Amount <= 0) return BadRequest(new { message = "A befizetett összegnek nagyobbnak kell lennie nullánál." });
        if (request.Amount > 1_000_000_000m) return BadRequest(new { message = "Túl nagy összeg." });
        var today = DateOnly.FromDateTime(Billing.BudapestClock.Now);
        if (request.Date > today.AddDays(1)) return BadRequest(new { message = "A befizetés dátuma nem lehet a jövőben." });
        if (request.Date < today.AddYears(-10)) return BadRequest(new { message = "Érvénytelen dátum." });
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 500) return BadRequest(new { message = "A megjegyzés legfeljebb 500 karakter lehet." });

        var payment = new TenantPayment
        {
            Id = Guid.NewGuid(),
            TenantId = id,
            Date = request.Date,
            Amount = Math.Round(request.Amount, 2),
            Note = note,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = User.GetUserId(),
        };
        db.TenantPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        return Ok(new TenantPaymentDto(payment.Id, payment.Date, payment.Amount, payment.Note, payment.CreatedAt));
    }

    [HttpDelete("{id:guid}/payments/{paymentId:guid}")]
    public async Task<IActionResult> DeletePayment(Guid id, Guid paymentId, CancellationToken ct)
    {
        var payment = await db.TenantPayments.Include(p => p.Invoice).FirstOrDefaultAsync(p => p.Id == paymentId && p.TenantId == id, ct);
        if (payment is null) return NotFound();
        db.TenantPayments.Remove(payment);
        await db.SaveChangesAsync(ct);
        // Bizonylatra rögzített befizetés törlésével a bizonylat fizetettsége is újraszámolódik.
        if (payment.Invoice is { } paidInvoice)
        {
            await Billing.InvoicePayments.RecalculateAsync(db, paidInvoice, ct);
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    private IQueryable<Invoice> ChargedInvoices() => db.Invoices
        .Where(i => i.TenantId != null && i.Status != Domain.Enums.InvoiceStatus.Failed);

    // Bérlőnként: kiállított bizonylatok összege − befizetések (egy bérlőre vagy mindre).
    private async Task<Dictionary<Guid, decimal>> BalancesAsync(Guid? tenantId, CancellationToken ct)
    {
        var charged = await ChargedInvoices().Where(i => tenantId == null || i.TenantId == tenantId)
            .GroupBy(i => i.TenantId!.Value).Select(g => new { g.Key, Sum = g.Sum(i => i.AmountTotal) }).ToListAsync(ct);
        var paid = await db.TenantPayments.Where(p => tenantId == null || p.TenantId == tenantId)
            .GroupBy(p => p.TenantId).Select(g => new { g.Key, Sum = g.Sum(p => p.Amount) }).ToListAsync(ct);
        var result = charged.ToDictionary(c => c.Key, c => c.Sum);
        foreach (var p in paid) result[p.Key] = result.GetValueOrDefault(p.Key) - p.Sum;
        return result;
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
        var email = EmailAddresses.Normalize(request.Email, out var emailError);
        if (emailError is not null) return BadRequest(new { message = emailError });
        if (LeaseEndError(request) is { } leaseError) return BadRequest(new { message = leaseError });
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Email = email,
            Phone = request.Phone,
            Address = request.Address,
            TaxId = request.TaxId,
            PropertyId = request.PropertyId,
            MoveInDate = request.MoveInDate,
            MoveOutDate = request.MoveOutDate,
            LeaseEndDate = request.LeaseEndDate,
            Notes = request.Notes,
            RentCurrency = request.RentCurrency,
            RentPaymentTiming = request.RentPaymentTiming ?? Domain.Enums.RentPaymentTiming.Advance,
            TenantPaysSzja = request.TenantPaysSzja ?? false,
            InvoiceAttachments = request.InvoiceAttachments ?? Domain.Enums.InvoiceAttachmentPreference.Ask,
            CreatedAt = DateTime.UtcNow,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        await db.Entry(tenant).Reference(t => t.Property).LoadAsync(ct);
        return Ok(ToDto(tenant, 0));
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

        if (request.LeaseEndDate != tenant.LeaseEndDate && LeaseEndError(request) is { } leaseError) return BadRequest(new { message = leaseError });
        // Csak a módosított címet ellenőrizzük: egy régebbi, szabálytalan érték ne akadályozza a többi mező mentését.
        if (request.Email != tenant.Email)
        {
            var email = EmailAddresses.Normalize(request.Email, out var emailError);
            if (emailError is not null) return BadRequest(new { message = emailError });
            tenant.Email = email;
        }
        tenant.Name = request.Name.Trim();
        tenant.Phone = request.Phone;
        tenant.Address = request.Address;
        tenant.TaxId = request.TaxId;
        tenant.PropertyId = request.PropertyId;
        tenant.MoveInDate = request.MoveInDate;
        tenant.MoveOutDate = request.MoveOutDate;
        tenant.LeaseEndDate = request.LeaseEndDate;
        tenant.Notes = request.Notes;
        tenant.RentCurrency = request.RentCurrency;
        if (request.RentPaymentTiming is { } timing) tenant.RentPaymentTiming = timing;
        if (request.TenantPaysSzja is { } paysSzja) tenant.TenantPaysSzja = paysSzja;
        if (request.InvoiceAttachments is { } attachments) tenant.InvoiceAttachments = attachments;
        await db.SaveChangesAsync(ct);

        await db.Entry(tenant).Reference(t => t.Property).LoadAsync(ct);
        var balances = await BalancesAsync(id, ct);
        return Ok(ToDto(tenant, balances.GetValueOrDefault(id)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants.FindAsync([id], ct);
        if (tenant is null) return NotFound();
        // A csak ehhez kapcsolódó dokumentumok vele együtt törlődnek (a tárhelyről is).
        await documents.DeleteExclusiveAsync(d => d.TenantId == id && d.PropertyId == null && d.OwnerId == null, ct);
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? LeaseEndError(UpsertTenantRequest request) =>
        request.LeaseEndDate is { } end && request.MoveInDate is { } moveIn && end < moveIn
            ? "A szerződés lejárata nem lehet a beköltözés előtt." : null;

    private static TenantDto ToDto(Tenant tenant, decimal balance) => new(
        tenant.Id, tenant.Name, tenant.Email, tenant.Phone, tenant.Address, tenant.TaxId,
        tenant.PropertyId, tenant.Property?.Name,
        tenant.MoveInDate, tenant.MoveOutDate, tenant.Notes, tenant.RentCurrency,
        tenant.RentPaymentTiming, tenant.TenantPaysSzja, balance, tenant.InvoiceAttachments, tenant.LeaseEndDate);
}
