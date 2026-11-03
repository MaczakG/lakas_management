using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record TenantDto(Guid Id, string Name, string? Email, string? Phone, string? Address, string? TaxId,
    Guid? PropertyId, string? PropertyName,
    DateOnly? MoveInDate, DateOnly? MoveOutDate, string? Notes, RentCurrency RentCurrency,
    RentPaymentTiming RentPaymentTiming, bool TenantPaysSzja, decimal Balance);

// RentPaymentTiming / TenantPaysSzja elhagyható: ha nincs megadva, a meglévő érték marad (régebbi kliensekhez).
public record UpsertTenantRequest(string Name, string? Email, string? Phone, string? Address, string? TaxId,
    Guid? PropertyId, DateOnly? MoveInDate, DateOnly? MoveOutDate, string? Notes, RentCurrency RentCurrency,
    RentPaymentTiming? RentPaymentTiming = null, bool? TenantPaysSzja = null);

// Egyenleg: a bérlőnek kiállított bizonylatok (a sztornó negatív) és a rögzített befizetések. Pozitív egyenleg = tartozás.
// UnbilledTotal: a felvett, de még bizonylatra nem került rezsitételek (a Balance-ben már benne vannak).
public record TenantLedgerDto(decimal Balance, decimal ChargedTotal, decimal PaidTotal, List<TenantLedgerEntryDto> Entries,
    decimal UnbilledTotal = 0);

// Kind: "invoice" | "payment" | "utility" (bizonylatra még nem került rezsitétel). Amount: a bizonylatnál a terhelés (sztornónál negatív), befizetésnél a befizetett összeg
// negatívan — így a Balance a sorok futó összege.
public record TenantLedgerEntryDto(string Kind, Guid Id, DateOnly Date, string Label, decimal Amount, decimal Balance,
    Guid? InvoiceId, string? InvoiceNumber, bool IsStorno, bool IsCancelled, string? Note);

public record TenantPaymentDto(Guid Id, DateOnly Date, decimal Amount, string? Note, DateTime CreatedAt);

public record CreateTenantPaymentRequest(DateOnly Date, decimal Amount, string? Note);
