using Lakaskezelo.Api.Billing;

namespace Lakaskezelo.Tests;

public class UtilityPeriodsTests
{
    [Theory]
    // Se rezsi tétel, se rögzített tétel: a rezsit kézzel kell felvenni.
    [InlineData(0, 0, false, 0, 0, UtilityPeriods.Missing)]
    // Kézzel rögzített tételek (rezsi tételek nélkül): rendben, mint eddig.
    [InlineData(2, 0, false, 0, 0, UtilityPeriods.Ok)]
    // Még nincs előkészítve, de csak fix rezsi tételek vannak: maguktól bekerülnek.
    [InlineData(0, 0, false, 3, 0, UtilityPeriods.Ok)]
    // Még nincs előkészítve, és nem fix tétel is van: azt ki kell majd tölteni.
    [InlineData(0, 0, false, 3, 1, UtilityPeriods.Pending)]
    // Előkészítve, van kitöltendő tétel.
    [InlineData(3, 1, true, 3, 1, UtilityPeriods.Pending)]
    // Előkészítve, minden kitöltve.
    [InlineData(3, 0, true, 3, 1, UtilityPeriods.Ok)]
    // Előkészítve, de minden tételt töröltek: nincs mit számlázni, nem hiányzik semmi.
    [InlineData(0, 0, true, 3, 1, UtilityPeriods.Ok)]
    // Kézzel felvett, de még összeg nélküli tétel is kitöltendő.
    [InlineData(1, 1, false, 0, 0, UtilityPeriods.Pending)]
    public void State_of_a_month(int entries, int pending, bool prepared, int items, int nonFixed, string expected)
    {
        Assert.Equal(expected, UtilityPeriods.State(entries, pending, prepared, items, nonFixed));
    }
}
