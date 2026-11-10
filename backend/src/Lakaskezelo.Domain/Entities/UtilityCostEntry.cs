namespace Lakaskezelo.Domain.Entities;

// Rezsi tétel — egy adott ingatlan adott havi rezsi-sora (pl. Áram, Gáz, Közös költség)
public class UtilityCostEntry : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    // Nem kötelező belső megjegyzés (pl. mérőállás, a szolgáltatói számla száma). A bizonylatra nem kerül rá.
    public string? Note { get; set; }
    // A bizonylat, amelyre a tétel rákerült. Amíg ez aktív (nem sztornózták), a tétel zárolt; sztornóval felszabadul,
    // és a következő bizonylatra kerül. Késői (utólag rögzített, korábbi hónapra szóló) tétel is így kerül a következő
    // bizonylatra, legfeljebb 36 hónapra visszamenőleg.
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    // Az egyenlegkövetés bevezetése előtti, korábbi hónapra szóló, bizonylathoz nem köthető tétel: nem kerül
    // automatikusan bizonylatra (nehogy egy régi, máshol már elszámolt tétel hirtelen felbukkanjon).
    public bool ExcludedFromBilling { get; set; }
    // Ha a tétel az ingatlan egyik rezsi tételéből (UtilityItem) készült a hónap előkészítésekor; egyedi (kézzel
    // felvett) tételnél null. Nem fix rezsi tételből 0 összeggel jön létre: ez „kitöltendő”, és amíg nincs összege,
    // nem kerül bizonylatra.
    public Guid? UtilityItemId { get; set; }
    public UtilityItem? UtilityItem { get; set; }
    // Tovább számlázandó: a bérleti díj már tartalmazza, ezért nem kerül a bérlő bizonylatára (és az egyenlegébe sem);
    // csak a tulajdonossal való elszámolásban szerepel.
    public bool ForwardBilled { get; set; }

    // A tételhez csatolt dokumentumok (Document.UtilityCostEntryId).
    public List<Document> Attachments { get; set; } = [];

    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
