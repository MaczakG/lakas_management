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
using Lakaskezelo.Domain.Enums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
        foreach (var operation in document.Paths.Values.SelectMany(p => p.Operations ?? []))
        {
            operation.Value.Security ??= [];
            operation.Value.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
            });
        }
        return Task.CompletedTask;
    });
});

var connectionString = builder.Configuration.GetConnectionString("LakaskezeloDb")
    ?? throw new InvalidOperationException("Connection string 'LakaskezeloDb' is not configured.");
connectionString = ConnectionStringHelper.NormalizeToNpgsqlFormat(connectionString);

builder.Services.AddHttpContextAccessor();
// Több-cégesség: kérésenként egy cég-kontextus, amire a DbContext globális szűrője szűr (ld.
// LakaskezeloDbContext). A JwtBearer OnTokenValidated tölti ki a bejelentkezett felhasználó cégével.
builder.Services.AddScoped<ICompanyContext, CompanyContext>();
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

                // Platform admin (külön fiók, külön token): cég-kontextus nincs, csak a PlatformAdmin szerepkört kapja.
                if (principal!.FindFirstValue(JwtTokenService.KindClaim) == JwtTokenService.PlatformKind)
                {
                    var adminOk = await db.PlatformAdmins.AsNoTracking()
                        .AnyAsync(a => a.Id == userId && a.IsActive && a.SecurityStamp == stamp, context.HttpContext.RequestAborted);
                    if (!adminOk)
                    {
                        context.Fail("Token has been revoked.");
                        return;
                    }
                    if (principal.Identity is ClaimsIdentity platformIdentity)
                    {
                        foreach (var stale in platformIdentity.FindAll(platformIdentity.RoleClaimType).ToList()) platformIdentity.RemoveClaim(stale);
                        platformIdentity.AddClaim(new Claim(platformIdentity.RoleClaimType, PlatformAuthController.PlatformOperatorRole));
                    }
                    return;
                }

                // IgnoreQueryFilters: itt még nincs cég-kontextus — épp ebből a sorból tudjuk meg a felhasználó cégét.
                var current = await db.Users.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => u.Id == userId && u.IsActive && u.EmailVerifiedAt != null && u.SecurityStamp == stamp)
                    .Select(u => new { u.CompanyId, u.Role })
                    .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
                if (current is null)
                {
                    context.Fail("Token has been revoked.");
                    return;
                }
                context.HttpContext.RequestServices.GetRequiredService<ICompanyContext>().Set(current.CompanyId);

                // A szerepkör nem a tokenből, hanem minden kérésnél az adatbázisból jön — így egy
                // admin-jog elvétele azonnal érvényes, nem csak a token lejártakor ([Authorize(Roles=...)]).
                if (principal!.Identity is ClaimsIdentity identity)
                {
                    foreach (var stale in identity.FindAll(identity.RoleClaimType).ToList()) identity.RemoveClaim(stale);
                    identity.AddClaim(new Claim(identity.RoleClaimType, current.Role.ToString()));
                }
            },
        };
    });
builder.Services.AddAuthorization();

// A bejelentkezési végpontok (login, kód-ellenőrzés, elfelejtett/új jelszó) IP-címenként percenként
// legfeljebb 10 kérést fogadnak — a jelszó és a belépési kód találgatása, valamint az e-mail-küldő
// végpontokkal való visszaélés ellen. A kliens IP-je az nginx X-Forwarded-For fejlécéből jön (ld.
// UseForwardedHeaders lent).
var authRateLimit = Math.Max(1, builder.Configuration.GetValue("Auth:RateLimitPerMinute", 10));
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
        // Auth:RateLimitPerMinute csak helyi/automata teszthez emelendő; élesben az alapérték (10) marad.
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authRateLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---------- Beállítások (DB-backed, nem appsettings) ----------
builder.Services.AddScoped<AppSettingsService>();

// ---------- E-mail (Mailgun és SMTP — a Beállításokban választható, melyik aktív) ----------
builder.Services.AddHttpClient<MailgunEmailSender>();
builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddScoped<IEmailSender, EmailSenderRouter>();
builder.Services.AddScoped<SystemEmailSender>();
builder.Services.AddScoped<AccountEmails>();
builder.Services.AddScoped<UserTokenService>();

// ---------- Előfizetés (SaaS-díj): Barion kártyás fizetés + Billingo-számla ----------
builder.Services.AddScoped<Lakaskezelo.Api.Subscriptions.PlatformSettingsService>();
builder.Services.AddScoped<Lakaskezelo.Api.Subscriptions.SubscriptionAccess>();
builder.Services.AddScoped<Lakaskezelo.Api.Subscriptions.SubscriptionEmails>();
builder.Services.AddScoped<Lakaskezelo.Api.Subscriptions.SubscriptionService>();
builder.Services.AddHttpClient<Lakaskezelo.Api.Subscriptions.BarionClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<Lakaskezelo.Api.Subscriptions.BillingoClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHostedService<Lakaskezelo.Api.Subscriptions.SubscriptionScanner>();

// ---------- Számla-PDF tárhely (Amazon S3) ----------
builder.Services.AddScoped<S3InvoiceStorage>();
builder.Services.AddScoped<InvoicePdfStore>();
builder.Services.AddScoped<Lakaskezelo.Api.Documents.DocumentStorage>();

