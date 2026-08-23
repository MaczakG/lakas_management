namespace Lakaskezelo.Domain.Entities;

// Ingatlan
public class Property
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // cím / megnevezés
    public decimal RentAmount { get; set; }
    public string? DriveFolderId { get; set; }

    // Havonta ismétlődő számlázási időpont ehhez az ingatlanhoz (helyi idő, Europe/Budapest)
    public int BillingDayOfMonth { get; set; } = 5;
    public int BillingHour { get; set; } = 8;
    public int BillingMinute { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public List<PropertyOwner> PropertyOwners { get; set; } = [];
    public List<Tenant> Tenants { get; set; } = [];
    public List<UtilityCostEntry> UtilityCostEntries { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
}
