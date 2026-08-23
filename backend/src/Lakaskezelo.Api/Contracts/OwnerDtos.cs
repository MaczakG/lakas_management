namespace Lakaskezelo.Api.Contracts;

public record OwnerDto(Guid Id, string Name, string? Email, string? Phone, string? TaxId, string? BankAccount, string? Address, string? Notes, List<string> PropertyNames);
public record UpsertOwnerRequest(string Name, string? Email, string? Phone, string? TaxId, string? BankAccount, string? Address, string? Notes);
