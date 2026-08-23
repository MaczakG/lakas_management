namespace Lakaskezelo.Domain.Entities;

// Tulajdonos
public class Owner
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? TaxId { get; set; }
    public string? BankAccount { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<PropertyOwner> PropertyOwners { get; set; } = [];
}
