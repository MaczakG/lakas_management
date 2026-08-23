namespace Lakaskezelo.Domain.Entities;

// Egy nap egy devizájának MNB középárfolyama HUF-ban — a napi automatikus lekérdezés (ld.
// ExchangeRates/MnbExchangeRateClient) tölti fel, egy sor devizánként/naponta.
public class ExchangeRate
{
    public Guid Id { get; set; }
    public string CurrencyCode { get; set; } = string.Empty; // "EUR", "USD"
    public decimal RateToHuf { get; set; }
    public DateOnly RateDate { get; set; }
    public DateTime FetchedAt { get; set; }
}
