using System.Net.Http.Headers;
using System.Text;
using Lakaskezelo.Api.Settings;

namespace Lakaskezelo.Api.Notifications;

// Mailgun HTTP API-n keresztül küld (POST /v3/{domain}/messages). A Flotta megfelelőjétől
// eltérően a hitelesítő adatokat (ApiKey/Domain/From) minden híváskor frissen olvassa az
// AppSettingsService-ből, nem egyszer, indításkor IOptions-ból — mert a Beállítások oldalon
// bármikor módosíthatók.
public class MailgunEmailSender(HttpClient http, AppSettingsService settingsService, ILogger<MailgunEmailSender> logger) : IEmailSender
{
    public string? LastError { get; private set; }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        return IsConfigured(settings.MailgunApiKey, settings.MailgunDomain, settings.MailgunFromAddress);
    }

    private static bool IsConfigured(string? apiKey, string? domain, string? from) =>
        !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(from);

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, string textBody, EmailAttachment? attachment, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (!IsConfigured(settings.MailgunApiKey, settings.MailgunDomain, settings.MailgunFromAddress))
        {
            LastError = "Mailgun nincs beállítva (API kulcs / domain / feladó hiányzik a Beállításokban).";
            logger.LogInformation("Mailgun not configured — skipping email to {To}: {Subject}", to, subject);
            return false;
        }

        try
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"api:{settings.MailgunApiKey}")));

            var fromHeader = string.IsNullOrWhiteSpace(settings.MailgunFromName)
                ? settings.MailgunFromAddress!
                : $"{settings.MailgunFromName} <{settings.MailgunFromAddress}>";

            // Mailgun a form mezők mellett a csatolmányt is multipart/form-data-ként várja (az
            // "attachment" mezőben) — a korábbi FormUrlEncodedContent nem tudna bináris fájlt vinni.
            using var form = new MultipartFormDataContent
            {
                { new StringContent(fromHeader), "from" },
                { new StringContent(to), "to" },
                { new StringContent(subject), "subject" },
                { new StringContent(htmlBody), "html" },
                { new StringContent(textBody), "text" },
            };
            if (attachment is not null)
            {
                var fileContent = new ByteArrayContent(attachment.Content);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(attachment.MimeType);
                form.Add(fileContent, "attachment", attachment.FileName);
            }

            var baseUrl = string.IsNullOrWhiteSpace(settings.MailgunApiBaseUrl) ? "https://api.mailgun.net" : settings.MailgunApiBaseUrl!.TrimEnd('/');
            var response = await http.PostAsync($"{baseUrl}/v3/{settings.MailgunDomain}/messages", form, ct);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                LastError = $"Mailgun {(int)response.StatusCode} {response.StatusCode}: {responseBody}";
                logger.LogError("Mailgun send to {To} failed with {Status}: {Body}", to, response.StatusCode, responseBody);
                return false;
            }
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Mailgun kivétel: {ex.Message}";
            logger.LogError(ex, "Failed to send email to {To} via Mailgun", to);
            return false;
        }
    }
}
