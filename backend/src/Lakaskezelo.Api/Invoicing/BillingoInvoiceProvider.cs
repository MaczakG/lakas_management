using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lakaskezelo.Domain;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Api.Invoicing;

// A cég saját Billingo-fiókja (Billingo v3 API, api.billingo.hu/v3, X-API-KEY). A vevő Billingo-partnerként
// (Tenant.BillingoPartnerId), a számla a beállított számlatömbben, átutalásos fizetéssel készül; a sztornó a
// Billingo „cancel” végpontjával (sztornó számlát hoz létre), a PDF a „download” végpontról jön.
public class BillingoInvoiceProvider(HttpClient http, IConfiguration configuration, ILogger<BillingoInvoiceProvider> logger) : IInvoiceProvider
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Name => InvoicingProviders.Billingo;

    private string BaseUrl => configuration["Billingo:ApiBaseUrlOverride"] is { Length: > 0 } o ? o.TrimEnd('/') + "/" : "https://api.billingo.hu/v3/";

    private HttpRequestMessage Request(HttpMethod method, string path, AppSettings s, object? body = null)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-API-KEY", s.BillingoApiKey);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, string what, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderException("Nem sikerült kapcsolódni a Billingóhoz.", ex);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Billingo {What} failed ({Status}): {Body}", what, response.StatusCode, text);
                throw new ProviderException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "A Billingo elutasította az API-kulcsot. Ellenőrizd a Beállításokban.",
                    HttpStatusCode.TooManyRequests => "A Billingo átmenetileg túlterhelt. Próbáld újra pár perc múlva.",
                    _ => $"Billingo hiba ({what}, {(int)response.StatusCode}): {Message(text)}",
                });
            }
            return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }

    private static string Message(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)) return m.GetString() ?? body;
            if (doc.RootElement.TryGetProperty("message", out var m2)) return m2.GetString() ?? body;
        }
        catch (JsonException) { /* nem JSON */ }
        return body.Length > 200 ? body[..200] : body;
    }

    public async Task<ProviderInvoice> IssueAsync(AppSettings s, ProviderInvoiceRequest r, CancellationToken ct)
    {
        var partnerId = await SavePartnerAsync(s, r.Buyer, ct);
        var body = new
        {
            partner_id = partnerId,
            block_id = s.BillingoBlockId!.Value,
            type = "invoice",
            fulfillment_date = r.FulfillmentDate.ToString("yyyy-MM-dd"),
            due_date = r.DueDate.ToString("yyyy-MM-dd"),
            payment_method = "wire_transfer",
            language = "hu",
            currency = "HUF",
            conversion_rate = 1,
            electronic = false,
            paid = false,
            comment = r.Comment,
            items = r.Lines.Select(l => new
            {
                name = l.Name,
                unit_price = l.GrossAmount,
                unit_price_type = "gross",
                quantity = 1,
                unit = "hó",
                vat = VatCodes.IsExempt(l.VatCode) ? l.VatCode : $"{l.VatCode}%",
            }).ToArray(),
        };
        var doc = await SendAsync<DocumentResponse>(Request(HttpMethod.Post, "documents", s, body), "számla kiállítása", ct);
        if (doc.Id is null) throw new ProviderException("A Billingo nem adott vissza számla-azonosítót.");
        return new ProviderInvoice(doc.InvoiceNumber ?? doc.Id.Value.ToString(), doc.Id.Value.ToString(), null, partnerId);
    }

    public async Task<ProviderInvoice> CancelAsync(AppSettings s, Invoice original, CancellationToken ct)
    {
        var doc = await SendAsync<DocumentResponse>(Request(HttpMethod.Post, $"documents/{original.ProviderDocumentId}/cancel", s,
            new { cancellation_reason = "Sztornó" }), "sztornózás", ct);
        if (doc.Id is null) throw new ProviderException("A Billingo nem adott vissza sztornó-azonosítót.");
        return new ProviderInvoice(doc.InvoiceNumber ?? doc.Id.Value.ToString(), doc.Id.Value.ToString(), null);
    }

    // A PDF a kiállítás után néhány másodperccel készül el a Billingónál, ezért pár újrapróbálás.
    public async Task<byte[]> DownloadPdfAsync(AppSettings s, Invoice invoice, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(2), ct);
            using var request = Request(HttpMethod.Get, $"documents/{invoice.ProviderDocumentId}/download", s);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/pdf")
                {
                    return await response.Content.ReadAsByteArrayAsync(ct);
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound) break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Billingo PDF download attempt {Attempt} failed", attempt + 1);
            }
        }
        throw new ProviderException("A számla PDF-je nem tölthető le a Billingóból. Próbáld újra később.");
    }

    public async Task<ProviderTestResult> TestAsync(AppSettings s, CancellationToken ct)
    {
        try
        {
            var blocks = await SendAsync<ListResponse<BlockResponse>>(Request(HttpMethod.Get, "document-blocks?per_page=100", s), "számlatömbök", ct);
            return new ProviderTestResult(true, null, [.. (blocks.Data ?? []).Where(b => b.Id is not null).Select(b => new ProviderBlock(b.Id!.Value, b.Name ?? $"#{b.Id}"))]);
        }
        catch (ProviderException ex)
        {
            return new ProviderTestResult(false, ex.Message);
        }
    }

    // A vevő mint Billingo-partner: a korábbi partnere frissítve, vagy új.
    private async Task<int> SavePartnerAsync(AppSettings s, ProviderBuyer b, CancellationToken ct)
    {
        var hasTax = !string.IsNullOrWhiteSpace(b.TaxNumber);
        var partner = new
        {
            name = b.Name,
            address = new { country_code = "HU", post_code = b.PostCode, city = b.City, address = b.Street },
            emails = string.IsNullOrWhiteSpace(b.Email) ? null : new[] { b.Email },
            taxcode = hasTax ? b.TaxNumber : null,
            tax_type = hasTax ? "HAS_TAX_NUMBER" : "NO_TAX_NUMBER",
        };
        if (b.BillingoPartnerId is { } id)
        {
            using var update = Request(HttpMethod.Put, $"partners/{id}", s, partner);
            using var response = await http.SendAsync(update, ct);
            if (response.IsSuccessStatusCode) return id;
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new ProviderException($"Nem sikerült frissíteni a vevőt a Billingóban ({(int)response.StatusCode}): {Message(await response.Content.ReadAsStringAsync(ct))}");
            }
        }
        var created = await SendAsync<DocumentResponse>(Request(HttpMethod.Post, "partners", s, partner), "vevő mentése", ct);
        return created.Id ?? throw new ProviderException("A Billingo nem adott vissza partner-azonosítót.");
    }

    private record DocumentResponse(int? Id, string? InvoiceNumber);
    private record BlockResponse(int? Id, string? Name);
    private record ListResponse<T>(List<T>? Data);
}