// ---------- Cégek számlázója (Billingo, Számlázz.hu) ----------
builder.Services.AddHttpClient<Lakaskezelo.Api.Invoicing.BillingoInvoiceProvider>(c => c.Timeout = TimeSpan.FromSeconds(40));
builder.Services.AddHttpClient<Lakaskezelo.Api.Invoicing.SzamlazzInvoiceProvider>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddScoped<Lakaskezelo.Api.Invoicing.InvoiceProviderResolver>();

// ---------- Számlázás ----------
builder.Services.AddScoped<InvoiceGenerationService>();
builder.Services.AddHostedService<BillingSchedulerService>();
builder.Services.AddHostedService<UtilityReminderService>();
builder.Services.AddHostedService<Lakaskezelo.Api.LeaseExpiry.LeaseExpiryReminderService>();
builder.Services.AddHostedService<Lakaskezelo.Api.Accounts.RegistrationCleanupService>();

// ---------- MNB árfolyamok ----------
builder.Services.AddHttpClient<MnbExchangeRateClient>();
builder.Services.AddScoped<ExchangeRateUpdater>();
builder.Services.AddHostedService<ExchangeRateFetchService>();

var app = builder.Build();

// Kezdeti felhasználó seedelése env változóból (InitialUser:Email / InitialUser:Password) — az első
// (üzemeltető) cég adminisztrátora. További cégek regisztrációval (Registration:Enabled), további
// felhasználók meghívással jönnek létre. Idempotens: csak akkor fut, ha a tábla még üres.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
    await db.Database.MigrateAsync();

    var seedEmail = app.Configuration["InitialUser:Email"];
    var seedPassword = app.Configuration["InitialUser:Password"];

    if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPassword) && !await db.Users.IgnoreQueryFilters().AnyAsync())
    {
        // Az első felhasználó az alapértelmezett céghez kerül (a cég a CompanyTenancy migrációval jön létre).
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            CompanyId = Company.DefaultCompanyId,
            Email = seedEmail.Trim().ToLowerInvariant(),
            FullName = "Adminisztrátor",
            IsActive = true,
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow,
            EmailVerifiedAt = DateTime.UtcNow,
            SecurityStamp = SecurityStamp.New(),
        };
        user.PasswordHash = hasher.HashPassword(user, seedPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}

// Az első Platform admin fiók (PlatformAdmin:Email) — jelszó nélkül jön létre; az első jelszót a
// platform-login.html „Jelszó beállítása / elfelejtett jelszó” linkjével e-mailben kapott linkkel állítja be.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LakaskezeloDbContext>();
    var adminEmail = app.Configuration["PlatformAdmin:Email"]?.Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(adminEmail) && !await db.PlatformAdmins.AnyAsync())
    {
        db.PlatformAdmins.Add(new PlatformAdmin
        {
            Id = Guid.NewGuid(),
            Email = adminEmail,
            FullName = app.Configuration["PlatformAdmin:Name"] ?? "Platform admin",
            IsActive = true,
            SecurityStamp = SecurityStamp.New(),
            CreatedAt = DateTime.UtcNow,
        });
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
    forwardedHeadersOptions.KnownIPNetworks.Clear();
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
// A platform-token csak a platform-végpontokat éri el (és fordítva: a cégek tokenje a platformot nem, ld.
// PlatformOperatorAttribute) — így a két világ teljesen elkülönül.
app.Use(async (context, next) =>
{
    if (context.User.IsInRole(PlatformAuthController.PlatformOperatorRole)
        && context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/api/platform")
        && !context.Request.Path.StartsWithSegments("/api/auth/platform")
        && !context.Request.Path.StartsWithSegments("/api/version"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "A Platform admin fiókkal csak a Platform admin felület érhető el." });
        return;
    }
    await next(context);
});
// Nem fizető (zárolt) cégnél a módosító kérések 402-t kapnak — a hitelesítés után, mert a cég a tokenből jön.
app.UseMiddleware<Lakaskezelo.Api.Subscriptions.SubscriptionWriteGuard>();

app.MapHealthChecks("/health");

// Nem védett — a sidebar lábjegyzete (frontend/nav.js) bejelentkezés előtt is lekérheti, és
// támogatáshoz/hibakereséshez hasznos anélkül is tudni, melyik verzió fut és mikor indult.
var appStartedAt = DateTime.UtcNow;
// environment: "production" az élesen, "staging" a teszt-környezetben (App:EnvironmentName) — a
// felület ebből tudja, hogy ki kell-e tennie a "TESZT" jelzést.
var environmentName = app.Configuration["App:EnvironmentName"] ?? "production";
// Nyilvános: verzió, környezet, a termék neve és hogy nyitva van-e a regisztráció (a belépési oldal ez
// alapján mutatja a "Regisztráció" linket).
app.MapGet("/api/version", () => Results.Ok(new
{
    version = Lakaskezelo.Api.AppVersion.Current,
    startedAt = appStartedAt,
    environment = environmentName,
    productName = app.Configuration["App:ProductName"] is { Length: > 0 } productName ? productName : "Lakáskezelő",
    registrationEnabled = app.Configuration.GetValue("Registration:Enabled", false),
}));

app.MapControllers();

app.Run();
