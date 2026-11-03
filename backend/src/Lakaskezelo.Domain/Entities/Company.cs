namespace Lakaskezelo.Domain.Entities;

// Előfizető cég (SaaS-bérlő). Minden üzleti adat (ingatlan, bérlő, számla, felhasználó, beállítás) egy
// céghez tartozik (ICompanyOwned), és a felhasználó csak a saját cége adatait látja — ezt az
// adatbázis-réteg globális szűrője biztosítja (ld. LakaskezeloDbContext).
public class Company
{
    // A SaaS előtti, egyetlen cég adatai ide kerültek át a CompanyTenancy migrációval.
    public static readonly Guid DefaultCompanyId = new("d0000000-0000-4000-8000-000000000001");

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Regisztrációkor elfogadott ÁSZF / adatkezelési tájékoztató változata és ideje.
    public string? TermsVersion { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }

    // Létrejöttek-e már a cég alapértelmezett dokumentumtípusai (az első lekéréskor jönnek létre; ha a cég
    // később mindet törli, nem jönnek vissza).
    public bool DocumentTypesInitialized { get; set; }
}
