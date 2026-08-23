using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Lakaskezelo.Api.Settings;

namespace Lakaskezelo.Api.Notifications;

// A Google Drive-hoz csatlakoztatott fiók refresh tokenjét használja fel (ld.
// GoogleOAuthController — a gmail.send scope onnan érkezik), ugyanazzal a
// GoogleAuthorizationCodeFlow + UserCredential mintával, mint a GoogleDriveService. A Gmail API
// nem fogad el sima form-postot, mint a Mailgun — egy base64url-kódolt RFC 2822 MIME üzenetet vár
// a users.messages.send hívásban.
public class GmailEmailSender(AppSettingsService settingsService) : IEmailSender
{
    private static readonly string[] Scopes = [GmailService.Scope.GmailSend];

    public string? LastError { get; private set; }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        return !string.IsNullOrWhiteSpace(settings.GoogleOAuthRefreshToken) && !string.IsNullOrWhiteSpace(settings.GoogleConnectedEmail);
    }

    private async Task<GmailService?> BuildClientAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.GoogleOAuthRefreshToken)
            || string.IsNullOrWhiteSpace(settings.GoogleOAuthClientId)
            || string.IsNullOrWhiteSpace(settings.GoogleOAuthClientSecret))
        {
            return null;
        }

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = settings.GoogleOAuthClientId, ClientSecret = settings.GoogleOAuthClientSecret },
            Scopes = Scopes,
        });

        var token = new TokenResponse { RefreshToken = settings.GoogleOAuthRefreshToken };
        var credential = new UserCredential(flow, "lakaskezelo", token);

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Lakaskezelo",
        });
    }

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.GoogleConnectedEmail))
        {
            LastError = "A Google-fiók nincs csatlakoztatva (Beállítások → Google).";
            return false;
        }

        var client = await BuildClientAsync(ct);
        if (client is null)
        {
            LastError = "A Google-fiók nincs csatlakoztatva (Beállítások → Google).";
            return false;
        }

        try
        {
            var fromName = string.IsNullOrWhiteSpace(settings.IssuerName) ? "Lakáskezelő" : settings.IssuerName;
            var message = new Google.Apis.Gmail.v1.Data.Message { Raw = BuildRawMessage(fromName!, settings.GoogleConnectedEmail!, to, subject, htmlBody) };
            await client.Users.Messages.Send(message, "me").ExecuteAsync(ct);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Gmail kivétel: {ex.Message}";
            return false;
        }
    }

    // Egy fejléc-mező (pl. From megjelenített neve, Subject) csak ASCII karaktereket
    // tartalmazhatna RFC 2822 szerint — az ékezetes szöveget RFC 2047 "encoded-word" formában
    // kell átadni, különben a fogadó kliens (pl. Gmail webes felülete) a nyers UTF-8 bájtokat
    // félreértelmezi és olvashatatlan (mojibake) szöveget jelenít meg.
    private static string EncodeHeaderWord(string text) => "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "?=";

    private static string BuildRawMessage(string fromName, string fromAddress, string to, string subject, string htmlBody)
    {
        var mime = $"From: {EncodeHeaderWord(fromName)} <{fromAddress}>\r\n"
            + $"To: {to}\r\n"
            + $"Subject: {EncodeHeaderWord(subject)}\r\n"
            + "MIME-Version: 1.0\r\n"
            + "Content-Type: text/html; charset=UTF-8\r\n\r\n"
            + htmlBody;

        // Gmail a "raw" mezőhöz web-safe (base64url) kódolást vár, kitöltés nélkül.
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
