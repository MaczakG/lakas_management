namespace Lakaskezelo.Domain.Enums;

// Kapja-e a bérlő a bizonylat e-mailjével a rezsitételek csatolmányait (pl. a szolgáltatói számlát).
public enum InvoiceAttachmentPreference
{
    Ask = 0,      // nincs beállítva: kézi bizonylatkészítésnél a program rákérdez, az időzített küldés mellékletek nélkül megy
    Send = 1,     // mindig megkapja
    DontSend = 2, // soha nem kapja meg
}
