using System.Globalization;
using System.Text.Json;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// Felhasználói napló (Beállítások → Napló): ki, mikor, mit csinált, a legfrissebb elöl, lapozva. A sorokat a mentés
// rögzíti (ld. AuditTrail); itt csak a megjelenítés van: magyar típus- és mezőnevek, olvasható értékek (dátum, összeg,
// igen/nem), és az azonosítók helyett a hivatkozott ingatlan, bérlő, bizonylat stb. neve.
[Authorize(Roles = UsersController.AdminRole)]
[ApiController]
[Route("api/audit-log")]
public class AuditLogController(LakaskezeloDbContext db) : ControllerBase
{
    private const int PageSize = 100;

    public record ChangeDto(string Field, string? Old, string? New);
    public record EntryDto(Guid Id, DateTime At, Guid? UserId, string User, string Action, string ActionLabel, string EntityType,
        string EntityTypeLabel, Guid? EntityId, string? EntityLabel, Guid? PropertyId, string? PropertyName, List<ChangeDto> Changes);
    public record PageDto(List<EntryDto> Entries, bool HasMore, Dictionary<string, string> Types);

    // skip: az eddig betöltött sorok száma (a „Továbbiak” gomb a következő lapot kéri).
    // userId = Guid.Empty: a rendszer (felhasználó nélküli) sorai.
    [HttpGet]
    public async Task<ActionResult<PageDto>> List([FromQuery] int skip, [FromQuery] Guid? userId, [FromQuery] string? type,
        [FromQuery] Guid? propertyId, CancellationToken ct)
    {
        var query = db.AuditEntries.AsNoTracking().AsQueryable();
        if (userId == Guid.Empty) query = query.Where(a => a.UserId == null);
        else if (userId is { } u) query = query.Where(a => a.UserId == u);
        if (!string.IsNullOrEmpty(type)) query = query.Where(a => a.EntityType == type);
        if (propertyId is { } p) query = query.Where(a => a.PropertyId == p);
        var rows = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Skip(Math.Max(skip, 0)).Take(PageSize + 1).ToListAsync(ct);
        var hasMore = rows.Count > PageSize;
        if (hasMore) rows.RemoveAt(PageSize);

        var parsed = rows.ToDictionary(r => r.Id, r => r.Changes is null ? [] : JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(r.Changes) ?? []);
        var names = await NamesAsync(rows, parsed, ct);

        var entries = rows.Select(r => new EntryDto(
            r.Id, r.At, r.UserId, r.UserName ?? (r.UserId is null ? "Rendszer" : "Ismeretlen felhasználó"), r.Action,
            AuditLabels.Actions.GetValueOrDefault(r.Action, r.Action), r.EntityType, AuditLabels.Types.GetValueOrDefault(r.EntityType, r.EntityType),
            r.EntityId, r.EntityLabel, r.PropertyId, r.PropertyId is { } pid ? names.GetValueOrDefault(pid) : null,
            parsed[r.Id].Select(c => Change(c, names)).ToList())).ToList();
        return Ok(new PageDto(entries, hasMore, AuditLabels.Types));
    }

    private static ChangeDto Change(Dictionary<string, string?> c, Dictionary<Guid, string> names)
    {
        var field = c.GetValueOrDefault("f") ?? "";
        return new(AuditLabels.Fields.GetValueOrDefault(field, field), Value(field, c.GetValueOrDefault("o"), names), Value(field, c.GetValueOrDefault("n"), names));
    }

