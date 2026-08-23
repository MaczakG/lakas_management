namespace Lakaskezelo.Domain.Entities;

// Egyetlen sorból álló tábla — a rendszer futásidejű beállításai (Beállítások oldal), nem
// appsettings/env-ből jönnek, mert a felhasználó a felületen akarja szerkeszteni őket.
public class AppSettings
{
    // Fix Id, mindig ugyanaz az egy sor — nincs több "beállítás-készlet".
    public static readonly Guid SingletonId = new("11111111-1111-1111-1111-111111111111");

    public Guid Id { get; set; } = SingletonId;

    // ---------- Mailgun ----------
    public string? MailgunApiKey { get; set; }
    public string? MailgunDomain { get; set; }
    public string? MailgunFromAddress { get; set; }
    public string? MailgunFromName { get; set; }
    // US régió: https://api.mailgun.net (alapértelmezett, ha üres); EU domainhez:
    // https://api.eu.mailgun.net.
    public string? MailgunApiBaseUrl { get; set; }

    // ---------- Google Drive (service account) ----------
    public string? GoogleServiceAccountJson { get; set; }

    // ---------- Rezsi emlékeztető ----------
    public string? UtilityContactEmail { get; set; }
    public int UtilityDeadlineDay { get; set; } = 5;

    // ---------- Számla-kibocsátó adatai ----------
    public string? IssuerName { get; set; }
    public string? IssuerAddress { get; set; }
    public string? IssuerTaxId { get; set; }
    public string? IssuerBankAccount { get; set; }
    public int NextInvoiceNumber { get; set; } = 1;

    public DateTime UpdatedAt { get; set; }
}
