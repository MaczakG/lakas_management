using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Lakaskezelo.Domain;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Api.Invoicing;

// A cég saját Számlázz.hu-fiókja a Számla Agenten át (https://www.szamlazz.hu/szamla/, Számla Agent kulccsal):
// XML-t küldünk multipart mezőben (action-xmlagentxmlfile: kiállítás, action-szamla_agent_st: sztornó,
// action-szamla_agent_pdf: PDF). A válasz 2-es verziójú XML (xmlszamlavalasz), benne a számlaszám és a PDF
// base64-ben; hibánál a szlahu_error / szlahu_error_code fejléc és a hibaüzenet.
public class SzamlazzInvoiceProvider(HttpClient http, IConfiguration configuration, ILogger<SzamlazzInvoiceProvider> logger) : IInvoiceProvider
{
    private static readonly XNamespace Ns = "http://www.szamlazz.hu/xmlszamla";
    private static readonly XNamespace NsSt = "http://www.szamlazz.hu/xmlszamlast";
    private static readonly XNamespace NsPdf = "http://www.szamlazz.hu/xmlszamlapdf";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Name => InvoicingProviders.Szamlazzhu;

    private string Url => configuration["Szamlazz:AgentUrlOverride"] is { Length: > 0 } o ? o : "https://www.szamlazz.hu/szamla/";

    // ----- XML összeállítás (tesztelhetőség miatt statikus) -----

