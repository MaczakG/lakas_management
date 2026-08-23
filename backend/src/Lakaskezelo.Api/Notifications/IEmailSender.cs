namespace Lakaskezelo.Api.Notifications;

public interface IEmailSender
{
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);
    // Diagnosztikai célra — az utolsó hiba oka, a Beállítások oldal "Kapcsolat tesztelése" gombjához.
    string? LastError { get; }
    Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken ct);
}
