using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Domain.Entities;

// Cégenként egy sor — a cég futásidejű beállításai (Beállítások oldal), nem appsettings/env-ből
// jönnek, mert a felhasználó a felületen szerkeszti őket.
public class AppSettings : ICompanyOwned
{
    // A SaaS előtti egyetlen beállítás-sor azonosítója (az alapértelmezett cégé lett).
    public static readonly Guid LegacySingletonId = new("11111111-1111-1111-1111-111111111111");

    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    // Melyik szolgáltató küldi ténylegesen a rendszer e-mailjeit — a másik konfigja megmarad,
    // csak nincs használatban (ld. Notifications/EmailSenderRouter).
    public EmailProvider EmailProvider { get; set; } = EmailProvider.Mailgun;

    // ---------- Mailgun ----------
    public string? MailgunApiKey { get; set; }
    public string? MailgunDomain { get; set; }
    public string? MailgunFromAddress { get; set; }
    public string? MailgunFromName { get; set; }
    // US régió: https://api.mailgun.net (alapértelmezett, ha üres); EU domainhez:
    // https://api.eu.mailgun.net.
    public string? MailgunApiBaseUrl { get; set; }

    // ---------- SMTP (pl. a workspace/Google Workspace SMTP-szervere) ----------
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName { get; set; }

    // ---------- Számla-PDF tárhely (Amazon S3) ----------
    // Hozzáférési kulcsot szándékosan nem tárolunk: élesben az EC2-höz rendelt IAM-szerep, helyben
    // a szokásos AWS hitelesítési lánc (~/.aws, környezeti változók) adja a jogot.
    public string? S3BucketName { get; set; }
    public string? S3Region { get; set; }

    // ---------- Rezsi emlékeztető ----------
    public string? UtilityContactEmail { get; set; }
    public int UtilityDeadlineDay { get; set; } = 5;

    // ---------- Számla-kibocsátó adatai ----------
    public string? IssuerName { get; set; }
    public string? IssuerAddress { get; set; }
    public string? IssuerTaxId { get; set; }
    public string? IssuerBankAccount { get; set; }

    // ---------- Számla e-mail szövege (üresen az InvoiceGenerationService beépített alapértéke érvényes) ----------
    public string? InvoiceEmailSubject { get; set; }
    public string? InvoiceEmailBody { get; set; }

    public DateTime UpdatedAt { get; set; }
}
