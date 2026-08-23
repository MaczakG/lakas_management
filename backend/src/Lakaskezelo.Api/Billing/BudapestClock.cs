namespace Lakaskezelo.Api.Billing;

// Az ingatlanonkénti számlázási nap/óra/perc helyi (magyarországi) időben értendő — ez a segéd
// mindkét ütemezett szolgáltatásban (BillingSchedulerService, UtilityReminderService) ugyanazt az
// idézőnket adja vissza. IANA-azonosítóval próbálkozik először (Linux/macOS és .NET 6+ Windows
// ICU-val), Windows-azonosítóval esik vissza, ha a rendszeren nincs meg az IANA adatbázis.
public static class BudapestClock
{
    private static readonly TimeZoneInfo TimeZone = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest"); }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        }
    }

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);
}
