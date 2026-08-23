namespace Lakaskezelo.Api.Contracts;

public record ExchangeRateDto(Guid Id, string CurrencyCode, decimal RateToHuf, DateOnly RateDate, DateTime FetchedAt);
