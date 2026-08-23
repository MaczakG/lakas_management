using System.Text;
using System.Text.Json.Serialization;
using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.ExchangeRates;
using Lakaskezelo.Api.GoogleDrive;
using Lakaskezelo.Api.Notifications;
using Lakaskezelo.Api.Settings;
using Lakaskezelo.Api.UtilityReminder;
using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new();
        document.Components.SecuritySchemes["Bearer"] = new()
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
        foreach (var operation in document.Paths.SelectMany(p => p.Value.Operations))
        {
            operation.Value.Security.Add(new() { [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            }] = [] });
        }
        return Task.CompletedTask;
    });
});

var connectionString = builder.Configuration.GetConnectionString("LakaskezeloDb")
    ?? throw new InvalidOperationException("Connection string 'LakaskezeloDb' is not configured.");
connectionString = ConnectionStringHelper.NormalizeToNpgsqlFormat(connectionString);

builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<LakaskezeloDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<LakaskezeloDbContext>("database");

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

// ---------- Auth ----------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be configured and at least 32 characters long.");
}

builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// ---------- Beállítások (DB-backed, nem appsettings) ----------
builder.Services.AddScoped<AppSettingsService>();

// ---------- E-mail (Mailgun és Gmail — a Beállításokban választható, melyik aktív) ----------
builder.Services.AddHttpClient<MailgunEmailSender>();
builder.Services.AddScoped<GmailEmailSender>();
builder.Services.AddScoped<IEmailSender, EmailSenderRouter>();

// ---------- Google Drive ----------
builder.Services.AddScoped<GoogleDriveService>();

// ---------- Számlázás ----------
builder.Services.AddScoped<InvoiceGenerationService>();
builder.Services.AddHostedService<BillingSchedulerService>();
builder.Services.AddHostedService<UtilityReminderService>();

// ---------- MNB árfolyamok ----------
builder.Services.AddHttpClient<MnbExchangeRateClient>();
builder.Services.AddHostedService<ExchangeRateFetchService>();

var app = builder.Build();

// Kezdeti felhasználó seedelése env változóból (InitialUser:Email / InitialUser:Password) — nincs
// önregisztráció, a Felhasználók oldalon minden további fiókot egy már bejelentkezett felhasználó
// hoz létre. Idempotens: csak akkor fut, ha a tábla még üres.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
    await db.Database.MigrateAsync();

    var seedEmail = app.Configuration["InitialUser:Email"];
    var seedPassword = app.Configuration["InitialUser:Password"];

    if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPassword) && !await db.Users.AnyAsync())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = seedEmail.Trim().ToLowerInvariant(),
            FullName = "Adminisztrátor",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, seedPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}

// Render terminates TLS at its edge and forwards to the container over plain HTTP.
if (!app.Environment.IsDevelopment())
{
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    forwardedHeadersOptions.KnownNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);
}

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapControllers();

app.Run();
