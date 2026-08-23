using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

// Bérlő
public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Guid? PropertyId { get; set; }
    public Property? Property { get; set; }
    public DateOnly? MoveInDate { get; set; }
    public DateOnly? MoveOutDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    // Milyen devizanemben van megadva az ingatlan bérleti díja ennél a bérlőnél — a számlán
    // mindig HUF-ra átváltva jelenik meg az aktuális MNB árfolyamon (ld. InvoiceGenerationService).
    public RentCurrency RentCurrency { get; set; } = RentCurrency.HUF;
}
