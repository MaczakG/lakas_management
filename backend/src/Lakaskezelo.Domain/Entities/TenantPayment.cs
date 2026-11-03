namespace Lakaskezelo.Domain.Entities;

// Egyenlegkövetés: a bérlő befizetése (vagy egyenleg-korrekció, ha negatív). A bérlő egyenlege = a neki kiállított
// bizonylatok (a sztornók negatívak) összege − a befizetések összege.
public class TenantPayment : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
