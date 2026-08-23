using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Lakaskezelo.Api.Settings;

namespace Lakaskezelo.Api.GoogleDrive;

public record DriveFileInfo(string Id, string Name, string? WebViewLink, DateTime? CreatedAt, long? SizeBytes);

// A Google Drive-hitelesítés OAuth-tal történik, a felhasználó saját Google-fiókjával — NEM
// service accounttal. Kipróbáltuk a service account utat, és élesben kiderült, hogy a Google nem
// engedi service accountnak fájlt létrehozni egy sima (nem Workspace) személyes Drive-ban: "Service
// Accounts do not have storage quota. Leverage shared drives, or use OAuth delegation instead." —
// mivel a felhasználónak nincs fizetős Google Workspace-e (Megosztott meghajtóhoz az kellene), az
// OAuth az egyetlen működő út ingyenes Gmail-lel. A Beállítások oldalon egyszer kell engedélyezni
// (GoogleOAuthController), utána a frissítő token (refresh token) teszi lehetővé a felügyelet
// nélküli, éjszakai hozzáférést is — a UserCredential automatikusan frissíti az access tokent.
public class GoogleDriveService(AppSettingsService settingsService)
{
    private static readonly string[] Scopes = [DriveService.Scope.Drive];

    private async Task<DriveService?> BuildClientAsync(CancellationToken ct)
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

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Lakaskezelo",
        });
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        return !string.IsNullOrWhiteSpace(settings.GoogleOAuthRefreshToken);
    }

    // A Beállítások "Kapcsolat tesztelése" gombjához — a csatlakoztatott fiók e-mail címét adja
    // vissza, hogy a felhasználó lássa, melyik fiókkal van összekötve.
    public async Task<(bool Success, string? Email, string? Error)> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.GoogleOAuthRefreshToken))
        {
            return (false, null, "A Google Drive nincs csatlakoztatva. Kattints a \"Csatlakoztatás Google-fiókkal\" gombra.");
        }

        try
        {
            var client = await BuildClientAsync(ct);
            if (client is null) return (false, null, "Nem sikerült kliens létrehozása.");

            var about = client.About.Get();
            about.Fields = "user";
            var result = await about.ExecuteAsync(ct);

            return (true, result.User?.EmailAddress ?? settings.GoogleConnectedEmail, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<DriveFileInfo?> UploadFileAsync(string folderId, string fileName, byte[] content, string mimeType, CancellationToken ct = default)
    {
        var client = await BuildClientAsync(ct) ?? throw new InvalidOperationException("Google Drive nincs csatlakoztatva.");

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = [folderId],
        };

        using var stream = new MemoryStream(content);
        var request = client.Files.Create(fileMetadata, stream, mimeType);
        request.Fields = "id,name,webViewLink,createdTime,size";
        request.SupportsAllDrives = true; // ártalmatlan no-op sima Drive-on, de Megosztott meghajtón szükséges
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
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var result = await request.ExecuteAsync(ct);
        return [.. result.Files.Select(f => new DriveFileInfo(f.Id, f.Name, f.WebViewLink, f.CreatedTimeDateTimeOffset?.UtcDateTime, f.Size))];
    }
}
