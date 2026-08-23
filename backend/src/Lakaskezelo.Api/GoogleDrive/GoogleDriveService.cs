using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Lakaskezelo.Api.Settings;

namespace Lakaskezelo.Api.GoogleDrive;

public record DriveFileInfo(string Id, string Name, string? WebViewLink, DateTime? CreatedAt, long? SizeBytes);

// A Google Drive-hitelesítés service account-tal történik (nem OAuth) — a felhasználó a
// Beállítások oldalon beilleszti a service account JSON kulcsát, és megosztja vele a cél Drive
// mappákat. Ez teszi lehetővé, hogy a háttérben, felügyelet nélkül (pl. éjjel a
// BillingSchedulerService) is tudjunk fájlt feltölteni/listázni — nincs szükség egy élő
// felhasználói OAuth-munkamenetre.
public class GoogleDriveService(AppSettingsService settingsService)
{
    private static readonly string[] Scopes = [DriveService.Scope.Drive];

    private async Task<DriveService?> BuildClientAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.GoogleServiceAccountJson)) return null;

        var credential = CredentialFactory.FromJson<ServiceAccountCredential>(settings.GoogleServiceAccountJson)
            .ToGoogleCredential().CreateScoped(Scopes);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Lakaskezelo",
        });
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        return !string.IsNullOrWhiteSpace(settings.GoogleServiceAccountJson);
    }

    // A service account JSON-ból kiolvasott client_email — ezt kell megosztani a cél Drive
    // mappákkal, különben a service account nem fér hozzájuk. A Beállítások "Kapcsolat
    // tesztelése" gombja ezt írja ki a felhasználónak.
    public async Task<(bool Success, string? ServiceAccountEmail, string? Error)> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.GoogleServiceAccountJson))
        {
            return (false, null, "Nincs megadva Google service account JSON kulcs.");
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(settings.GoogleServiceAccountJson);
            var email = doc.RootElement.TryGetProperty("client_email", out var el) ? el.GetString() : null;

            var client = await BuildClientAsync(ct);
            if (client is null) return (false, email, "Nem sikerült kliens létrehozása.");

            var about = client.About.Get();
            about.Fields = "user";
            await about.ExecuteAsync(ct);

            return (true, email, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<DriveFileInfo?> UploadFileAsync(string folderId, string fileName, byte[] content, string mimeType, CancellationToken ct = default)
    {
        var client = await BuildClientAsync(ct) ?? throw new InvalidOperationException("Google Drive nincs beállítva.");

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = [folderId],
        };

        using var stream = new MemoryStream(content);
        var request = client.Files.Create(fileMetadata, stream, mimeType);
        request.Fields = "id,name,webViewLink,createdTime,size";
        var progress = await request.UploadAsync(ct);
        if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
        {
            throw new InvalidOperationException($"Drive feltöltés sikertelen: {progress.Exception?.Message}");
        }

        var file = request.ResponseBody;
        return new DriveFileInfo(file.Id, file.Name, file.WebViewLink, file.CreatedTimeDateTimeOffset?.UtcDateTime, file.Size);
    }

    public async Task<List<DriveFileInfo>> ListFilesAsync(string folderId, CancellationToken ct = default)
    {
        var client = await BuildClientAsync(ct);
        if (client is null) return [];

        var request = client.Files.List();
        request.Q = $"'{folderId}' in parents and trashed = false";
        request.Fields = "files(id,name,webViewLink,createdTime,size)";
        request.OrderBy = "createdTime desc";
        request.PageSize = 100;

        var result = await request.ExecuteAsync(ct);
        return [.. result.Files.Select(f => new DriveFileInfo(f.Id, f.Name, f.WebViewLink, f.CreatedTimeDateTimeOffset?.UtcDateTime, f.Size))];
    }
}
