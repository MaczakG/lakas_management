namespace Lakaskezelo.Domain.Entities;

// Ingatlan
public class Property
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty; // cím / megnevezés
    public decimal RentAmount { get; set; }

    // A számla sorszámának előtagja — a végleges szám "{Előtag}-{hónap}-{év}" alakban épül fel
    // (ld. InvoiceGenerationService), pl. "A22-8-2026".
    public string? InvoicePrefix { get; set; }

    // Havonta ismétlődő számlázási időpont ehhez az ingatlanhoz (helyi idő, Europe/Budapest)
    public int BillingDayOfMonth { get; set; } = 5;
    public int BillingHour { get; set; } = 8;
    public int BillingMinute { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // ---------- Adatlap (property-detail.html) — mind opcionális ----------
    public string? PropertyType { get; set; } // Lakás, Családi ház, Iroda, Üzlethelyiség, Garázs, Egyéb
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Street { get; set; }       // utca, házszám
    public string? FloorDoor { get; set; }    // pl. "4. em. 22."
    public string? Hrsz { get; set; }         // helyrajzi szám
    public decimal? FloorArea { get; set; }   // m²
    public decimal? Rooms { get; set; }       // fél szoba: 0,5
    public int? BuildYear { get; set; }
    public string? ElectricityMeter { get; set; } // mérőóra-gyári számok
    public string? GasMeter { get; set; }
    public string? WaterMeter { get; set; }
    public decimal? Deposit { get; set; }     // kaució, Ft
    public string? Notes { get; set; }

    public List<PropertyOwner> PropertyOwners { get; set; } = [];
    public List<Tenant> Tenants { get; set; } = [];
    public List<UtilityCostEntry> UtilityCostEntries { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
}
