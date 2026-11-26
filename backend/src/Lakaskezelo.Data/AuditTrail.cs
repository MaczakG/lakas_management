using System.Text.Json;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lakaskezelo.Data;

// A felhasználói napló sorai a mentés előtt, a változáskövetőből (ld. AuditEntry). Csak a felhasználó számára érdemi
// adatok kerülnek bele; a származtatott és technikai mezők (pl. a bizonylat fizetettsége, a rezsitétel bizonylathoz
// kötése) nem, a titkos mezőknek (jelszó, API-kulcs) csak a ténye, értéke soha.
public static class AuditTrail
{
    private static readonly HashSet<Type> Audited =
    [
        typeof(Property), typeof(Tenant), typeof(Owner), typeof(PropertyOwner), typeof(UtilityCostEntry), typeof(UtilityItem),
        typeof(MoveInItem), typeof(Invoice), typeof(TenantPayment), typeof(Document), typeof(DocumentType), typeof(AppSettings), typeof(User),
    ];

    private static readonly HashSet<string> Ignored =
    [
        "Id", "CompanyId", "CreatedAt", "UpdatedAt", "CreatedByUserId", "SortOrder",
        "PaidAmount", "PaidAt", "PdfStorageKey", "AttachmentsAvailable", "AttachmentsSent", "ProviderDocumentId",
        "FailedLoginCount", "LockoutEndsAt", "LeaseExpiryNoticeFor", "BillingoPartnerId",
        "CurrentVersion", "SizeBytes", "ContentType", "UploadedAt", "UploadedByUserId",
    ];

    // Per típus: a rezsitétel bizonylathoz kötése a bizonylat kiállításának mellékterméke (a bizonylat külön sor).
    private static readonly HashSet<(Type, string)> IgnoredOn = [(typeof(UtilityCostEntry), "InvoiceId")];

    public static bool IsSecret(string field) =>
        field.Contains("Password") || field.Contains("ApiKey") || field.Contains("AgentKey") || field.Contains("Hash") || field == "SecurityStamp";

    public static List<AuditEntry> Collect(ChangeTracker tracker, Guid? userId, string? userName, DateTime now)
    {
        var result = new List<AuditEntry>();
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            var type = entry.Metadata.ClrType;
            if (!Audited.Contains(type)) continue;
            // A rendszer (felhasználó nélküli) műveletei közül csak a bizonylat érdekes (ütemezett kiállítás, kiküldés).
            if (userId is null && type != typeof(Invoice)) continue;

            var changes = new List<Dictionary<string, string?>>();
            foreach (var p in entry.Properties)
            {
                var name = p.Metadata.Name;
                if (Ignored.Contains(name) || IgnoredOn.Contains((type, name)) || p.Metadata.ClrType == typeof(byte[])) continue;
                string? oldValue = null, newValue = null;
                switch (entry.State)
                {
                    case EntityState.Added:
                        if (p.CurrentValue is null || p.CurrentValue is string { Length: 0 }) continue;
                        newValue = Format(p.CurrentValue);
                        break;
                    case EntityState.Modified:
                        if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue)) continue;
                        (oldValue, newValue) = (Format(p.OriginalValue), Format(p.CurrentValue));
                        break;
                    default:
                        if (p.OriginalValue is null || p.OriginalValue is string { Length: 0 }) continue;
                        oldValue = Format(p.OriginalValue);
                        break;
                }
                if (IsSecret(name)) (oldValue, newValue) = (oldValue is null ? null : "••••", newValue is null ? null : "••••");
                changes.Add(new() { ["f"] = name, ["o"] = oldValue, ["n"] = newValue });
            }
            if (entry.State == EntityState.Modified && changes.Count == 0) continue;

            result.Add(new AuditEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = ((ICompanyOwned)entry.Entity).CompanyId,
                At = now,
                UserId = userId,
                UserName = userName,
                Action = entry.State switch { EntityState.Added => AuditActions.Created, EntityState.Modified => AuditActions.Updated, _ => AuditActions.Deleted },
                EntityType = type.Name,
                EntityId = Value(entry, "Id") as Guid?,
                EntityLabel = Truncate(Label(entry), 300),
                PropertyId = type == typeof(Property) ? Value(entry, "Id") as Guid? : Value(entry, "PropertyId") as Guid?,
                Changes = changes.Count == 0 ? null : JsonSerializer.Serialize(changes),
            });
        }
        return result;
    }

    private static readonly System.Globalization.CultureInfo Hu = System.Globalization.CultureInfo.GetCultureInfo("hu-HU");

    private static object? Value(EntityEntry entry, string name) =>
        entry.Metadata.FindProperty(name) is null ? null
            : entry.State == EntityState.Deleted ? entry.Property(name).OriginalValue : entry.Property(name).CurrentValue;

    // A sor emberi megnevezése: név / szám / megnevezés; a rezsitételnél a hónappal, a befizetésnél az összeggel.
    private static string? Label(EntityEntry entry)
    {
        string? S(string name) => Value(entry, name)?.ToString();
        return entry.Entity switch
        {
            UtilityCostEntry => $"{S("Label")} ({S("Year")}. {Value(entry, "Month"):00}.)",
            TenantPayment => $"{(Value(entry, "Amount") as decimal?)?.ToString("N0", Hu)} Ft ({Value(entry, "Date") as DateOnly?:yyyy. MM. dd.})",
            PropertyOwner => S("Share"),
            AppSettings => null,
            User => S("FullName") ?? S("Email"),
            _ => S("Name") ?? S("Number") ?? S("Label") ?? S("FileName"),
        };
    }

    private static string? Format(object? value) => Truncate(value switch
    {
        null => null,
        bool b => b ? "igen" : "nem",
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        decimal m => m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    }, 500);

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max] + "…";
}
