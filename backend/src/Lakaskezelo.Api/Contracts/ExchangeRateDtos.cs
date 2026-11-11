namespace Lakaskezelo.Api.Contracts;

// Name: a deviza magyar neve (pl. „euró”).
public record ExchangeRateDto(Guid Id, string CurrencyCode, decimal RateToHuf, DateOnly RateDate, DateTime FetchedAt, string Name = "");

// Felvehető deviza: az MNB most közöl rá árfolyamot, de még nincs követve.
public record AvailableCurrencyDto(string CurrencyCode, string Name, decimal RateToHuf, DateOnly RateDate);

public record AddCurrencyRequest(string CurrencyCode);
