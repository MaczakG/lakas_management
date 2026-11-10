using System.Net;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Beköltözési tudnivalók: ingatlanonként egy szabad szöveg (kapukód, wifi, mérőórák helye, házirend…), amit
// egy gombbal ki lehet küldeni e-mailben a bérlőnek. A kapcsolóval (Sendable) a szöveg belső feljegyzésként is
// megtartható, ekkor nem küldhető ki.
[Authorize]
[ApiController]
[Route("api/properties/{id:guid}/move-in-info")]
public class MoveInInfoController(LakaskezeloDbContext db, IEmailSender emailSender, ILogger<MoveInInfoController> logger) : ControllerBase
{
    public const int MaxLength = 5000;

    [HttpGet]
    public async Task<ActionResult<MoveInInfoDto>> Get(Guid id, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Tenants).FirstOrDefaultAsync(p => p.Id == id, ct);
        return property is null ? NotFound() : Ok(await ToDtoAsync(property, ct));
    }

    [HttpPut]
    public async Task<ActionResult<MoveInInfoDto>> Update(Guid id, UpdateMoveInInfoRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Tenants).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();

        var text = MoveInInfo.Clean(request.Text);
        if (text is { Length: > MaxLength }) return BadRequest(new { message = $"A szöveg legfeljebb {MaxLength} karakter lehet." });

        property.MoveInInfo = text;
        property.MoveInInfoSendable = request.Sendable;
        await db.SaveChangesAsync(ct);
        return Ok(await ToDtoAsync(property, ct));
    }

    [HttpPost("send")]
    public async Task<ActionResult<MoveInInfoDto>> Send(Guid id, SendMoveInInfoRequest request, CancellationToken ct)
    {
        var property = await db.Properties.Include(p => p.Tenants).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (property is null) return NotFound();

        if (string.IsNullOrWhiteSpace(property.MoveInInfo)) return BadRequest(new { message = "Nincs kiküldhető szöveg. Előbb írd be a beköltözési tudnivalókat." });
        if (!property.MoveInInfoSendable) return BadRequest(new { message = "A szöveg belső feljegyzésnek van jelölve, ezért nem küldhető ki." });

        var tenant = MoveInInfo.Recipients(property.Tenants, DateOnly.FromDateTime(BudapestClock.Now)).FirstOrDefault(t => t.Id == request.TenantId);
        if (tenant is null) return BadRequest(new { message = "A címzett nem az ingatlan jelenlegi vagy leendő bérlője." });
        if (string.IsNullOrWhiteSpace(tenant.Email)) return BadRequest(new { message = "A bérlőnek nincs megadva e-mail címe." });
        if (!await emailSender.IsConfiguredAsync(ct)) return BadRequest(new { message = "Az e-mail küldés nincs beállítva." });

        var subject = $"Beköltözési tudnivalók – {property.Name}";
        var (html, plain) = MoveInInfo.Body(tenant.Name, property.Name, property.MoveInInfo);
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

    private async Task<MoveInInfoDto> ToDtoAsync(Property property, CancellationToken ct) => new(
        property.MoveInInfo, property.MoveInInfoSendable, await emailSender.IsConfiguredAsync(ct),
        MoveInInfo.Recipients(property.Tenants, DateOnly.FromDateTime(BudapestClock.Now))
            .Select(t => new MoveInRecipientDto(t.Id, t.Name, t.Email, t.MoveInDate, t.MoveInInfoSentAt)).ToList());
}

public static class MoveInInfo
{
    // A sorvégeket egységesíti, a szöveg végi üres sorokat levágja; az üres szöveg null.
    public static string? Clean(string? text)
    {
        var cleaned = text?.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
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

    // A levél törzse HTML-ben és sima szövegként. A szabad szöveg üres sorral elválasztott részei bekezdések,
    // a sortörések megmaradnak; a HTML-ben minden kódolva kerül be.
    public static (string Html, string Plain) Body(string tenantName, string propertyName, string text)
    {
        var greeting = $"Kedves {tenantName.Trim()}!";
        var intro = $"Az alábbiakban küldjük a beköltözéshez szükséges tudnivalókat.\nIngatlan: {propertyName.Trim()}";
        const string closing = "Ha kérdésed van, válaszolj erre a levélre.";

        static string P(string s) => $"""<p style="margin:0 0 12px;">{WebUtility.HtmlEncode(s).Replace("\n", "<br>")}</p>""";
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var html = P(greeting) + P(intro) + string.Concat(paragraphs.Select(P)) + P(closing);
        var plain = $"{greeting}\n\n{intro}\n\n{string.Join("\n\n", paragraphs)}\n\n{closing}";
        return (html, plain);
    }
}
