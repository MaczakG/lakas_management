namespace Lakaskezelo.Data;

// Melyik cég (előfizető) nevében fut az aktuális kérés vagy háttérfeladat. HTTP-kérésben a
// JwtBearer OnTokenValidated tölti ki a bejelentkezett felhasználó cégével (ld. Program.cs); a
// háttérszolgáltatások cégenként maguk állítják be. Ha nincs beállítva, a cégekhez tartozó adatokból
// semmi sem látszik (a globális szűrő "zárva" hibázik). A felhasználó (UserId, UserName) a naplóhoz kell
// (AuditEntry); háttérfeladatban null, ott a rendszer a szereplő.
public interface ICompanyContext
{
    Guid? CompanyId { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    void Set(Guid? companyId);
    void SetUser(Guid? userId, string? userName);
}

public class CompanyContext : ICompanyContext
{
    public Guid? CompanyId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public void Set(Guid? companyId) => CompanyId = companyId;
    public void SetUser(Guid? userId, string? userName) => (UserId, UserName) = (userId, userName);
}
