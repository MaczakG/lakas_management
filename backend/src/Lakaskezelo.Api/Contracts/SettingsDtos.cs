using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

// A titkos mezők (MailgunApiKey, SmtpPassword) csak befelé, mentéskor jönnek: a GET üresen adja
// vissza őket, és csak a *Set jelzi, hogy van-e eltárolt érték. Mentéskor az üres érték a
// meglévőt megtartja (ld. SettingsController).
public record AppSettingsDto(
    EmailProvider EmailProvider,
    string? MailgunApiKey, string? MailgunDomain, string? MailgunFromAddress, string? MailgunFromName, string? MailgunApiBaseUrl,
    string? SmtpHost, int? SmtpPort, string? SmtpUsername, string? SmtpPassword, string? SmtpFromAddress, string? SmtpFromName,
    string? S3BucketName, string? S3Region,
    string? UtilityContactEmail, int UtilityDeadlineDay,
    string? IssuerName, string? IssuerAddress, string? IssuerTaxId, string? IssuerBankAccount,
    string? InvoiceEmailSubject, string? InvoiceEmailBody,
    bool MailgunApiKeySet = false, bool SmtpPasswordSet = false);

public record TestMailgunResponse(bool Success, string? Error);
public record TestStorageResponse(bool Success, string? Error);
public record TestSmtpResponse(bool Success, string? Error);
public record TestEmailRequest(string To);
