namespace Lakaskezelo.Api;

// Kézzel bővítendő, dátum-alapú verziójelző — minden érdemi élesítéskor érdemes frissíteni, hogy a
// sidebar lábjegyzetében (ld. frontend/nav.js) és a /api/version végponton keresztül látszódjon,
// pontosan melyik változat fut éppen. Nincs automatikus (git commit alapú) verziózás, mert az AWS
// EC2-re tar-ral másolt forrásnak nincs .git előzménye a build kontextusban (ld. deploy/README.md).
public static class AppVersion
{
    public const string Current = "2026.11.12.3";

    // A kiadás neve (pl. „1.1”): ez látszik a menü alján „V1.1” alakban. A fenti, dátum-alapú jelző a pontos
    // változatot azonosítja (a menüben a kiadás fölé állva, és a tesztkörnyezet sávjában látszik).
    public const string Release = "1.1";
}
