namespace Lakaskezelo.Domain.Enums;

// A bérlőnél beállítható, hogy a bérleti díj milyen devizanemben van megadva (az Ingatlan
// RentAmount mezőjének egysége) — a számlán ettől függetlenül mindig HUF összeg jelenik meg,
// az aktuális MNB árfolyamon átváltva (ld. Billing/InvoiceGenerationService).
public enum RentCurrency
{
    HUF = 0,
    EUR = 1,
    USD = 2,
}
