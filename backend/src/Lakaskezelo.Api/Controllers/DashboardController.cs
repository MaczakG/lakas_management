using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Vezérlőpult (dashboard.html): egy év összesítői (számlák, rezsi) és a következő 30 nap eseményei.
// A sikertelen (Failed) számlák nem számítanak bele a kiszámlázott összegbe, mert nem mentek ki —
// ugyanúgy, mint a számla csomagoknál; az állapot-bontásban külön látszanak.
[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController(LakaskezeloDbContext db) : ControllerBase
{
    private const int UtilityCategoryLimit = 5;
    private const string OtherCategory = "Egyéb";
    private const int UpcomingDays = 30;

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get([FromQuery] int? year, CancellationToken ct)
    {
        var now = BudapestClock.Now;
        var today = DateOnly.FromDateTime(now);
        var y = year is >= 2000 and <= 2100 ? year.Value : now.Year;

        // Az archivált számlák nem számítanak bele az összesítőkbe.
        var invoices = await db.Invoices.Where(i => (i.PeriodYear == y || i.PeriodYear == y - 1) && i.ArchivedAt == null)
            .Select(i => new { i.PeriodYear, i.PeriodMonth, i.Status, i.AmountTotal, i.PropertyId, PropertyName = i.Property!.Name })
            .ToListAsync(ct);
        var current = invoices.Where(i => i.PeriodYear == y).ToList();
        var previous = invoices.Where(i => i.PeriodYear == y - 1 && i.Status != InvoiceStatus.Failed).ToList();
        var counted = current.Where(i => i.Status != InvoiceStatus.Failed).ToList();

        // Az előző évvel csak az összevethető időszakot vetjük össze: a folyó évben az aktuális hónapig.
        var compareUntilMonth = y == now.Year ? now.Month : 12;
        var previousToDate = previous.Where(i => i.PeriodMonth <= compareUntilMonth).Sum(i => i.AmountTotal);

        StatusSummary Status(InvoiceStatus s) => new(
            current.Count(i => i.Status == s), current.Where(i => i.Status == s).Sum(i => i.AmountTotal));

        var monthly = Enumerable.Range(1, 12).Select(m => new MonthAmount(m,
            counted.Where(i => i.PeriodMonth == m).Sum(i => i.AmountTotal),
            previous.Where(i => i.PeriodMonth == m).Sum(i => i.AmountTotal),
            counted.Count(i => i.PeriodMonth == m))).ToList();

        var byProperty = counted.GroupBy(i => new { i.PropertyId, i.PropertyName })
            .Select(g => new PropertyAmount(g.Key.PropertyId, g.Key.PropertyName, g.Sum(i => i.AmountTotal), g.Count()))
            .OrderByDescending(p => p.Amount).ThenBy(p => p.Name)
            .ToList();

        // Rezsi: a tételek megnevezése szabad szöveg, ezért kis-nagybetűtől függetlenül csoportosítjuk; a legnagyobb
        // öt kategória külön sávot kap, a többi „Egyéb”-be kerül (több szín már nem különböztethető meg biztosan).
        var utilityRows = await db.UtilityCostEntries.Where(u => u.Year == y)
            .Select(u => new { u.Month, u.Label, u.Amount })
            .ToListAsync(ct);
        var normalized = utilityRows.Select(u => new { u.Month, Label = NormalizeLabel(u.Label), u.Amount }).ToList();
        var ranked = normalized.GroupBy(u => u.Label.ToLowerInvariant())
            .Select(g => new { Key = g.Key, Name = g.GroupBy(x => x.Label).OrderByDescending(n => n.Count()).First().Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(g => g.Total).ThenBy(g => g.Name)
            .ToList();
        var topKeys = ranked.Count > UtilityCategoryLimit + 1
            ? ranked.Take(UtilityCategoryLimit).ToList()
            : ranked;
        var categoryOf = topKeys.ToDictionary(k => k.Key, k => k.Name);
        var categories = topKeys.Select(k => k.Name).ToList();
        // Ha maga az „Egyéb” is a legnagyobbak közt van, a kimaradó tételek abba olvadnak (nem lesz két „Egyéb”).
        var otherName = categoryOf.GetValueOrDefault(OtherCategory.ToLowerInvariant()) ?? OtherCategory;
        if (ranked.Count > topKeys.Count && !categories.Contains(otherName)) categories.Add(otherName);
        string CategoryOf(string label) => categoryOf.GetValueOrDefault(label.ToLowerInvariant()) ?? otherName;
        var utilityMonthly = Enumerable.Range(1, 12).Select(m => new UtilityMonth(m,
            categories.Select(c => normalized.Where(u => u.Month == m && CategoryOf(u.Label) == c).Sum(u => u.Amount)).ToList()))
            .ToList();

        // Kihasználtság: aktív ingatlan, amin ma lakó bérlő van.
        var properties = await db.Properties.Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Name, p.BillingDayOfMonth, p.BillingHour, p.BillingMinute })
            .ToListAsync(ct);
        var tenants = await db.Tenants.Where(t => t.PropertyId != null)
            .Select(t => new { t.Id, t.Name, t.PropertyId, t.MoveInDate, t.MoveOutDate, PropertyName = t.Property!.Name, PropertyActive = t.Property.IsActive })
            .ToListAsync(ct);
        var activeIds = properties.Select(p => p.Id).ToHashSet();
        var rented = tenants
            .Where(t => activeIds.Contains(t.PropertyId!.Value)
                && (t.MoveInDate is null || t.MoveInDate <= today)
                && (t.MoveOutDate is null || t.MoveOutDate >= today))
            .Select(t => t.PropertyId!.Value).Distinct().Count();

        // Közelgő események (a következő 30 nap): számla-kiküldések, be- és kiköltözések.
        var horizon = today.AddDays(UpcomingDays);
        var upcoming = new List<UpcomingEvent>();
        foreach (var p in properties)
        {
            var next = BillingCalendar.NextBillingAt(now, p.BillingDayOfMonth, p.BillingHour, p.BillingMinute);
            if (DateOnly.FromDateTime(next) <= horizon)
            {
                upcoming.Add(new UpcomingEvent(DateOnly.FromDateTime(next), "billing", p.Id, p.Name, null, null));
            }
        }
        foreach (var t in tenants.Where(t => t.PropertyActive))
        {
            if (t.MoveInDate is { } inDate && inDate >= today && inDate <= horizon)
                upcoming.Add(new UpcomingEvent(inDate, "moveIn", t.PropertyId!.Value, t.PropertyName, t.Id, t.Name));
            if (t.MoveOutDate is { } outDate && outDate >= today && outDate <= horizon)
                upcoming.Add(new UpcomingEvent(outDate, "moveOut", t.PropertyId!.Value, t.PropertyName, t.Id, t.Name));
        }

        var years = await db.Invoices.Where(i => i.ArchivedAt == null).Select(i => i.PeriodYear).Distinct().ToListAsync(ct);
        years.Add(now.Year);
        years.Add(y);

        return Ok(new DashboardDto(
            y,
            years.Distinct().OrderByDescending(v => v).ToList(),
            compareUntilMonth,
            counted.Sum(i => i.AmountTotal),
            counted.Count,
            previousToDate,
            Status(InvoiceStatus.Sent), Status(InvoiceStatus.Generated), Status(InvoiceStatus.Failed),
            monthly,
            byProperty,
            normalized.Sum(u => u.Amount),
            categories,
            utilityMonthly,
            properties.Count,
            rented,
            upcoming.OrderBy(e => e.Date).ThenBy(e => e.PropertyName).ToList()));
    }

    private static string NormalizeLabel(string label)
    {
        var trimmed = (label ?? "").Trim();
        return trimmed.Length == 0 ? OtherCategory : trimmed;
    }
}
