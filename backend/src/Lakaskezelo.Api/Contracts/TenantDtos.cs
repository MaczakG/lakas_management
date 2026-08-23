using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record TenantDto(Guid Id, string Name, string? Email, string? Phone, string? Address, string? TaxId,
    Guid? PropertyId, string? PropertyName,
    DateOnly? MoveInDate, DateOnly? MoveOutDate, string? Notes, RentCurrency RentCurrency);

public record UpsertTenantRequest(string Name, string? Email, string? Phone, string? Address, string? TaxId,
    Guid? PropertyId, DateOnly? MoveInDate, DateOnly? MoveOutDate, string? Notes, RentCurrency RentCurrency);
