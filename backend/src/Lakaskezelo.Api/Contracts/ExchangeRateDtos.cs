namespace Lakaskezelo.Api.Contracts;

// Name: a deviza magyar neve (pl. „euró”). IsDefault: alap deviza (EUR, USD, GBP), nem törölhető.
public record ExchangeRateDto(Guid Id, string CurrencyCode, decimal RateToHuf, DateOnly RateDate, DateTime FetchedAt, string Name = "", bool IsDefault = false);

// Felvehető deviza: az MNB most közöl rá árfolyamot, de még nincs követve.
public record AvailableCurrencyDto(string CurrencyCode, string Name, decimal RateToHuf, DateOnly RateDate);

public record AddCurrencyRequest(string CurrencyCode);
