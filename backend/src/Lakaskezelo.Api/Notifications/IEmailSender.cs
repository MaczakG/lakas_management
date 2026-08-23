namespace Lakaskezelo.Api.Notifications;

public record EmailAttachment(string FileName, byte[] Content, string MimeType);

public interface IEmailSender
{
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);
    // Diagnosztikai célra — az utolsó hiba oka, a Beállítások oldal "Kapcsolat tesztelése" gombjához.
    string? LastError { get; }
    Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, EmailAttachment? attachment, CancellationToken ct);
}
