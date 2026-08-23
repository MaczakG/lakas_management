using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record TenantDto(Guid Id, string Name, string? Email, string? Phone, Guid? PropertyId, string? PropertyName,
    DateOnly? MoveInDate, DateOnly? MoveOutDate, string? Notes, RentCurrency RentCurrency);

public record UpsertTenantRequest(string Name, string? Email, string? Phone, Guid? PropertyId,
    DateOnly? MoveInDate, DateOnly? MoveOutDate, string? Notes, RentCurrency RentCurrency);
