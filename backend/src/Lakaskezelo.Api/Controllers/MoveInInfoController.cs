using System.Net;
using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Documents;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Beköltözési tudnivalók: ingatlanonként egy táblázat (megnevezés, megjegyzés — pl. kapukód, wifi, mérőórák helye),
// amit egy gombbal ki lehet küldeni e-mailben a bérlőnek. A kapcsolóval (Sendable) a tudnivalók belső feljegyzésként is
// megtarthatók, ekkor nem küldhetők ki. A bepipált sorokból átadás-átvételi jegyzőkönyv (PDF) készül, ami a
// dokumentumok közé kerül.
[Authorize]
[ApiController]
[Route("api/properties/{id:guid}/move-in-info")]
public class MoveInInfoController(LakaskezeloDbContext db, IEmailSender emailSender, AppSettingsService settingsService,
    DocumentStorage storage, ILogger<MoveInInfoController> logger) : ControllerBase
{
    private const string ProtocolTypeName = "Jegyzőkönyv";

    [HttpGet]
    public async Task<ActionResult<MoveInInfoDto>> Get(Guid id, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Tenants).FirstOrDefaultAsync(p => p.Id == id, ct);
        return property is null ? NotFound() : Ok(await ToDtoAsync(property, ct));
    }

    // A teljes táblázatot cseréli (a felület mindig az egészet küldi).
    [HttpPut]
    public async Task<ActionResult<MoveInInfoDto>> Update(Guid id, UpdateMoveInInfoRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Tenants).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();

        var (items, error) = MoveInInfo.CleanItems(request.Items);
        if (error is not null) return BadRequest(new { message = error });

        db.MoveInItems.RemoveRange(await db.MoveInItems.Where(i => i.PropertyId == id).ToListAsync(ct));
        db.MoveInItems.AddRange(items.Select((i, n) => new MoveInItem
        {
            Id = Guid.NewGuid(), PropertyId = id, Label = i.Label, Note = i.Note, InChecklist = i.InChecklist, SortOrder = n,
        }));
        property.MoveInInfoSendable = request.Sendable;
        await db.SaveChangesAsync(ct);
        return Ok(await ToDtoAsync(property, ct));
    }

    [HttpPost("send")]
    public async Task<ActionResult<MoveInInfoDto>> Send(Guid id, SendMoveInInfoRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Tenants).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();

        var items = await ItemsAsync(id, ct);
        if (items.Count == 0) return BadRequest(new { message = "Nincs kiküldhető tudnivaló. Előbb vedd fel és mentsd el a sorokat." });
        if (!property.MoveInInfoSendable) return BadRequest(new { message = "A tudnivalók belső feljegyzésnek vannak jelölve, ezért nem küldhetők ki." });

        var tenant = MoveInInfo.Recipients(property.Tenants, DateOnly.FromDateTime(BudapestClock.Now)).FirstOrDefault(t => t.Id == request.TenantId);
        if (tenant is null) return BadRequest(new { message = "A címzett nem az ingatlan jelenlegi vagy leendő bérlője." });
        if (string.IsNullOrWhiteSpace(tenant.Email)) return BadRequest(new { message = "A bérlőnek nincs megadva e-mail címe." });
        if (!await emailSender.IsConfiguredAsync(ct)) return BadRequest(new { message = "Az e-mail küldés nincs beállítva." });

        var subject = $"Beköltözési tudnivalók – {property.Name}";
        var (html, plain) = MoveInInfo.Body(tenant.Name, property.Name, items);
        var htmlBody = EmailTemplate.Render(preheader: subject, heading: "Beköltözési tudnivalók", bodyHtml: html);
        if (!await emailSender.SendAsync(tenant.Email.Trim(), subject, htmlBody, plain, null, ct))
        {
            logger.LogWarning("Move-in info e-mail failed for property {PropertyId}: {Error}", property.Id, emailSender.LastError);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Az e-mailt nem sikerült elküldeni. Próbáld újra később." });
        }

        tenant.MoveInInfoSentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await ToDtoAsync(property, ct));
    }

    // Átadás-átvételi jegyzőkönyv a bepipált sorokból, a megadott bérlőre (dátuma a beköltözés napja, ha nincs
    // megadva, a mai nap). A PDF új dokumentumként kerül az ingatlanhoz és a bérlőhöz, „Jegyzőkönyv” típussal.
    [HttpPost("protocol")]
    public async Task<ActionResult<HandoverProtocolResponse>> Protocol(Guid id, SendMoveInInfoRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Tenants).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();
        var tenant = MoveInInfo.Recipients(property.Tenants, DateOnly.FromDateTime(BudapestClock.Now)).FirstOrDefault(t => t.Id == request.TenantId);
        if (tenant is null) return BadRequest(new { message = "A bérlő nem az ingatlan jelenlegi vagy leendő bérlője." });
        var checklist = (await ItemsAsync(id, ct)).Where(i => i.InChecklist).ToList();
        if (checklist.Count == 0) return BadRequest(new { message = "Pipáld be (és mentsd el) azokat a sorokat, amelyek a jegyzőkönyvre kerüljenek." });

        var settings = await settingsService.GetAsync(ct);
        var date = tenant.MoveInDate ?? DateOnly.FromDateTime(BudapestClock.Now);
        var pdf = HandoverProtocolPdf.Generate(property, tenant, settings, checklist, date);

        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var name = $"Átadás-átvételi jegyzőkönyv – {tenant.Name.Trim()} ({date:yyyy.MM.dd.})";
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            CompanyId = property.CompanyId,
            Name = name.Length > 200 ? name[..200] : name,
            DocumentTypeId = await db.DocumentTypes.Where(t => t.Name == ProtocolTypeName).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct),
            PropertyId = property.Id,
            TenantId = tenant.Id,
            FileName = $"atadas-atveteli-jegyzokonyv-{date:yyyy-MM-dd}.pdf",
            ContentType = "application/pdf",
            SizeBytes = pdf.LongLength,
            UploadedAt = now,
            UploadedByUserId = userId,
        };
        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(), CompanyId = doc.CompanyId, DocumentId = doc.Id, Number = 1, FileName = doc.FileName,
            ContentType = doc.ContentType, SizeBytes = doc.SizeBytes, UploadedAt = now, UploadedByUserId = userId,
        };
        try
        {
            await storage.StoreAsync(version, pdf, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handover protocol storage failed for property {PropertyId}", property.Id);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "A jegyzőkönyvet nem sikerült a tárhelyre menteni. Próbáld újra később." });
        }
        db.Documents.Add(doc);
        db.DocumentVersions.Add(version);
        db.DocumentEvents.Add(new DocumentEvent
        {
            Id = Guid.NewGuid(), CompanyId = doc.CompanyId, DocumentId = doc.Id, Type = DocumentEventTypes.Uploaded, VersionNumber = 1, UserId = userId, At = now,
        });
        await db.SaveChangesAsync(ct);
        return Ok(new HandoverProtocolResponse(doc.Id));
    }

    private Task<List<MoveInItem>> ItemsAsync(Guid propertyId, CancellationToken ct) =>
        db.MoveInItems.AsNoTracking().Where(i => i.PropertyId == propertyId).OrderBy(i => i.SortOrder).ToListAsync(ct);

    private async Task<MoveInInfoDto> ToDtoAsync(Property property, CancellationToken ct) => new(
        (await ItemsAsync(property.Id, ct)).Select(i => new MoveInItemDto(i.Label, i.Note, i.InChecklist)).ToList(),
        property.MoveInInfoSendable, await emailSender.IsConfiguredAsync(ct),
        MoveInInfo.Recipients(property.Tenants, DateOnly.FromDateTime(BudapestClock.Now))
            .Select(t => new MoveInRecipientDto(t.Id, t.Name, t.Email, t.MoveInDate, t.MoveInInfoSentAt)).ToList());
}

