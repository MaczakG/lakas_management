namespace Lakaskezelo.Api.Billing;

// Melyik napon megy ki ténylegesen egy havi számla: ha az ingatlanon beállított számlázási nap
// hétvégére vagy magyar munkaszüneti napra esik, az azt követő első munkanapon (ugyanabban az
// órában/percben). A számlázott időszak ettől nem változik — pl. a november 1-jei (vasárnap,
// Mindenszentek) számla november 2-án megy ki, de novemberi számla marad.
//
// A munkaszüneti napok a Munka Törvénykönyve szerintiek (a december 24. 2025-től). A kormány által
// évente rendeletben áthelyezett pihenőnapokat (pl. "híd" péntekek) ez nem ismeri — azokon a
// számla a szokásos módon kimegy.
public static class BillingCalendar
{
    public static bool IsWorkday(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsPublicHoliday(date);

    public static bool IsPublicHoliday(DateOnly date)
    {
        var (m, d) = (date.Month, date.Day);
        if ((m, d) is (1, 1) or (3, 15) or (5, 1) or (8, 20) or (10, 23) or (11, 1) or (12, 25) or (12, 26))
        {
            return true;
        }
        if ((m, d) == (12, 24) && date.Year >= 2025)
        {
            return true;
        }

        var easter = EasterSunday(date.Year);
        return date == easter.AddDays(-2) // nagypéntek
            || date == easter.AddDays(1)   // húsvéthétfő
            || date == easter.AddDays(50); // pünkösdhétfő
    }

    // Az adott (év, hónap) időszak számlájának tényleges kiküldési napja: a beállított nap, vagy ha
    // az nem munkanap, az azt követő első munkanap (ez akár a következő hónapba is átcsúszhat).
    public static DateOnly EffectiveBillingDate(int year, int month, int billingDayOfMonth)
    {
        var date = new DateOnly(year, month, Math.Clamp(billingDayOfMonth, 1, DateTime.DaysInMonth(year, month)));
        while (!IsWorkday(date))
        {
            date = date.AddDays(1);
        }
        return date;
    }

    // A következő ütemezett kiküldés (budapesti helyi idő) a megadott időpont után.
    public static DateTime NextBillingAt(DateTime nowLocal, int billingDayOfMonth, int hour, int minute)
    {
        // Az előző hónap számlája is még előttünk lehet, ha a kiküldése átcsúszott ebbe a hónapba.
        var period = new DateOnly(nowLocal.Year, nowLocal.Month, 1).AddMonths(-1);
        while (true)
        {
            var at = EffectiveBillingDate(period.Year, period.Month, billingDayOfMonth).ToDateTime(new TimeOnly(hour, minute));
            if (at > nowLocal) return at;
            period = period.AddMonths(1);
        }
    }

    // Húsvétvasárnap a Gergely-naptárban (anonim gregorián algoritmus, Meeus/Jones/Butcher).
    private static DateOnly EasterSunday(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
