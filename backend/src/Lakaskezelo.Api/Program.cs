using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Lakaskezelo.Api.Auth;
using Lakaskezelo.Api.Billing;
using Lakaskezelo.Api.Controllers;
using Lakaskezelo.Api.ExchangeRates;
using Lakaskezelo.Api.Storage;
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
            // Content-Disposition: a letöltött PDF/ZIP fájlnevét a frontend (app.js downloadFile) innen olvassa.
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Content-Disposition");
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
        options.Events = new JwtBearerEvents
        {
            // Minden kérésnél ellenőrizzük, hogy a fiók még aktív-e, és a token a felhasználó
            // aktuális biztonsági bélyegével készült-e (ld. User.SecurityStamp) — így inaktiválás,
            // törlés vagy jelszócsere után a korábban kiadott tokenek azonnal használhatatlanok,
            // nem csak a lejáratukkor.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var rawUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                var stamp = principal?.FindFirstValue(SecurityStamp.ClaimType);
                if (!Guid.TryParse(rawUserId, out var userId) || string.IsNullOrEmpty(stamp))
                {
                    context.Fail("Token has no user id or security stamp.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<LakaskezeloDbContext>();
                var valid = await db.Users.AsNoTracking()
                    .AnyAsync(u => u.Id == userId && u.IsActive && u.SecurityStamp == stamp, context.HttpContext.RequestAborted);
                if (!valid)
                {
                    context.Fail("Token has been revoked.");
                }
            },
        };
    });
builder.Services.AddAuthorization();

// A bejelentkezési végpontok (login, kód-ellenőrzés, elfelejtett/új jelszó) IP-címenként percenként
// legfeljebb 10 kérést fogadnak — a jelszó és a belépési kód találgatása, valamint az e-mail-küldő
// végpontokkal való visszaélés ellen. A kliens IP-je az nginx X-Forwarded-For fejlécéből jön (ld.
// UseForwardedHeaders lent).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsync("{\"message\":\"Túl sok próbálkozás. Várj egy percet, és próbáld újra.\"}", ct);
    };
    options.AddPolicy(AuthController.RateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---------- Beállítások (DB-backed, nem appsettings) ----------
builder.Services.AddScoped<AppSettingsService>();

// ---------- E-mail (Mailgun és SMTP — a Beállításokban választható, melyik aktív) ----------
builder.Services.AddHttpClient<MailgunEmailSender>();
builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddScoped<IEmailSender, EmailSenderRouter>();

// ---------- Számla-PDF tárhely (Amazon S3) ----------
builder.Services.AddScoped<S3InvoiceStorage>();
builder.Services.AddScoped<InvoicePdfStore>();

// ---------- Számlázás ----------
builder.Services.AddScoped<InvoiceGenerationService>();
builder.Services.AddHostedService<BillingSchedulerService>();
builder.Services.AddHostedService<UtilityReminderService>();

// ---------- MNB árfolyamok ----------
builder.Services.AddHttpClient<MnbExchangeRateClient>();
builder.Services.AddScoped<ExchangeRateUpdater>();
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
            SecurityStamp = SecurityStamp.New(),
        };
        user.PasswordHash = hasher.HashPassword(user, seedPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}

if (!app.Configuration.GetValue("Auth:TwoFactorEnabled", true))
{
    app.Logger.LogWarning("Two-factor sign-in is DISABLED (Auth:TwoFactorEnabled=false) — only use this as a temporary emergency measure.");
}

// TLS-t az nginx (EC2) zárja le, és sima HTTP-n továbbít a konténernek — a kliens valódi IP-je és a
// séma az X-Forwarded-* fejlécekből jön. ForwardLimit = 1: csak az utolsó (nginx által hozzáfűzött)
// értéket fogadjuk el, így a kliens által küldött hamis X-Forwarded-For nem számít.
if (!app.Environment.IsDevelopment())
{
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

// Nem védett — a sidebar lábjegyzete (frontend/nav.js) bejelentkezés előtt is lekérheti, és
// támogatáshoz/hibakereséshez hasznos anélkül is tudni, melyik verzió fut és mikor indult.
var appStartedAt = DateTime.UtcNow;
app.MapGet("/api/version", () => Results.Ok(new { version = Lakaskezelo.Api.AppVersion.Current, startedAt = appStartedAt }));

app.MapControllers();

app.Run();
