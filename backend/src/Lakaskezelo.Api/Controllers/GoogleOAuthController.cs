using System.Text.Json;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

// A Google Drive OAuth-csatlakoztatás két lépése: /start (bejelentkezve hívja a Beállítások oldal,
// visszaadja a Google consent-képernyő URL-jét), /callback (Google hívja böngésző-átirányítással,
// NEM hordoz Authorization fejlécet — ezért AllowAnonymous —, itt cseréljük be a kapott kódot
// frissítő tokenre, és mentjük el az AppSettings egyetlen sorába).
[ApiController]
[Route("api/settings/google-oauth")]
public class GoogleOAuthController(LakaskezeloDbContext db, AppSettingsService settingsService, IHttpClientFactory httpClientFactory, IConfiguration configuration) : ControllerBase
{
    private const string Scope = "https://www.googleapis.com/auth/drive";

    private string BuildRedirectUri() => $"{Request.Scheme}://{Request.Host}/api/settings/google-oauth/callback";

    [Authorize]
    [HttpGet("start")]
    public async Task<ActionResult> Start(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.GoogleOAuthClientId) || string.IsNullOrWhiteSpace(settings.GoogleOAuthClientSecret))
        {
            return BadRequest(new { message = "Előbb add meg a Google OAuth Client ID-t és Client Secret-et." });
        }

        var redirectUri = BuildRedirectUri();
        var authUrl = "https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={Uri.EscapeDataString(settings.GoogleOAuthClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(Scope)}"
            + "&access_type=offline"
            + "&prompt=consent"; // mindig kérjen új refresh tokent — újracsatlakoztatáskor is kapjunk egyet

        return Ok(new { authUrl });
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, CancellationToken ct)
    {
        var frontendBaseUrl = (configuration["Frontend:BaseUrl"] ?? "").TrimEnd('/');

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return Redirect($"{frontendBaseUrl}/settings.html?driveError=1");
        }

        var settingsRow = await db.AppSettings.SingleOrDefaultAsync(s => s.Id == Domain.Entities.AppSettings.SingletonId, ct);
        if (settingsRow is null || string.IsNullOrWhiteSpace(settingsRow.GoogleOAuthClientId) || string.IsNullOrWhiteSpace(settingsRow.GoogleOAuthClientSecret))
        {
            return Redirect($"{frontendBaseUrl}/settings.html?driveError=1");
        }

        try
        {
            var http = httpClientFactory.CreateClient();
            var tokenResponse = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = settingsRow.GoogleOAuthClientId,
                ["client_secret"] = settingsRow.GoogleOAuthClientSecret,
                ["redirect_uri"] = BuildRedirectUri(),
                ["grant_type"] = "authorization_code",
            }), ct);

            var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return Redirect($"{frontendBaseUrl}/settings.html?driveError=1");
            }

            using var tokenDoc = JsonDocument.Parse(tokenBody);
            var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
            var refreshToken = tokenDoc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

            // Google csak első engedélyezéskor (vagy prompt=consent mellett mindig) ad vissza
            // refresh_token-t — ha valamiért mégsem jönne, a régit tartjuk meg, ne törjük el a
            // meglévő csatlakozást egy sikeres, de refresh_token nélküli válasszal.
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                settingsRow.GoogleOAuthRefreshToken = refreshToken;
            }

            var userInfoResponse = await http.GetAsync($"https://www.googleapis.com/oauth2/v2/userinfo?access_token={Uri.EscapeDataString(accessToken!)}", ct);
            if (userInfoResponse.IsSuccessStatusCode)
            {
                using var userDoc = JsonDocument.Parse(await userInfoResponse.Content.ReadAsStringAsync(ct));
                settingsRow.GoogleConnectedEmail = userDoc.RootElement.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
            }

            settingsRow.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            AppSettingsService.Invalidate();

            return Redirect($"{frontendBaseUrl}/settings.html?driveConnected=1");
        }
        catch
        {
            return Redirect($"{frontendBaseUrl}/settings.html?driveError=1");
        }
    }

    [Authorize]
    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        var settingsRow = await db.AppSettings.SingleOrDefaultAsync(s => s.Id == Domain.Entities.AppSettings.SingletonId, ct);
        if (settingsRow is null) return NoContent();

        settingsRow.GoogleOAuthRefreshToken = null;
        settingsRow.GoogleConnectedEmail = null;
        settingsRow.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        AppSettingsService.Invalidate();

        return NoContent();
    }
}
