namespace Lakaskezelo.Domain.Entities;

// Egy deviza aktuális MNB középárfolyama HUF-ban — devizánként egyetlen sor, amit az óránkénti
// automatikus lekérdezés (ld. Api/ExchangeRates/ExchangeRateUpdater) mindig felülír. Előzmény nem
// kell: a kiállított számla a felhasznált árfolyamot és dátumát a saját szövegében őrzi.
public class ExchangeRate
{
    public Guid Id { get; set; }
    public string CurrencyCode { get; set; } = string.Empty; // "EUR", "USD"
    public decimal RateToHuf { get; set; }
    public DateOnly RateDate { get; set; } // az MNB árfolyam érvényességi napja (hétvégén a legutóbbi banki nap)
    public DateTime FetchedAt { get; set; } // az utolsó sikeres lekérdezés ideje
}
