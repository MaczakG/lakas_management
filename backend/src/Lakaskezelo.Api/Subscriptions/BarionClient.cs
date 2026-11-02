using System.Text.Json;
using System.Text.Json.Serialization;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Api.Subscriptions;

public record BarionStartResult(bool Success, string? PaymentId, string? GatewayUrl, string? Status, string? ErrorMessage);
// TraceId: a kártyakibocsátó azonosítója a kártyát regisztráló fizetésnél — a későbbi terhelésekkel küldjük.
public record BarionPaymentState(bool Success, string? Status, string? ErrorMessage, string? TraceId = null);

// A Barion Smart Gateway API vékony burkolója (a Yardly BarionClient-je alapján). Minden kártyás
// fizetés token-fizetés: az első, a fizetőoldalon teljesített fizetés regisztrálja a kártyát egy általunk
// adott RecurrenceId alatt (InitiateRecurrence: true), a megújítások ugyanezzel, felhasználó nélkül
// terhelnek (InitiateRecurrence: false, MerchantInitiatedPayment, TraceId-val). A token-fizetést a
// Barionnak engedélyeznie kell a bolt számára. A kulcsok a PlatformSettings-ből jönnek hívásonként.
public class BarionClient(HttpClient http, IConfiguration configuration, ILogger<BarionClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null, // a Barion pontos PascalCase mezőneveket vár
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool IsConfigured(PlatformSettings s) =>
        !string.IsNullOrWhiteSpace(s.BarionPosKey) && !string.IsNullOrWhiteSpace(s.BarionPayeeEmail);

    private string BaseUrl(PlatformSettings s) =>
        configuration["Barion:ApiBaseUrlOverride"] is { Length: > 0 } overrideUrl ? overrideUrl.TrimEnd('/')
        : s.BarionEnvironment.Equals("Prod", StringComparison.OrdinalIgnoreCase) ? "https://api.barion.com"
        : "https://api.test.barion.com";

    public async Task<BarionStartResult> StartPaymentAsync(
        PlatformSettings s, string paymentRequestId, string payerEmail, decimal amountHuf, string itemName, int quantity,
        string unit, decimal unitPriceGross, string redirectUrl, string callbackUrl, string recurrenceId, bool initiateRecurrence,
        string? traceId, CancellationToken ct)
    {
        var request = new
        {
            POSKey = s.BarionPosKey,
            PaymentType = "Immediate",
            GuestCheckOut = true,
            InitiateRecurrence = initiateRecurrence,
            RecurrenceId = recurrenceId,
            RecurrenceType = "MerchantInitiatedPayment",
            TraceId = initiateRecurrence ? null : traceId,
            FundingSources = new[] { "All" },
            PaymentRequestId = paymentRequestId,
            PayerHint = payerEmail,
            Locale = "hu-HU",
            RedirectUrl = redirectUrl,
            CallbackUrl = callbackUrl,
            Currency = "HUF",
            Transactions = new[]
            {
                new
                {
                    POSTransactionId = paymentRequestId,
                    Payee = s.BarionPayeeEmail,
                    Total = amountHuf,
                    Comment = itemName,
                    Items = new[]
                    {
                        new { Name = itemName, Description = itemName, Quantity = quantity, Unit = unit, UnitPrice = unitPriceGross, ItemTotal = amountHuf },
                    },
                },
            },
        };

        try
        {
            using var response = await http.PostAsJsonAsync($"{BaseUrl(s)}/v2/Payment/Start", request, JsonOptions, ct);
            var payload = await response.Content.ReadFromJsonAsync<StartResponse>(JsonOptions, ct);
            if (payload?.Errors is { Length: > 0 })
            {
                var message = string.Join("; ", payload.Errors.Select(e => e.Description));
                logger.LogWarning("Barion payment start rejected: {Message}", message);
                return new BarionStartResult(false, null, null, null, message);
            }
            if (payload?.PaymentId is null)
            {
                return new BarionStartResult(false, null, null, null, "A Barion nem adott vissza fizetési azonosítót.");
            }
            return new BarionStartResult(true, payload.PaymentId, payload.GatewayUrl, payload.Status, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Barion payment start failed");
            return new BarionStartResult(false, null, null, null, "Nem sikerült kapcsolódni a Barion fizetési szolgáltatáshoz.");
        }
    }

    public async Task<BarionPaymentState> GetPaymentStateAsync(PlatformSettings s, string paymentId, CancellationToken ct)
    {
        try
        {
            var url = $"{BaseUrl(s)}/v2/Payment/GetPaymentState?POSKey={Uri.EscapeDataString(s.BarionPosKey ?? "")}&PaymentId={Uri.EscapeDataString(paymentId)}";
            using var response = await http.GetAsync(url, ct);
            var payload = await response.Content.ReadFromJsonAsync<StateResponse>(JsonOptions, ct);
            if (payload?.Errors is { Length: > 0 })
            {
                return new BarionPaymentState(false, null, string.Join("; ", payload.Errors.Select(e => e.Description)));
            }
            return new BarionPaymentState(true, payload?.Status, null, payload?.TraceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Barion payment state check failed for {PaymentId}", paymentId);
            return new BarionPaymentState(false, null, "Nem sikerült lekérdezni a fizetés állapotát a Bariontól.");
        }
    }

    public static bool IsSucceeded(string? status) => string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase);

    public static bool IsFinalFailure(string? status) =>
        status is not null && (status.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Expired", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase));

    private record BarionError(string? ErrorCode, string? Title, string? Description);
    private record StartResponse(string? PaymentId, string? Status, string? GatewayUrl, BarionError[]? Errors);
    private record StateResponse(string? Status, string? TraceId, BarionError[]? Errors);
}