    // A nyers (invariáns) érték olvasható alakja.
    private static string? Value(string field, string? raw, Dictionary<Guid, string> names)
    {
        if (raw is null) return null;
        if (AuditLabels.Values.TryGetValue((field, raw), out var label)) return label;
        if (field.EndsWith("Id") && Guid.TryParse(raw, out var id)) return names.GetValueOrDefault(id, "(törölt)");
        if (AuditLabels.Money.Contains(field) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)) return MoneyFormat.Huf(m);
        if (DateTime.TryParseExact(raw, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dt, DateTimeKind.Utc), BudapestClock.Zone).ToString("yyyy. MM. dd. HH:mm");
        }
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.ToString("yyyy. MM. dd.");
        return raw;
    }

    // A naplóban hivatkozott azonosítók (ingatlan, bérlő, tulajdonos, bizonylat, …) nevei egy-egy lekérdezéssel.
    private async Task<Dictionary<Guid, string>> NamesAsync(List<AuditEntry> rows, Dictionary<Guid, List<Dictionary<string, string?>>> parsed, CancellationToken ct)
    {
        var ids = parsed.Values.SelectMany(cs => cs)
            .Where(c => c.GetValueOrDefault("f")?.EndsWith("Id") == true)
            .SelectMany(c => new[] { c.GetValueOrDefault("o"), c.GetValueOrDefault("n") })
            .Select(v => Guid.TryParse(v, out var g) ? g : Guid.Empty)
            .Concat(rows.Where(r => r.PropertyId is not null).Select(r => r.PropertyId!.Value))
            .Where(g => g != Guid.Empty).ToHashSet();
        var result = new Dictionary<Guid, string>();
        if (ids.Count == 0) return result;
        void Put<T>(IEnumerable<T> items, Func<T, Guid> key, Func<T, string> name) { foreach (var i in items) result.TryAdd(key(i), name(i)); }
        Put(await db.Properties.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(ct), x => x.Id, x => x.Name);
        Put(await db.Tenants.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(ct), x => x.Id, x => x.Name);
        Put(await db.Owners.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(ct), x => x.Id, x => x.Name);
        Put(await db.Invoices.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Number }).ToListAsync(ct), x => x.Id, x => x.Number);
        Put(await db.DocumentTypes.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToListAsync(ct), x => x.Id, x => x.Name);
        Put(await db.UtilityItems.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Label }).ToListAsync(ct), x => x.Id, x => x.Label);
        Put(await db.UtilityCostEntries.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Label, x.Year, x.Month }).ToListAsync(ct),
            x => x.Id, x => $"{x.Label} ({x.Year}. {x.Month:00}.)");
        Put(await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.FullName }).ToListAsync(ct), x => x.Id, x => x.FullName);
        return result;
    }
}

// A napló magyar feliratai. Ami nincs itt, nyers névvel jelenik meg (új mező esetén ide érdemes felvenni).
public static class AuditLabels
{
    public static readonly Dictionary<string, string> Actions = new()
    {
        [AuditActions.Created] = "Létrehozás", [AuditActions.Updated] = "Módosítás", [AuditActions.Deleted] = "Törlés", [AuditActions.Login] = "Bejelentkezés",
    };

    public static readonly Dictionary<string, string> Types = new()
    {
        [nameof(Property)] = "Ingatlan", [nameof(Tenant)] = "Bérlő", [nameof(Owner)] = "Tulajdonos", [nameof(PropertyOwner)] = "Tulajdonos az ingatlanon",
        [nameof(UtilityCostEntry)] = "Havi rezsi", [nameof(UtilityItem)] = "Rezsi tétel", [nameof(MoveInItem)] = "Beköltözési tudnivaló",
        [nameof(Invoice)] = "Bizonylat", [nameof(TenantPayment)] = "Befizetés", [nameof(Document)] = "Dokumentum", [nameof(DocumentType)] = "Dokumentumtípus",
        [nameof(AppSettings)] = "Beállítások", [nameof(User)] = "Felhasználó",
    };

    public static readonly HashSet<string> Money = ["RentAmount", "Deposit", "Amount", "AmountTotal"];