public static class MoveInInfo
{
    public const int MaxItems = 100;
    public const int MaxLabel = 200;
    public const int MaxNote = 1000;

    // A sorok megtisztítása: a szövegek levágva, az üres megjegyzés null, a teljesen üres sor kimarad. Megnevezés
    // nélküli (de megjegyzéssel kitöltött) sor, túl hosszú szöveg vagy túl sok sor hibát ad.
    public static (List<MoveInItemDto> Items, string? Error) CleanItems(IEnumerable<MoveInItemDto>? items)
    {
        var result = new List<MoveInItemDto>();
        foreach (var item in items ?? [])
        {
            var label = item.Label?.Trim() ?? "";
            var note = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (label.Length == 0 && note is null) continue;
            if (label.Length == 0) return ([], "Minden kitöltött sorhoz adj meg megnevezést.");
            if (label.Length > MaxLabel) return ([], $"A megnevezés legfeljebb {MaxLabel} karakter lehet.");
            if (note?.Length > MaxNote) return ([], $"A megjegyzés legfeljebb {MaxNote} karakter lehet.");
            result.Add(new MoveInItemDto(label, note, item.InChecklist));
        }
        return result.Count > MaxItems ? ([], $"Legfeljebb {MaxItems} sor lehet.") : (result, null);
    }

