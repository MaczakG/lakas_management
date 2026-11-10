namespace Lakaskezelo.Domain.Entities;

// Rezsi tétel: az ingatlan havonta ismétlődő rezsije (pl. Közös költség, Áram, Internet). Ezekből készül el a
// hónap rezsije (a „csomag”) a bizonylat előtt: gombnyomásra, a rezsi-határnapon vagy legkésőbb a bizonylat
// kiállításakor (ld. Billing.UtilityPeriods).
public class UtilityItem : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public string Label { get; set; } = string.Empty;
    // Fix tétel: minden hónapban ezzel az összeggel kerül a hónap rezsijébe. Nem fix tételnél az összeget
    // havonta kell kitölteni (itt nincs összeg).
    public bool IsFixed { get; set; }
    public decimal? Amount { get; set; }
    // Tovább számlázandó: a bérleti díj már tartalmazza, ezért nem kerül külön sorként a bérlő bizonylatára; csak a
    // tulajdonossal való elszámolásban szerepel. Ha hamis, a tétel külön sorként rákerül a bérlő bizonylatára.
    public bool ForwardBilled { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Előkészített rezsi-hónap: jelzi, hogy az ingatlan adott hónapjára a rezsi tételekből már elkészültek a havi
// tételek. Az automatikus előkészítés hónaponként egyszer fut, így a kézzel törölt tétel nem jön vissza magától.
public class UtilityPeriod : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime PreparedAt { get; set; }
}