    public static readonly Dictionary<string, string> Fields = new()
    {
        ["Name"] = "Név", ["Email"] = "E-mail", ["Phone"] = "Telefon", ["Address"] = "Cím", ["TaxId"] = "Adószám", ["Notes"] = "Megjegyzés",
        ["Note"] = "Megjegyzés", ["Label"] = "Megnevezés", ["Amount"] = "Összeg", ["IsActive"] = "Aktív", ["FullName"] = "Név", ["Role"] = "Szerepkör",
        ["EmailVerifiedAt"] = "E-mail megerősítve", ["BankAccount"] = "Bankszámlaszám", ["Share"] = "Tulajdoni hányad",
        ["PropertyId"] = "Ingatlan", ["TenantId"] = "Bérlő", ["OwnerId"] = "Tulajdonos", ["InvoiceId"] = "Bizonylat", ["DocumentTypeId"] = "Dokumentumtípus",
        ["UtilityItemId"] = "Rezsi tétel", ["UtilityCostEntryId"] = "Havi rezsi", ["CancelsInvoiceId"] = "Sztornózott bizonylat", ["CancelledByUserId"] = "Sztornózta",
        // ingatlan
        ["RentAmount"] = "Bérleti díj", ["InvoicePrefix"] = "Bizonylat előtag", ["BillingDayOfMonth"] = "Kiállítás napja", ["BillingHour"] = "Kiküldés órája",
        ["BillingMinute"] = "Kiküldés perce", ["PropertyType"] = "Típus", ["PostalCode"] = "Irányítószám", ["City"] = "Település", ["Street"] = "Utca, házszám",
        ["FloorDoor"] = "Emelet, ajtó", ["Hrsz"] = "Helyrajzi szám", ["FloorArea"] = "Alapterület", ["Rooms"] = "Szobák", ["BuildYear"] = "Építés éve",
        ["ElectricityMeter"] = "Villanyóra száma", ["GasMeter"] = "Gázóra száma", ["WaterMeter"] = "Vízóra száma", ["Deposit"] = "Kaució",
        ["RentVat"] = "Lakbér áfa", ["UtilityVat"] = "Rezsi áfa", ["MoveInInfoSendable"] = "Beköltözési tudnivalók kiküldhetők",
        // bérlő
        ["MoveInDate"] = "Beköltözés", ["MoveOutDate"] = "Kiköltözés", ["LeaseEndDate"] = "Szerződés lejárata", ["RentPaymentTiming"] = "Lakbér fizetése",
        ["UtilityBillingTiming"] = "Rezsi a bizonylaton", ["TenantPaysSzja"] = "A bérlő vonja le az SZJA-t", ["InvoiceAttachments"] = "Mellékletek a bizonylathoz",
        ["MoveInInfoSentAt"] = "Beköltözési tudnivalók kiküldve", ["RentCurrency"] = "Díj devizája",
        // havi rezsi, rezsi tétel, beköltözés
        ["Year"] = "Év", ["Month"] = "Hónap", ["ExcludedFromBilling"] = "Nem kerül bizonylatra", ["ForwardBilled"] = "Tovább számlázandó",
        ["IsFixed"] = "Fix", ["InChecklist"] = "Jegyzőkönyvre",
        // bizonylat, befizetés
        ["PeriodYear"] = "Időszak éve", ["PeriodMonth"] = "Időszak hónapja", ["Number"] = "Szám", ["IssuedAt"] = "Kiállítva", ["DueDate"] = "Fizetési határidő",
        ["AmountTotal"] = "Végösszeg", ["RentConversionNote"] = "Átváltási megjegyzés", ["Status"] = "Állapot", ["SentAt"] = "Kiküldve",
        ["ErrorMessage"] = "Hibaüzenet", ["Provider"] = "Számlázó", ["CancelledAt"] = "Sztornózva", ["Date"] = "Dátum",
        // dokumentum
        ["FileName"] = "Fájl",
        // beállítások
        ["EmailProvider"] = "E-mail küldő", ["MailgunApiKey"] = "Mailgun API-kulcs", ["MailgunDomain"] = "Mailgun domain", ["MailgunFromAddress"] = "Feladó címe (Mailgun)",
        ["MailgunFromName"] = "Feladó neve (Mailgun)", ["MailgunApiBaseUrl"] = "Mailgun API címe", ["SmtpHost"] = "SMTP szerver", ["SmtpPort"] = "SMTP port",
        ["SmtpUsername"] = "SMTP felhasználó", ["SmtpPassword"] = "SMTP jelszó", ["SmtpFromAddress"] = "Feladó címe (SMTP)", ["SmtpFromName"] = "Feladó neve (SMTP)",
        ["UtilityContactEmail"] = "Rezsi kontakt e-mail", ["UtilityDeadlineDay"] = "Rezsi rögzítési határidő (nap)", ["LeaseExpiryEmails"] = "Szerződéslejárat-értesítés címei",
        ["LeaseExpiryNoticeDays"] = "Szerződéslejárat-értesítés (nap)", ["IssuerName"] = "Kiállító neve", ["IssuerAddress"] = "Kiállító címe",
        ["IssuerTaxId"] = "Kiállító adószáma", ["IssuerBankAccount"] = "Kiállító bankszámlája", ["IssuerEmail"] = "Kiállító e-mail címe",
        ["InvoiceEmailSubject"] = "Bizonylat-levél tárgya", ["InvoiceEmailBody"] = "Bizonylat-levél szövege", ["InvoiceProvider"] = "Számlázó szolgáltatás",
        ["BillingoApiKey"] = "Billingo API-kulcs", ["BillingoBlockId"] = "Billingo számlatömb", ["SzamlazzAgentKey"] = "Számlázz.hu kulcs",
        ["SzamlazzInvoicePrefix"] = "Számlázz.hu előtag", ["DefaultRentVat"] = "Alapértelmezett lakbér áfa", ["DefaultUtilityVat"] = "Alapértelmezett rezsi áfa",
    };

    public static readonly Dictionary<(string Field, string Value), string> Values = new()
    {
        [("Status", "Generated")] = "Elkészült", [("Status", "Sent")] = "Kiküldve", [("Status", "Failed")] = "Sikertelen",
        [("Role", "Admin")] = "Adminisztrátor", [("Role", "Member")] = "Felhasználó",
        [("UtilityBillingTiming", "SameMonth")] = "Tárgyhavi", [("UtilityBillingTiming", "PreviousMonth")] = "Előző havi",
    };
}
