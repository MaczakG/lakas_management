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
[Route("api/invoices")]
public class InvoicesController(LakaskezeloDbContext db, InvoiceGenerationService generator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<InvoiceDto>>> List(CancellationToken ct)
    {
        var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .OrderByDescending(i => i.IssuedAt)
            .ToListAsync(ct);
        return Ok(invoices.Select(ToDto));
    }

    // Manuális "Generálás most" / "Újraküldés" — ugyanazt a logikát futtatja, mint az automatikus
    // ütemező (InvoiceGenerationService), így egy korábban Failed számla is újrapróbálható innen.
    [HttpPost("generate")]
    public async Task<ActionResult<InvoiceDto>> Generate(GenerateInvoiceRequest request, CancellationToken ct)
    {
        var property = await db.Properties.FindAsync([request.PropertyId], ct);
        if (property is null) return NotFound(new { message = "Az ingatlan nem található." });

        var invoice = await generator.GenerateAndSendAsync(property, request.Year, request.Month, ct);

        await db.Entry(invoice).Reference(i => i.Property).LoadAsync(ct);
        await db.Entry(invoice).Reference(i => i.Tenant).LoadAsync(ct);
        await db.Entry(invoice).Collection(i => i.Lines).LoadAsync(ct);

        return Ok(ToDto(invoice));
    }

    // Csak a még ki nem küldött (Generated/Failed) számlák törölhetők — pl. téves/teszt generálás
    // után, hogy az adott időszak rezsi tételei újra szerkeszthetők legyenek (ld.
    // PropertiesController.IsPeriodLockedAsync). Egy ténylegesen kiküldött (Sent) számlát a
    // rendszer alapból nem enged törölni, az már valós, bérlőnek elküldött bizonylat — a
    // force=true csak szándékos admin felülbírálásra való (pl. téves teszt-küldés törlése).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool force, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status == Domain.Enums.InvoiceStatus.Sent && !force)
        {
            return Conflict(new { message = "Egy már kiküldött számla nem törölhető." });
        }

        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public static InvoiceDto ToDto(Invoice invoice) => new(
        invoice.Id, invoice.PropertyId, invoice.Property?.Name ?? "", invoice.Tenant?.Name,
        invoice.PeriodYear, invoice.PeriodMonth, invoice.Number, invoice.IssuedAt, invoice.DueDate,
        invoice.AmountTotal, invoice.Status, invoice.PdfDriveLink, invoice.SentAt, invoice.ErrorMessage,
        [.. invoice.Lines.Select(l => new InvoiceLineDto(l.Label, l.Amount))]);
}
