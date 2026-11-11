using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

// Bérlő
public class Tenant : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Guid? PropertyId { get; set; }
    public Property? Property { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public DateOnly? MoveInDate { get; set; }
    public DateOnly? MoveOutDate { get; set; }
    // A bérleti szerződés lejárata; előtte értesítő megy a Beállításokban megadott címekre (ld. LeaseExpiry).
    public DateOnly? LeaseEndDate { get; set; }
    // Melyik lejárati dátumról ment már ki értesítő: ha a lejárat módosul (pl. hosszabbítás), az újról megint szól.
    public DateOnly? LeaseExpiryNoticeFor { get; set; }
    public string? Notes { get; set; }
    // A bérlő partner-azonosítója a cég Billingo-fiókjában (az első kiállított számlakor jön létre).
    public int? BillingoPartnerId { get; set; }

    // A lakbér előre (a bizonylat hónapjára) vagy utólag (az előző hónapra) fizetendő — a bizonylat bérleti díj
    // sorának időszaka ezt követi.
    public RentPaymentTiming RentPaymentTiming { get; set; } = RentPaymentTiming.Advance;
    // A bérlő (kifizetőként) vonja le és fizeti be a bérbeadó SZJA-ját: a bérleti díjból levonódik (magánszemély
    // bérbeadónál; ld. Billing.Szja).
    public bool TenantPaysSzja { get; set; }
    // Megkapja-e a bizonylat e-mailjével a rezsitételek csatolmányait.
    public InvoiceAttachmentPreference InvoiceAttachments { get; set; } = InvoiceAttachmentPreference.Ask;
    public DateTime CreatedAt { get; set; }
    // Mikor kapta meg utoljára e-mailben az ingatlan beköltözési tudnivalóit (ld. Property.MoveInInfo).
    public DateTime? MoveInInfoSentAt { get; set; }

    // Milyen devizanemben van megadva az ingatlan bérleti díja ennél a bérlőnél — a számlán
    // mindig HUF-ra átváltva jelenik meg az aktuális MNB árfolyamon (ld. InvoiceGenerationService).
    public RentCurrency RentCurrency { get; set; } = RentCurrency.HUF;
}
