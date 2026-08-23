namespace Lakaskezelo.Domain.Enums;

// Melyik szolgáltatón keresztül menjenek ki a rendszer e-mailjei (számlák, rezsi-emlékeztető,
// teszt e-mail) — mindkettő beállítható marad egyszerre a Beállítások oldalon, csak az egyik
// aktív ténylegesen küldéskor (ld. Notifications/EmailSenderRouter).
public enum EmailProvider
{
    Mailgun = 0,
    Google = 1,
}
