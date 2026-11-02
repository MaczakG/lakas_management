using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

// A cég Beállítások oldala: rezsi-emlékeztető, számlakibocsátó adatai, számla e-mail szövege. (Az e-mail
// küldés és a számla-tárhely platform-szintű — PlatformDeliveryDto.)
public record CompanySettingsDto(
    string? UtilityContactEmail, int UtilityDeadlineDay,
    string? IssuerName, string? IssuerAddress, string? IssuerTaxId, string? IssuerBankAccount, string? IssuerEmail,
    string? InvoiceEmailSubject, string? InvoiceEmailBody);

// A platform kézbesítési beállításai (Platform admin → E-mail és tárhely). A titkos mezők (MailgunApiKey,
// SmtpPassword) csak befelé jönnek: a GET üresen adja vissza őket, a *Set jelzi, van-e eltárolt érték;
// mentéskor az üres érték a meglévőt megtartja.
public record PlatformDeliveryDto(
    EmailProvider EmailProvider,
    string? MailgunApiKey, string? MailgunDomain, string? MailgunFromAddress, string? MailgunFromName, string? MailgunApiBaseUrl,
    string? SmtpHost, int? SmtpPort, string? SmtpUsername, string? SmtpPassword, string? SmtpFromAddress, string? SmtpFromName,
    string? S3BucketName, string? S3Region,
    bool MailgunApiKeySet = false, bool SmtpPasswordSet = false);

public record TestMailgunResponse(bool Success, string? Error);
public record TestStorageResponse(bool Success, string? Error);
public record TestSmtpResponse(bool Success, string? Error);
public record TestEmailRequest(string To);
