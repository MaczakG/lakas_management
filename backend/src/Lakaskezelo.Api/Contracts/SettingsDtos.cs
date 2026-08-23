namespace Lakaskezelo.Api.Contracts;

public record AppSettingsDto(
    string? MailgunApiKey, string? MailgunDomain, string? MailgunFromAddress, string? MailgunFromName, string? MailgunApiBaseUrl,
    string? GoogleServiceAccountJson,
    string? UtilityContactEmail, int UtilityDeadlineDay,
    string? IssuerName, string? IssuerAddress, string? IssuerTaxId, string? IssuerBankAccount, int NextInvoiceNumber);

public record TestMailgunResponse(bool Success, string? Error);
public record TestDriveResponse(bool Success, string? ServiceAccountEmail, string? Error);
public record TestEmailRequest(string To);
