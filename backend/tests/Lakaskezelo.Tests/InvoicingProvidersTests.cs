using System.Net;
using System.Text;
using System.Xml.Linq;
using Lakaskezelo.Api.Invoicing;
using Lakaskezelo.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakaskezelo.Tests;

// A számlázók (Billingo, Számlázz.hu) hívásai ál-HTTP-kiszolgálóval: mit küldünk, és hogyan értelmezzük a választ.
public class InvoicingProvidersTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly IConfiguration NoConfig = new ConfigurationBuilder().Build();

    private static ProviderInvoiceRequest Request() => new(
        new ProviderBuyer("Kiss Anna", "anna@example.test", null, "1061", "Budapest", "Futó utca 12. 3/4", null),
        [new ProviderLine("Bérleti díj (2026. december)", 250000m, "TAM"), new ProviderLine("Közös költség", 12700m, "27")],
        new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 9), new DateOnly(2026, 12, 9), "Futó utca 12. 3/4, 2026. december");

    [Theory]
    [InlineData(12700, "27", 10000, 2700)]
    [InlineData(250000, "TAM", 250000, 0)]
    [InlineData(10500, "5", 10000, 500)]
    public void Gross_is_split_into_net_and_vat(decimal gross, string code, decimal net, decimal vat)
    {
        Assert.Equal((net, vat), InvoicingProviders.Split(gross, code));
    }

    [Fact]
    public async Task Billingo_creates_partner_then_gross_priced_invoice_with_vat_codes()
    {
        var handler = new FakeHandler((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/partners")
            ? Json("""{"id": 77}""")
            : Json("""{"id": 9001, "invoice_number": "LAK-2026-12"}"""));
        var provider = new BillingoInvoiceProvider(new HttpClient(handler), NoConfig, NullLogger<BillingoInvoiceProvider>.Instance);
        var settings = new AppSettings { BillingoApiKey = "kulcs", BillingoBlockId = 5 };

        var result = await provider.IssueAsync(settings, Request(), default);

        Assert.Equal("LAK-2026-12", result.Number);
        Assert.Equal("9001", result.DocumentId);
        Assert.Equal(77, result.BillingoPartnerId);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal("kulcs", handler.Calls[1].Request.Headers.GetValues("X-API-KEY").Single());
        var doc = handler.Calls[1].Body;
        Assert.Contains("\"partner_id\":77", doc);
        Assert.Contains("\"block_id\":5", doc);
        Assert.Contains("\"payment_method\":\"wire_transfer\"", doc);
        Assert.Contains("\"unit_price_type\":\"gross\"", doc);
        Assert.Contains("\"vat\":\"TAM\"", doc);
        Assert.Contains("\"vat\":\"27%\"", doc);
    }

    [Fact]
    public async Task Billingo_rejected_key_gives_readable_error()
    {
        var handler = new FakeHandler((_, _) => Json("""{"error":{"message":"Unauthenticated."}}""", HttpStatusCode.Unauthorized));
        var provider = new BillingoInvoiceProvider(new HttpClient(handler), NoConfig, NullLogger<BillingoInvoiceProvider>.Instance);

        var test = await provider.TestAsync(new AppSettings { BillingoApiKey = "rossz" }, default);

        Assert.False(test.Success);
        Assert.Contains("API-kulcs", test.Error);
    }

    [Fact]
    public async Task Billingo_cancel_returns_the_storno_document()
    {
        var handler = new FakeHandler((_, _) => Json("""{"id": 9002, "invoice_number": "LAK-2026-13"}"""));
        var provider = new BillingoInvoiceProvider(new HttpClient(handler), NoConfig, NullLogger<BillingoInvoiceProvider>.Instance);

        var storno = await provider.CancelAsync(new AppSettings { BillingoApiKey = "k", BillingoBlockId = 5 },
            new Invoice { Provider = "Billingo", ProviderDocumentId = "9001", Number = "LAK-2026-12" }, default);

        Assert.Equal("LAK-2026-13", storno.Number);
        Assert.EndsWith("/documents/9001/cancel", handler.Calls.Single().Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Szamlazz_invoice_xml_has_buyer_items_and_no_email_from_szamlazz()
    {
        var xml = SzamlazzInvoiceProvider.BuildInvoiceXml(new AppSettings { SzamlazzAgentKey = "agent", SzamlazzInvoicePrefix = "LAK" }, Request());
        XNamespace ns = "http://www.szamlazz.hu/xmlszamla";
        var root = xml.Root!;
        Assert.Equal("agent", root.Element(ns + "beallitasok")!.Element(ns + "szamlaagentkulcs")!.Value);
        Assert.Equal("LAK", root.Element(ns + "fejlec")!.Element(ns + "szamlaszamElotag")!.Value);
        Assert.Equal("false", root.Element(ns + "vevo")!.Element(ns + "sendEmail")!.Value);
        var items = root.Element(ns + "tetelek")!.Elements(ns + "tetel").ToList();
        Assert.Equal("TAM", items[0].Element(ns + "afakulcs")!.Value);
        Assert.Equal("250000", items[0].Element(ns + "bruttoErtek")!.Value);
        Assert.Equal("10000", items[1].Element(ns + "nettoErtek")!.Value);
        Assert.Equal("2700", items[1].Element(ns + "afaErtek")!.Value);
    }

    [Fact]
    public async Task Szamlazz_issue_reads_number_and_pdf_from_xml_response()
    {
        var pdf = Convert.ToBase64String("%PDF-1.4 teszt"u8.ToArray());
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"""<?xml version="1.0" encoding="UTF-8"?><xmlszamlavalasz xmlns="http://www.szamlazz.hu/xmlszamlavalasz"><sikeres>true</sikeres><szamlaszam>LAK-2026-7</szamlaszam><pdf>{pdf}</pdf></xmlszamlavalasz>""", Encoding.UTF8, "application/xml"),
        });
        var provider = new SzamlazzInvoiceProvider(new HttpClient(handler), NoConfig, NullLogger<SzamlazzInvoiceProvider>.Instance);

        var result = await provider.IssueAsync(new AppSettings { SzamlazzAgentKey = "agent" }, Request(), default);

        Assert.Equal("LAK-2026-7", result.Number);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(result.Pdf!));
        Assert.Contains("name=action-xmlagentxmlfile", handler.Calls.Single().Body);
    }

    [Fact]
    public async Task Szamlazz_error_header_becomes_readable_error()
    {
        var handler = new FakeHandler((_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("", Encoding.UTF8, "text/plain") };
            r.Headers.Add("szlahu_error_code", "3");
            r.Headers.Add("szlahu_error", Uri.EscapeDataString("Sikertelen bejelentkezés"));
            return r;
        });
        var provider = new SzamlazzInvoiceProvider(new HttpClient(handler), NoConfig, NullLogger<SzamlazzInvoiceProvider>.Instance);

        var test = await provider.TestAsync(new AppSettings { SzamlazzAgentKey = "rossz" }, default);

        Assert.False(test.Success);
        Assert.Contains("kulcs", test.Error);
    }

    [Fact]
    public void Buyer_address_comes_from_tenant_or_property()
    {
        var property = new Property { PostalCode = "1061", City = "Budapest", Street = "Futó utca 12.", FloorDoor = "3/4" };
        Assert.Equal(("2890", "Tata", "Kocsi utca 1."), Parts(InvoicingProviders.BuyerFor(new Tenant { Name = "A", Address = "2890 Tata, Kocsi utca 1." }, property)));
        Assert.Equal(("1061", "Budapest", "Futó utca 12. 3/4"), Parts(InvoicingProviders.BuyerFor(new Tenant { Name = "A" }, property)));
        Assert.Throws<ProviderException>(() => InvoicingProviders.BuyerFor(new Tenant { Name = "A" }, new Property()));

        static (string, string, string) Parts(ProviderBuyer b) => (b.PostCode, b.City, b.Street);
    }
}
