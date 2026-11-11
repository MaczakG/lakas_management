namespace Lakaskezelo.Domain.Enums;

// A bérlőnél beállítható, hogy a bérleti díj milyen devizanemben van megadva (az Ingatlan
// RentAmount mezőjének egysége) — a számlán ettől függetlenül mindig HUF összeg jelenik meg,
// az aktuális MNB árfolyamon átváltva (ld. Billing/InvoiceGenerationService).
//
// A forinton kívül az MNB által közölt devizák; a számértékek az adatbázisban tárolódnak, ezért nem
// változhatnak (újat csak a végére). Hogy közülük melyik választható, azt az Árfolyamok oldalon felvett
// devizák döntik el (ExchangeRates tábla) — alapból EUR, USD és GBP.
public enum RentCurrency
{
    HUF = 0,
    EUR = 1,
    USD = 2,
    GBP = 3,
    AUD = 4,
    BRL = 5,
    CAD = 6,
    CHF = 7,
    CNY = 8,
    CZK = 9,
    DKK = 10,
    HKD = 11,
    IDR = 12,
    ILS = 13,
    INR = 14,
    ISK = 15,
    JPY = 16,
    KRW = 17,
    MXN = 18,
    MYR = 19,
    NOK = 20,
    NZD = 21,
    PHP = 22,
    PLN = 23,
    RON = 24,
    RSD = 25,
    RUB = 26,
    SEK = 27,
    SGD = 28,
    THB = 29,
    TRY = 30,
    UAH = 31,
    ZAR = 32,
}
