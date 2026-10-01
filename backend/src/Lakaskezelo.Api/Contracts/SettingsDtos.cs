using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Api.Contracts;

public record AppSettingsDto(
    EmailProvider EmailProvider,
    string? MailgunApiKey, string? MailgunDomain, string? MailgunFromAddress, string? MailgunFromName, string? MailgunApiBaseUrl,
    string? SmtpHost, int? SmtpPort, string? SmtpUsername, string? SmtpPassword, string? SmtpFromAddress, string? SmtpFromName,
    string? S3BucketName, string? S3Region,
    string? UtilityContactEmail, int UtilityDeadlineDay,
    string? IssuerName, string? IssuerAddress, string? IssuerTaxId, string? IssuerBankAccount,
    string? InvoiceEmailSubject, string? InvoiceEmailBody);

public record TestMailgunResponse(bool Success, string? Error);
public record TestStorageResponse(bool Success, string? Error);
public record TestSmtpResponse(bool Success, string? Error);
public record TestEmailRequest(string To);
