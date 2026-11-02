namespace Lakaskezelo.Api.Contracts;

public record OwnerDto(Guid Id, string Name, string? Email, string? Phone, string? TaxId, string? BankAccount, string? Address, string? Notes, List<string> PropertyNames);
// A tulajdonos adatlapjához: az ingatlanai a tulajdoni hányaddal és a jelenlegi bérlővel.
public record OwnerPropertyDto(Guid PropertyId, string Name, string? Share, bool IsActive, decimal RentAmount, string? TenantName, string? City);
public record UpsertOwnerRequest(string Name, string? Email, string? Phone, string? TaxId, string? BankAccount, string? Address, string? Notes);
