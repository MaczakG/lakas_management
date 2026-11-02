namespace Lakaskezelo.Domain.Entities;

// Egy céghez (előfizetőhöz) tartozó adat. Az adatbázis-réteg ezekre automatikusan a bejelentkezett
// felhasználó cégére szűr, és mentéskor kitölti/ellenőrzi a CompanyId-t (ld. LakaskezeloDbContext).
public interface ICompanyOwned
{
    Guid CompanyId { get; set; }
}