    // Akinek kiküldhető: aki még nem költözött ki (a leendő bérlő is). Elöl a jelenlegi (a legutóbb beköltözött),
    // utána a leendők a beköltözés sorrendjében.
    public static List<Tenant> Recipients(IEnumerable<Tenant> tenants, DateOnly today)
    {
        bool Future(Tenant t) => t.MoveInDate is { } d && d > today;
        return tenants
            .Where(t => t.MoveOutDate is null || t.MoveOutDate >= today)
            .OrderBy(Future)
            .ThenBy(t => Future(t) ? t.MoveInDate!.Value.DayNumber : -(t.MoveInDate?.DayNumber ?? 0))
            .ThenBy(t => t.Name)
            .ToList();
    }

    // A levél törzse HTML-ben és sima szövegként: a tudnivalók táblázatban (megnevezés, megjegyzés), a megjegyzés
    // sortörései megmaradnak; a HTML-ben minden kódolva kerül be.
    public static (string Html, string Plain) Body(string tenantName, string propertyName, IReadOnlyList<MoveInItem> items)
    {
        var greeting = $"Kedves {tenantName.Trim()}!";
        var intro = $"Az alábbiakban küldjük a beköltözéshez szükséges tudnivalókat.\nIngatlan: {propertyName.Trim()}";
        const string closing = "Ha kérdésed van, válaszolj erre a levélre.";

        static string Enc(string s) => WebUtility.HtmlEncode(s).Replace("\n", "<br>");
        static string P(string s) => $"""<p style="margin:0 0 12px;">{Enc(s)}</p>""";
        const string cell = "padding:8px 10px;border-bottom:1px solid #E4E4E7;vertical-align:top;text-align:left;";
        var rows = string.Concat(items.Select(i =>
            $"""<tr><td style="{cell}font-weight:600;width:38%;">{Enc(i.Label)}</td><td style="{cell}">{Enc(i.Note ?? "")}</td></tr>"""));
        var table = $"""<table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border-collapse:collapse;margin:0 0 16px;font-size:14px;">{rows}</table>""";
        var html = P(greeting) + P(intro) + table + P(closing);
        var lines = items.Select(i => string.IsNullOrEmpty(i.Note) ? i.Label : $"{i.Label}: {i.Note}");
        var plain = $"{greeting}\n\n{intro}\n\n{string.Join("\n", lines)}\n\n{closing}";
        return (html, plain);
    }
}
