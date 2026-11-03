using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/invoices")]
public class InvoicesController(LakaskezeloDbContext db, InvoiceGenerationService generator, InvoicePdfStore pdfStore,
    IOptions<JsonOptions> jsonOptions, ILogger<InvoicesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<InvoiceDto>>> List(CancellationToken ct)
    {
        var invoices = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .OrderByDescending(i => i.IssuedAt)
            .ToListAsync(ct);
        return Ok(invoices.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return NotFound();
        var related = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .Where(i => i.Id != id && i.PropertyId == invoice.PropertyId && i.PeriodYear == invoice.PeriodYear && i.PeriodMonth == invoice.PeriodMonth)
            .OrderBy(i => i.IssuedAt)
            .ToListAsync(ct);
        var cancelledBy = invoice.CancelledByUserId is { } uid
            ? await db.Users.Where(u => u.Id == uid).Select(u => u.FullName).FirstOrDefaultAsync(ct)
            : null;
        var payments = await db.TenantPayments.Where(p => p.InvoiceId == id).OrderBy(p => p.Date).ThenBy(p => p.CreatedAt)
            .Select(p => new TenantPaymentDto(p.Id, p.Date, p.Amount, p.Note, p.CreatedAt))
            .ToListAsync(ct);
        return Ok(new InvoiceDetailDto(ToDto(invoice), cancelledBy, invoice.RentConversionNote, [.. related.Select(ToDto)], payments));
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken ct)
    {
        var invoice = await WithPdfData(db.Invoices).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return NotFound();
        try
        {
            var pdf = await pdfStore.GetOrCreateAsync(invoice, ct);
            return File(pdf, "application/pdf", InvoicePdfStore.FileName(invoice));
        }
        catch (Invoicing.ProviderException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }

    // Minden, ami a PDF (újra)generálásához kell — ld. InvoicePdfStore.GetOrCreateAsync.
    public static IQueryable<Invoice> WithPdfData(IQueryable<Invoice> invoices) => invoices
        .Include(i => i.Lines)
        .Include(i => i.Tenant)
        .Include(i => i.Property!).ThenInclude(p => p.PropertyOwners).ThenInclude(po => po.Owner);

    // Manuális "Generálás most" / "Újraküldés" — ugyanazt a logikát futtatja, mint az automatikus
    // ütemező (InvoiceGenerationService), így egy korábban Failed számla is újrapróbálható innen.
    [HttpPost("generate")]
    public async Task<ActionResult<InvoiceDto>> Generate(GenerateInvoiceRequest request, CancellationToken ct)
    {
        var property = await db.Properties.FindAsync([request.PropertyId], ct);
        if (property is null) return NotFound(new { message = "Az ingatlan nem található." });

        Invoice invoice;
        try
        {
            invoice = await generator.GenerateAndSendAsync(property, request.Year, request.Month, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }

        await db.Entry(invoice).Reference(i => i.Property).LoadAsync(ct);
        await db.Entry(invoice).Reference(i => i.Tenant).LoadAsync(ct);
        await db.Entry(invoice).Collection(i => i.Lines).LoadAsync(ct);

        return Ok(ToDto(invoice));
    }

    // Ugyanaz, mint a Generate, de folyamatjelzővel: a válasz soronként egy JSON (application/x-ndjson) —
    // {"type":"plan","steps":[...]}, {"type":"step","step":"pdf"}, …, végül {"type":"done","invoice":{…}}
    // vagy {"type":"error","message":"…"}. A generálás a böngésző bezárásakor sem szakad félbe (különben pl. a
    // kiküldött e-mail után a mentés elmaradhatna), ezért nem a kérés CancellationTokenjét kapja.
    [HttpPost("generate/stream")]
    public async Task GenerateWithProgress(GenerateInvoiceRequest request)
    {
        var json = jsonOptions.Value.JsonSerializerOptions;
        var property = await db.Properties.FindAsync([request.PropertyId], HttpContext.RequestAborted);
        if (property is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new { message = "Az ingatlan nem található." }, json);
            return;
        }

        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // nginx: ne pufferelje, hogy a lépések azonnal átérjenek

        async Task Write(object message)
        {
            if (HttpContext.RequestAborted.IsCancellationRequested) return;
            await Response.WriteAsync(JsonSerializer.Serialize(message, json) + "\n");
            await Response.Body.FlushAsync();
        }

        try
        {
            var invoice = await generator.GenerateAndSendAsync(property, request.Year, request.Month, CancellationToken.None,
                progress => progress.Step == GenerationSteps.Plan
                    ? Write(new { type = "plan", steps = progress.Plan })
                    : Write(new { type = "step", step = progress.Step }));

            await db.Entry(invoice).Reference(i => i.Property).LoadAsync();
            await db.Entry(invoice).Reference(i => i.Tenant).LoadAsync();
            await db.Entry(invoice).Collection(i => i.Lines).LoadAsync();
            await Write(new { type = "done", invoice = ToDto(invoice) });
        }
        catch (InvalidOperationException ex)
        {
            try { await Write(new { type = "error", message = ex.Message }); }
            catch (Exception writeEx) { logger.LogDebug(writeEx, "Could not report the generation error to the client"); }
        }
        catch (Exception ex)
        {
            // A válasz már elindult, státuszkódot nem lehet váltani — a hibát a folyam utolsó sora jelzi.
            logger.LogError(ex, "Invoice generation with progress failed for property {PropertyId}", request.PropertyId);
            try { await Write(new { type = "error", message = "Hiba történt a generálás során." }); }
            catch (Exception writeEx) { logger.LogDebug(writeEx, "Could not report the generation error to the client"); }
        }
    }

    // A kiállított bizonylat nem törölhető és nem archiválható (nincs ilyen végpont): csak sztornózható. Ilyenkor
    // sztornó bizonylat készül (InvoiceGenerationService.CancelAsync); az eredeti „sztornózott” lesz, nem zárja
    // tovább az időszak rezsijét, és új számla készíthető ugyanarra az időszakra.
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<InvoiceDto>> Cancel(Guid id, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return NotFound();
        Invoice storno;
        try
        {
            storno = await generator.CancelAsync(invoice, User.GetUserId(), ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        await db.Entry(storno).Reference(i => i.Property).LoadAsync(ct);
        await db.Entry(storno).Reference(i => i.Tenant).LoadAsync(ct);
        return Ok(ToDto(storno));
    }

    // Befizetés rögzítése a bizonylatra (dátum + összeg, részletekben is): a bérlő egyenlegébe is bekerül, és ha a
    // befizetések elérik a végösszeget, a bizonylat fizetett lesz. A felajánlott összeg a hátralék; SZJA-t fizető
    // bérlőnél a végösszeg már az SZJA-mentes összeg (a bizonylaton „SZJA levonás” sor van).
    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<InvoiceDto>> AddPayment(Guid id, InvoicePaymentRequest request, CancellationToken ct)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return NotFound();
        if (invoice.IsStorno) return BadRequest(new { message = "Sztornó bizonylatra nem lehet befizetést rögzíteni." });
        if (invoice.Status == Domain.Enums.InvoiceStatus.Failed) return BadRequest(new { message = "Sikertelen bizonylatra nem lehet befizetést rögzíteni: nem lett kiállítva." });
        if (invoice.TenantId is null) return BadRequest(new { message = "A bizonylathoz nincs bérlő rendelve, így a befizetést nincs kinek elszámolni." });
        if (request.Amount <= 0) return BadRequest(new { message = "A befizetett összegnek nagyobbnak kell lennie nullánál." });
        if (request.Amount > 1_000_000_000m) return BadRequest(new { message = "Túl nagy összeg." });
        var today = DateOnly.FromDateTime(BudapestClock.Now);
        if (request.Date > today.AddDays(1)) return BadRequest(new { message = "A fizetés dátuma nem lehet a jövőben." });
        if (request.Date < today.AddYears(-10)) return BadRequest(new { message = "Érvénytelen dátum." });
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 500) return BadRequest(new { message = "A megjegyzés legfeljebb 500 karakter lehet." });

        db.TenantPayments.Add(new TenantPayment
        {
            Id = Guid.NewGuid(),
            TenantId = invoice.TenantId.Value,
            InvoiceId = invoice.Id,
            Date = request.Date,
            Amount = Math.Round(request.Amount, 2),
            Note = note,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = User.GetUserId(),
        });
        await db.SaveChangesAsync(ct);
        await InvoicePayments.RecalculateAsync(db, invoice, ct);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(invoice));
    }

    // A bizonylat összes befizetésének visszavonása (a bérlő egyenlegéből is törlődnek).
    [HttpDelete("{id:guid}/payments")]
    public async Task<ActionResult<InvoiceDto>> RemovePayments(Guid id, CancellationToken ct)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Property).Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return NotFound();
        db.TenantPayments.RemoveRange(await db.TenantPayments.Where(p => p.InvoiceId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        await InvoicePayments.RecalculateAsync(db, invoice, ct);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(invoice));
    }

    public static InvoiceDto ToDto(Invoice invoice) => new(
        invoice.Id, invoice.PropertyId, invoice.Property?.Name ?? "", invoice.Tenant?.Name,
        invoice.PeriodYear, invoice.PeriodMonth, invoice.Number, invoice.IssuedAt, invoice.DueDate,
        invoice.AmountTotal, invoice.Status, invoice.SentAt, invoice.ErrorMessage,
        [.. InvoiceLine.InDisplayOrder(invoice.Lines).Select(l => new InvoiceLineDto(l.Label, l.Amount))], invoice.CancelledAt, invoice.CancelsInvoiceId, invoice.Provider,
        invoice.PaidAt, invoice.PaidAmount);
}
