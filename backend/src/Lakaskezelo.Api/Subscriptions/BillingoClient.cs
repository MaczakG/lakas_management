using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Api.Subscriptions;

public record BillingoIssueResult(bool Success, int? DocumentId, string? PublicUrl, string? ErrorMessage, int? PartnerId = null);
public sealed record BillingoBuyer(string Name, string? Email, string? TaxNumber, string PostCode, string City, string Street, int? PartnerId);
public sealed record BillingoLine(string Name, int Quantity, string Unit, decimal UnitNetPrice, decimal? VatPercent, string? VatExemption);

// A Billingo v3 API (api.billingo.hu/v3) vékony burkolója (a Yardly BillingoClient-je alapján): a
// sikeres kártyás fizetésről kifizetett ("bankcard") számlát állít ki a vevő partnerére. Minden hívás
// try/catch-elt, és (Success, ...) eredményre redukált — egy Billingo-kiesés nem akasztja meg a
// fizetést; a számla később, az ütemező újrapróbálásakor készül el (SubscriptionPayment.BillingoAttempts).
public partial class BillingoClient(HttpClient http, IConfiguration configuration, ILogger<BillingoClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool IsConfigured(PlatformSettings s) => !string.IsNullOrWhiteSpace(s.BillingoApiKey) && s.BillingoBlockId is not null;

    private string BaseUrl => configuration["Billingo:ApiBaseUrlOverride"] is { Length: > 0 } o ? o.TrimEnd('/') + "/" : "https://api.billingo.hu/v3/";

    private HttpRequestMessage Request(HttpMethod method, string path, PlatformSettings s, object? body = null)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-API-KEY", s.BillingoApiKey);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return request;
    }

    public async Task<BillingoIssueResult> IssuePaidInvoiceAsync(
        PlatformSettings s, BillingoBuyer buyer, IReadOnlyList<BillingoLine> lines, DateOnly fulfillmentDate, string comment, CancellationToken ct)
    {
        int? partnerId = null;
        try
        {
            partnerId = await SavePartnerAsync(s, buyer, ct);
            if (partnerId is null) return new BillingoIssueResult(false, null, null, "Nem sikerült menteni a Billingo partnert.");

            var body = new
            {
                partner_id = partnerId.Value,
                block_id = s.BillingoBlockId!.Value,
                type = "invoice",
                fulfillment_date = fulfillmentDate.ToString("yyyy-MM-dd"),
                due_date = fulfillmentDate.ToString("yyyy-MM-dd"),
                payment_method = "bankcard",
                language = "hu",
                currency = "HUF",
                conversion_rate = 1,
                electronic = true,
                paid = true,
                comment,
                items = lines.Select(l => new
                {
                    name = l.Name,
                    quantity = l.Quantity,
                    unit = l.Unit,
                    unit_price = l.UnitNetPrice,
                    unit_price_type = "net",
                    vat = l.VatExemption ?? $"{l.VatPercent:0.##}%",
                }).ToArray(),
            };

            using var request = Request(HttpMethod.Post, "documents", s, body);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Billingo document creation failed ({Status}): {Body}", response.StatusCode, text);
                return new BillingoIssueResult(false, null, null, $"Billingo hiba ({(int)response.StatusCode}).", partnerId);
            }
            var doc = await response.Content.ReadFromJsonAsync<IdResponse>(JsonOptions, ct);
            if (doc?.Id is null) return new BillingoIssueResult(false, null, null, "A Billingo nem adott vissza dokumentum-azonosítót.", partnerId);

            return new BillingoIssueResult(true, doc.Id, await PublicUrlAsync(s, doc.Id.Value, ct), null, partnerId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Billingo document issuance failed");
            return new BillingoIssueResult(false, null, null, "Nem sikerült kapcsolódni a Billingo szolgáltatáshoz.", partnerId);
        }
    }

    // A vevő mint Billingo-partner: a korábbi partnere frissítve, vagy új. Név alapján soha nem keresünk.
    private async Task<int?> SavePartnerAsync(PlatformSettings s, BillingoBuyer buyer, CancellationToken ct)
    {
        var hasTaxNumber = !string.IsNullOrWhiteSpace(buyer.TaxNumber);
        var partner = new
        {
            name = buyer.Name,
            address = new { country_code = "HU", post_code = buyer.PostCode, city = buyer.City, address = buyer.Street },
            emails = string.IsNullOrWhiteSpace(buyer.Email) ? null : new[] { buyer.Email },
            taxcode = hasTaxNumber ? buyer.TaxNumber : null,
            tax_type = hasTaxNumber ? "HAS_TAX_NUMBER" : "NO_TAX_NUMBER",
        };

        if (buyer.PartnerId is { } id)
        {
            using var update = Request(HttpMethod.Put, $"partners/{id}", s, partner);
            using var updateResponse = await http.SendAsync(update, ct);
            if (updateResponse.IsSuccessStatusCode) return id;
            if (updateResponse.StatusCode != HttpStatusCode.NotFound)
            {
                logger.LogWarning("Billingo partner update failed ({Status}): {Body}", updateResponse.StatusCode, await updateResponse.Content.ReadAsStringAsync(ct));
                return null;
            }
        }

        using var create = Request(HttpMethod.Post, "partners", s, partner);
        using var createResponse = await http.SendAsync(create, ct);
        if (!createResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Billingo partner creation failed ({Status}): {Body}", createResponse.StatusCode, await createResponse.Content.ReadAsStringAsync(ct));
            return null;
        }
        return (await createResponse.Content.ReadFromJsonAsync<IdResponse>(JsonOptions, ct))?.Id;
    }

    public async Task<string?> PublicUrlAsync(PlatformSettings s, int documentId, CancellationToken ct)
    {
        try
        {
            using var request = Request(HttpMethod.Get, $"documents/{documentId}/public-url", s);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return (await response.Content.ReadFromJsonAsync<PublicUrlResponse>(JsonOptions, ct))?.PublicUrl;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch Billingo public URL for document {DocumentId}", documentId);
            return null;
        }
    }

    // Magyar adószám (12345678-1-12) — adószám nélkül magánszemélyként számlázunk.
    public static bool IsHungarianTaxNumber(string value) => TaxNumberRegex().IsMatch(value);
    public static bool IsPostCode(string value) => PostCodeRegex().IsMatch(value);

    [GeneratedRegex(@"^\d{8}-\d-\d{2}$")]
    private static partial Regex TaxNumberRegex();
    [GeneratedRegex(@"^[1-9]\d{3}$")]
    private static partial Regex PostCodeRegex();

    private record IdResponse(int? Id);
    private record PublicUrlResponse(string? PublicUrl);
}
