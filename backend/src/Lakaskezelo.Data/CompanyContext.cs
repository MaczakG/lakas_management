namespace Lakaskezelo.Data;

// Melyik cég (előfizető) nevében fut az aktuális kérés vagy háttérfeladat. HTTP-kérésben a
// JwtBearer OnTokenValidated tölti ki a bejelentkezett felhasználó cégével (ld. Program.cs); a
// háttérszolgáltatások cégenként maguk állítják be. Ha nincs beállítva, a cégekhez tartozó adatokból
// semmi sem látszik (a globális szűrő "zárva" hibázik).
public interface ICompanyContext
{
    Guid? CompanyId { get; }
    void Set(Guid? companyId);
}

public class CompanyContext : ICompanyContext
{
    public Guid? CompanyId { get; private set; }
    public void Set(Guid? companyId) => CompanyId = companyId;
}
