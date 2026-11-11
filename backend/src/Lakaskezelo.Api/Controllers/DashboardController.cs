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
public class DashboardController(LakaskezeloDbContext db, ICompanyContext companyContext) : ControllerBase
{
    private const int UtilityCategoryLimit = 5;
    private const string OtherCategory = "Egyéb";
    private const int UpcomingDays = 30;

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get([FromQuery] int? year, [FromQuery] Guid? propertyId, CancellationToken ct)
    {
        // Ingatlanválasztó: propertyId nélkül az összes ingatlan, különben minden szám csak arra az egyre vonatkozik.
        var allProperties = await db.Properties.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name)
            .Select(p => new DashboardProperty(p.Id, p.Name, p.IsActive)).ToListAsync(ct);
        if (propertyId is not null && allProperties.All(p => p.Id != propertyId)) return NotFound(new { message = "Az ingatlan nem található." });

        var now = BudapestClock.Now;
        var today = DateOnly.FromDateTime(now);
        var y = year is >= 2000 and <= 2100 ? year.Value : now.Year;

        // A sztornózott számla és a sztornója együtt nulla — egyik sem számít bele az összesítőkbe.
        var invoices = await db.Invoices.Where(i => (i.PeriodYear == y || i.PeriodYear == y - 1) && i.CancelledAt == null && i.CancelsInvoiceId == null
                && (propertyId == null || i.PropertyId == propertyId))
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
        var utilityRows = await db.UtilityCostEntries.Where(u => u.Year == y && (propertyId == null || u.PropertyId == propertyId))
            .Select(u => new { u.Month, u.Label, u.Amount, u.ForwardBilled })
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
        var properties = await db.Properties.Where(p => p.IsActive && (propertyId == null || p.Id == propertyId))
            .Select(p => new { p.Id, p.Name, p.BillingDayOfMonth, p.BillingHour, p.BillingMinute })
            .ToListAsync(ct);
        var tenants = await db.Tenants.Where(t => t.PropertyId != null && (propertyId == null || t.PropertyId == propertyId))
            .Select(t => new { t.Id, t.Name, t.PropertyId, t.MoveInDate, t.MoveOutDate, PropertyName = t.Property!.Name, PropertyActive = t.Property.IsActive })
            .ToListAsync(ct);
        var activeIds = properties.Select(p => p.Id).ToHashSet();
        var living = tenants
            .Where(t => activeIds.Contains(t.PropertyId!.Value)
                && (t.MoveInDate is null || t.MoveInDate <= today)
                && (t.MoveOutDate is null || t.MoveOutDate >= today))
            .ToList();
        var rented = living.Select(t => t.PropertyId!.Value).Distinct().Count();

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

        // Egyenleg ingatlanonként, a teljes időszakra (a tartozás nem évhez kötött). A bizonylat és a hozzá kötött befizetés
        // a bizonylat ingatlanához, a bizonylathoz nem kötött befizetés a bérlő ingatlanához tartozik; az ingatlan nélküli
        // bérlő ilyen befizetése csak az összesítettben szerepel.
        var expectedBy = (await db.Invoices.Where(i => i.TenantId != null && i.Status != InvoiceStatus.Failed)
                .GroupBy(i => i.PropertyId).Select(g => new { g.Key, Sum = g.Sum(i => i.AmountTotal) }).ToListAsync(ct))
            .ToDictionary(g => g.Key, g => g.Sum);
        foreach (var (_, entry) in await TenantBalances.OpenUtilitiesAsync(db, null, ct))
            expectedBy[entry.PropertyId] = expectedBy.GetValueOrDefault(entry.PropertyId) + entry.Amount;
        var payments = await db.TenantPayments
            .Select(p => new { p.Amount, PropertyId = p.InvoiceId != null ? (Guid?)p.Invoice!.PropertyId : p.Tenant!.PropertyId })
            .ToListAsync(ct);
        var paidBy = payments.Where(p => p.PropertyId != null).GroupBy(p => p.PropertyId!.Value).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var balanceByProperty = allProperties
            .Where(p => propertyId == null || p.Id == propertyId)
            .Select(p => new PropertyBalance(p.Id, p.Name, expectedBy.GetValueOrDefault(p.Id), paidBy.GetValueOrDefault(p.Id)))
            .Where(b => propertyId != null || b.Expected != 0 || b.Paid != 0 || activeIds.Contains(b.PropertyId))
            .ToList();
        var balanceExpected = balanceByProperty.Sum(b => b.Expected);
        var balancePaid = balanceByProperty.Sum(b => b.Paid) + (propertyId == null ? payments.Where(p => p.PropertyId == null).Sum(p => p.Amount) : 0);

        var years = await db.Invoices.Where(i => i.CancelledAt == null && i.CancelsInvoiceId == null).Select(i => i.PeriodYear).Distinct().ToListAsync(ct);
        years.Add(now.Year);
        years.Add(y);

        // Adózási kimutatás (csak magánszemély bérbeadónak): bevétel a kiállított bizonylatokból (a bérlő által levont
        // SZJA sora nélkül), költség a nem „tovább számlázandó” rezsi tételekből.
        TaxEstimate.Result? tax = null;
        var isBusiness = await db.Companies.Where(c => c.Id == companyContext.CompanyId).Select(c => c.IsBusiness).SingleOrDefaultAsync(ct);
        if (!isBusiness)
        {
            var withheld = (await db.InvoiceLines
                    .Where(l => l.Label == InvoiceGenerationService.SzjaLabel && l.Invoice!.PeriodYear == y && l.Invoice.Status != InvoiceStatus.Failed
                        && l.Invoice.CancelledAt == null && l.Invoice.CancelsInvoiceId == null
                        && (propertyId == null || l.Invoice.PropertyId == propertyId))
                    .GroupBy(l => l.Invoice!.PeriodMonth).Select(g => new { g.Key, Sum = g.Sum(l => l.Amount) }).ToListAsync(ct))
                .ToDictionary(g => g.Key, g => -g.Sum);
            tax = TaxEstimate.Calculate(
                counted.GroupBy(i => i.PeriodMonth).ToDictionary(g => g.Key, g => g.Sum(i => i.AmountTotal) + withheld.GetValueOrDefault(g.Key)),
                utilityRows.Where(u => !u.ForwardBilled && u.Amount != 0).GroupBy(u => u.Month).ToDictionary(g => g.Key, g => g.Sum(u => u.Amount)),
                withheld);
        }

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
            upcoming.OrderBy(e => e.Date).ThenBy(e => e.PropertyName).ToList(),
            allProperties,
            propertyId,
            propertyId is null ? null : living.OrderByDescending(t => t.MoveInDate).FirstOrDefault()?.Name,
            balanceExpected, balancePaid, balanceByProperty, tax));
    }

    private static string NormalizeLabel(string label)
    {
        var trimmed = (label ?? "").Trim();
        return trimmed.Length == 0 ? OtherCategory : trimmed;
    }
}