    public static XDocument BuildInvoiceXml(AppSettings s, ProviderInvoiceRequest r)
    {
        var items = r.Lines.Select(l =>
        {
            var (net, vat) = InvoicingProviders.Split(l.GrossAmount, l.VatCode);
            return new XElement(Ns + "tetel",
                new XElement(Ns + "megnevezes", l.Name),
                new XElement(Ns + "mennyiseg", "1"),
                new XElement(Ns + "mennyisegiEgyseg", "hó"),
                new XElement(Ns + "nettoEgysegar", Amount(net)),
                new XElement(Ns + "afakulcs", l.VatCode),
                new XElement(Ns + "nettoErtek", Amount(net)),
                new XElement(Ns + "afaErtek", Amount(vat)),
                new XElement(Ns + "bruttoErtek", Amount(l.GrossAmount)));
        });
        var fejlec = new XElement(Ns + "fejlec",
            new XElement(Ns + "keltDatum", Date(r.IssueDate)),
            new XElement(Ns + "teljesitesDatum", Date(r.FulfillmentDate)),
            new XElement(Ns + "fizetesiHataridoDatum", Date(r.DueDate)),
            new XElement(Ns + "fizmod", "Átutalás"),
            new XElement(Ns + "penznem", "HUF"),
            new XElement(Ns + "szamlaNyelve", "hu"),
            new XElement(Ns + "megjegyzes", r.Comment),
            new XElement(Ns + "elolegszamla", "false"),
            new XElement(Ns + "vegszamla", "false"),
            new XElement(Ns + "dijbekero", "false"));
        if (!string.IsNullOrWhiteSpace(s.SzamlazzInvoicePrefix)) fejlec.Add(new XElement(Ns + "szamlaszamElotag", s.SzamlazzInvoicePrefix.Trim()));

        var vevo = new XElement(Ns + "vevo",
            new XElement(Ns + "nev", r.Buyer.Name),
            new XElement(Ns + "irsz", r.Buyer.PostCode),
            new XElement(Ns + "telepules", r.Buyer.City),
            new XElement(Ns + "cim", r.Buyer.Street));
        // A Számlázz.hu vevő-adata egyetlen címet fogad: több címzettnél az első kerül a számlára.
        if (Notifications.EmailAddresses.First(r.Buyer.Email) is { } buyerEmail) vevo.Add(new XElement(Ns + "email", buyerEmail));
        // A levelet a Lakáskezelő küldi, a Számlázz.hu ne.
        vevo.Add(new XElement(Ns + "sendEmail", "false"));
        if (!string.IsNullOrWhiteSpace(r.Buyer.TaxNumber)) vevo.Add(new XElement(Ns + "adoszam", r.Buyer.TaxNumber));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "xmlszamla",
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
                new XAttribute(Xsi + "schemaLocation", "http://www.szamlazz.hu/xmlszamla https://www.szamlazz.hu/szamla/docs/xsds/agent/xmlszamla.xsd"),
                Settings(Ns, s),
                fejlec,
                new XElement(Ns + "elado"),
                vevo,
                new XElement(Ns + "tetelek", items)));
    }

    public static XDocument BuildStornoXml(AppSettings s, string originalNumber, DateOnly today) =>
        new(new XDeclaration("1.0", "UTF-8", null),
            new XElement(NsSt + "xmlszamlast",
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
                new XAttribute(Xsi + "schemaLocation", "http://www.szamlazz.hu/xmlszamlast https://www.szamlazz.hu/szamla/docs/xsds/agentst/xmlszamlast.xsd"),
                Settings(NsSt, s),
                new XElement(NsSt + "fejlec",
                    new XElement(NsSt + "szamlaszam", originalNumber),
                    new XElement(NsSt + "keltDatum", Date(today)),
                    new XElement(NsSt + "teljesitesDatum", Date(today)),
                    new XElement(NsSt + "tipus", "SS")),
                new XElement(NsSt + "elado"),
                new XElement(NsSt + "vevo")));

    public static XDocument BuildPdfXml(AppSettings s, string number) =>
        new(new XDeclaration("1.0", "UTF-8", null),
            new XElement(NsPdf + "xmlszamlapdf",
                new XElement(NsPdf + "szamlaagentkulcs", s.SzamlazzAgentKey),
                new XElement(NsPdf + "szamlaszam", number),
                new XElement(NsPdf + "valaszVerzio", "2")));

    private static XElement Settings(XNamespace ns, AppSettings s) => new(ns + "beallitasok",
        new XElement(ns + "szamlaagentkulcs", s.SzamlazzAgentKey),
        new XElement(ns + "eszamla", "false"),
        new XElement(ns + "szamlaLetoltes", "true"),
        new XElement(ns + "valaszVerzio", "2"));

    private static string Amount(decimal v) => v.ToString("0.##", Inv);
    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);

    // ----- hívások -----

    private record AgentResponse(string? Number, byte[]? Pdf);

    private async Task<AgentResponse> CallAsync(string action, XDocument xml, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml.Declaration + "\n" + xml.Root));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        form.Add(file, action, "agent.xml");

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(Url, form, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderException("Nem sikerült kapcsolódni a Számlázz.hu-hoz.", ex);
        }
        using (response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            string? Header(string name) => response.Headers.TryGetValues(name, out var v) ? Uri.UnescapeDataString(v.First()) : null;
            var errorCode = Header("szlahu_error_code");
            var error = Header("szlahu_error");
            var number = Header("szlahu_szamlaszam");

            // Közvetlen PDF (1-es válaszverzió).
            if (response.Content.Headers.ContentType?.MediaType == "application/pdf" && string.IsNullOrEmpty(errorCode))
            {
                return new AgentResponse(number, bytes);
            }

            XDocument? doc = null;
            try { doc = XDocument.Parse(Encoding.UTF8.GetString(bytes)); } catch (System.Xml.XmlException) { /* nem XML */ }
            string? Value(string local) => doc?.Descendants().FirstOrDefault(e => e.Name.LocalName == local)?.Value;
            var success = Value("sikeres");
            if (!string.IsNullOrEmpty(errorCode) || success == "false" || !response.IsSuccessStatusCode)
            {
                var message = Value("hibauzenet") ?? error ?? $"HTTP {(int)response.StatusCode}";
                var code = Value("hibakod") ?? errorCode;
                logger.LogWarning("Számlázz.hu {Action} failed: {Code} {Message}", action, code, message);
                throw new ProviderException(code is "3" ? "A Számlázz.hu elutasította a Számla Agent kulcsot. Ellenőrizd a Beállításokban."
                    : $"Számlázz.hu hiba{(code is null ? "" : $" ({code})")}: {message}");
            }
            var pdf = Value("pdf") is { Length: > 0 } b64 ? Convert.FromBase64String(b64) : null;
            return new AgentResponse(Value("szamlaszam") ?? number, pdf);
        }
    }

    public async Task<ProviderInvoice> IssueAsync(AppSettings s, ProviderInvoiceRequest r, CancellationToken ct)
    {
        var res = await CallAsync("action-xmlagentxmlfile", BuildInvoiceXml(s, r), ct);
        if (string.IsNullOrWhiteSpace(res.Number)) throw new ProviderException("A Számlázz.hu nem adott vissza számlaszámot.");
        return new ProviderInvoice(res.Number, res.Number, res.Pdf);
    }

    public async Task<ProviderInvoice> CancelAsync(AppSettings s, Invoice original, CancellationToken ct)
    {
        var res = await CallAsync("action-szamla_agent_st", BuildStornoXml(s, original.ProviderDocumentId ?? original.Number, DateOnly.FromDateTime(DateTime.Now)), ct);
        if (string.IsNullOrWhiteSpace(res.Number)) throw new ProviderException("A Számlázz.hu nem adott vissza sztornó számlaszámot.");
        return new ProviderInvoice(res.Number, res.Number, res.Pdf);
    }

    public async Task<byte[]> DownloadPdfAsync(AppSettings s, Invoice invoice, CancellationToken ct)
    {
        var res = await CallAsync("action-szamla_agent_pdf", BuildPdfXml(s, invoice.ProviderDocumentId ?? invoice.Number), ct);
        return res.Pdf ?? throw new ProviderException("A Számlázz.hu nem adta vissza a számla PDF-jét.");
    }

    // Kapcsolatpróba: egy nem létező számla PDF-jét kérjük. Hibás kulcsnál bejelentkezési hiba jön (3-as kód);
    // ha csak a számla nem létezik, a kulcs jó.
    public async Task<ProviderTestResult> TestAsync(AppSettings s, CancellationToken ct)
    {
        try
        {
            await CallAsync("action-szamla_agent_pdf", BuildPdfXml(s, "LAKASKEZELO-KAPCSOLATPROBA"), ct);
            return new ProviderTestResult(true, null);
        }
        catch (ProviderException ex) when (ex.Message.Contains("kulcs") || ex.InnerException is not null)
        {
            return new ProviderTestResult(false, ex.Message);
        }
        catch (ProviderException)
        {
            return new ProviderTestResult(true, null);
        }
    }
}
