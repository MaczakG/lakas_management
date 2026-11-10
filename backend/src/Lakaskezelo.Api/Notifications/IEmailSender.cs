namespace Lakaskezelo.Api.Notifications;

public record EmailAttachment(string FileName, byte[] Content, string MimeType);

public interface IEmailSender
{
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);
    // Diagnosztikai célra — az utolsó hiba oka, a Beállítások oldal "Kapcsolat tesztelése" gombjához.
    string? LastError { get; }
    Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, EmailAttachment? attachment, CancellationToken ct) =>
        SendWithAttachmentsAsync(to, subject, htmlBody, textBody, attachment is null ? [] : [attachment], ct);
    // Több csatolmánnyal (pl. a bizonylat PDF-je és a rezsitételek csatolmányai).
    Task<bool> SendWithAttachmentsAsync(string to, string subject, string htmlBody, string textBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken ct);
}
