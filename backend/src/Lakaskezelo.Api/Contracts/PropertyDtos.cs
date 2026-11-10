using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record PropertyListItemDto(
    Guid Id, string Name, decimal RentAmount, RentCurrency RentCurrency, bool IsActive,
    string OwnerNames, string TenantNames,
    bool UtilityComplete, int BillingDayOfMonth, int BillingHour, int BillingMinute,
    DateTime NextBillingAt, // budapesti helyi idő, munkanapra tolva (ld. Billing.BillingCalendar)
    // A következő kiküldés időszaka — a UtilityComplete erre az időszakra vonatkozik (ennek a rezsije kerül a számlára).
    int NextPeriodYear, int NextPeriodMonth);

public record PropertyDetailDto(
    Guid Id, string Name, decimal RentAmount, string? InvoicePrefix,
    int BillingDayOfMonth, int BillingHour, int BillingMinute, bool IsActive,
    List<PropertyOwnerDto> Owners, List<TenantDto> Tenants,
    PropertySheetDto Sheet, DateTime NextBillingAt, int NextPeriodYear, int NextPeriodMonth);

// Az ingatlan adatlapja (property-detail.html) — mind opcionális.
public record PropertySheetDto(
    string? PropertyType, string? PostalCode, string? City, string? Street, string? FloorDoor, string? Hrsz,
    decimal? FloorArea, decimal? Rooms, int? BuildYear,
    string? ElectricityMeter, string? GasMeter, string? WaterMeter,
    decimal? Deposit, string? Notes,
    // Áfa a számlázóval kiállított számlán (cégnél); null: a cég alapértelmezése.
    string? RentVat = null, string? UtilityVat = null);

public record PropertyOwnerDto(Guid OwnerId, string OwnerName, string? Share);

public record PropertyOwnerInput(Guid OwnerId, string? Share);

public record UpsertPropertyRequest(
    string Name, decimal RentAmount, string? InvoicePrefix,
    int BillingDayOfMonth, int BillingHour, int BillingMinute, bool IsActive,
    List<PropertyOwnerInput> Owners,
    PropertySheetDto? Sheet = null); // null: az adatlap változatlan marad (pl. az "Új ingatlan" dialógus)

public record UtilityCostEntryDto(Guid Id, int Year, int Month, string Label, decimal Amount, DateTime CreatedAt, bool IsLocked,
    Guid? InvoiceId = null, string? InvoiceNumber = null, string? Note = null, List<UtilityAttachmentDto>? Attachments = null);

// A rezsitételhez csatolt dokumentum (pl. a szolgáltatói számla).
public record UtilityAttachmentDto(Guid Id, string Name, string FileName, string ContentType, long SizeBytes);
public record UpsertUtilityCostEntryRequest(int Year, int Month, string Label, decimal Amount, string? Note = null);
public record UpdateUtilityCostEntryRequest(string Label, decimal Amount, string? Note = null);

// Beköltözési tudnivalók (az ingatlan adatlapjának „Beköltözés” füle).
public record MoveInInfoDto(string? Text, bool Sendable, bool EmailConfigured, List<MoveInRecipientDto> Recipients);

// Akinek kiküldhető: az ingatlan jelenlegi vagy még be nem költözött bérlője.
public record MoveInRecipientDto(Guid TenantId, string Name, string? Email, DateOnly? MoveInDate, DateTime? SentAt);

public record UpdateMoveInInfoRequest(string? Text, bool Sendable);

public record SendMoveInInfoRequest(Guid TenantId);
