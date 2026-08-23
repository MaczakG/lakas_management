using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.GoogleDrive;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/settings")]
public class SettingsController(LakaskezeloDbContext db, AppSettingsService settingsService, GoogleDriveService driveService, IEmailSender emailSender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AppSettingsDto>> Get(CancellationToken ct)
    {
        var s = await settingsService.GetAsync(ct);
        return Ok(ToDto(s));
    }

    [HttpPut]
    public async Task<ActionResult<AppSettingsDto>> Update(AppSettingsDto dto, CancellationToken ct)
    {
        var settings = await db.AppSettings.SingleOrDefaultAsync(s => s.Id == Domain.Entities.AppSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new Domain.Entities.AppSettings { Id = Domain.Entities.AppSettings.SingletonId };
            db.AppSettings.Add(settings);
        }

        settings.MailgunApiKey = dto.MailgunApiKey;
        settings.MailgunDomain = dto.MailgunDomain;
        settings.MailgunFromAddress = dto.MailgunFromAddress;
        settings.MailgunFromName = dto.MailgunFromName;
        settings.MailgunApiBaseUrl = dto.MailgunApiBaseUrl;
        settings.GoogleOAuthClientId = dto.GoogleOAuthClientId;
        settings.GoogleOAuthClientSecret = dto.GoogleOAuthClientSecret;
        settings.UtilityContactEmail = dto.UtilityContactEmail;
        settings.UtilityDeadlineDay = Math.Clamp(dto.UtilityDeadlineDay, 1, 28);
        settings.IssuerName = dto.IssuerName;
        settings.IssuerAddress = dto.IssuerAddress;
        settings.IssuerTaxId = dto.IssuerTaxId;
        settings.IssuerBankAccount = dto.IssuerBankAccount;
        settings.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        AppSettingsService.Invalidate();

        return Ok(ToDto(settings));
    }

    [HttpPost("test-mailgun")]
    public async Task<ActionResult<TestMailgunResponse>> TestMailgun(TestEmailRequest request, CancellationToken ct)
    {
        var htmlBody = EmailTemplate.Render("Teszt e-mail", "Teszt e-mail", "<p style=\"margin:0;\">Ez egy teszt e-mail a Lakáskezelő Beállítások oldaláról.</p>");
        var sent = await emailSender.SendAsync(request.To, "Lakáskezelő — teszt e-mail", htmlBody, "Ez egy teszt e-mail a Lakáskezelő Beállítások oldaláról.", ct);
        return Ok(new TestMailgunResponse(sent, sent ? null : emailSender.LastError));
    }

    [HttpPost("test-drive")]
    public async Task<ActionResult<TestDriveResponse>> TestDrive(CancellationToken ct)
    {
        var (success, email, error) = await driveService.TestConnectionAsync(ct);
        return Ok(new TestDriveResponse(success, email, error));
    }

    private static AppSettingsDto ToDto(Domain.Entities.AppSettings s) => new(
        s.MailgunApiKey, s.MailgunDomain, s.MailgunFromAddress, s.MailgunFromName, s.MailgunApiBaseUrl,
        s.GoogleOAuthClientId, s.GoogleOAuthClientSecret, !string.IsNullOrWhiteSpace(s.GoogleOAuthRefreshToken), s.GoogleConnectedEmail,
        s.UtilityContactEmail, s.UtilityDeadlineDay,
        s.IssuerName, s.IssuerAddress, s.IssuerTaxId, s.IssuerBankAccount);
}
