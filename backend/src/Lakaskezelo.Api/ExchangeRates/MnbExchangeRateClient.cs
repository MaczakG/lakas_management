using System.Globalization;
using System.Net;
using System.Xml.Linq;

namespace Lakaskezelo.Api.ExchangeRates;

public record MnbRate(string CurrencyCode, decimal RateToHuf, DateOnly RateDate);

// Az MNB (Magyar Nemzeti Bank) hivatalos árfolyam-webszolgáltatásának kliense
// (http://www.mnb.hu/arfolyamok.asmx, GetCurrentExchangeRates SOAP metódus).
//
// Fontos: a HTTPS végpontot (https://www.mnb.hu/...) az MNB WAF-ja blokkolja szkriptelt POST
// kérésekre (404-et ad minden nem böngészős klienstől, tesztelve curl-lal és .NET
// HttpClient/HttpWebRequest-tel is) — viszont a régi, sima HTTP végpont (http://www.mnb.hu/...,
// 80-as port) megbízhatóan válaszol. Ez nem elírás: a webszolgáltatás máig ezen fut.
public class MnbExchangeRateClient(HttpClient http, ILogger<MnbExchangeRateClient> logger)
{
    private const string EndpointUrl = "http://www.mnb.hu/arfolyamok.asmx";
    private const string SoapAction = "/webservices/MNBArfolyamServiceSoap/GetCurrentExchangeRates";

    private static readonly XNamespace SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace MnbNs = "http://www.mnb.hu/webservices/";

    public async Task<List<MnbRate>> FetchCurrentRatesAsync(CancellationToken ct)
    {
        var requestBody = """
            <?xml version="1.0" encoding="utf-8" ?>
            <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:web="http://www.mnb.hu/webservices/">
                <soapenv:Header/>
                <soapenv:Body>
                    <web:GetCurrentExchangeRates/>
                </soapenv:Body>
            </soapenv:Envelope>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
        {
            Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "text/xml"),
        };
        request.Headers.Add("SOAPAction", SoapAction);
        request.Headers.Add("Accept", "application/xml");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var responseXml = await response.Content.ReadAsStringAsync(ct);

        var envelope = XDocument.Parse(responseXml);
        var resultElement = envelope.Descendants(MnbNs + "GetCurrentExchangeRatesResult").FirstOrDefault()
            ?? throw new InvalidOperationException("Az MNB válasz nem tartalmazza a GetCurrentExchangeRatesResult mezőt.");

        // A tényleges adat egy HTML-escapelt XML-string a SOAP body-n belül (MNB kvirkje) — külön
        // kell parse-olni, nem a külső SOAP dokumentum részeként.
        var innerXml = WebUtility.HtmlDecode(resultElement.Value);
        var innerDoc = XDocument.Parse(innerXml);

        var dayElement = innerDoc.Descendants("Day").FirstOrDefault();
        if (dayElement is null)
        {
            logger.LogWarning("MNB válasz nem tartalmaz Day elemet — nincs elérhető árfolyam.");
            return [];
        }

        var rateDate = DateOnly.Parse(dayElement.Attribute("date")!.Value, CultureInfo.InvariantCulture);

        var rates = new List<MnbRate>();
        foreach (var rateElement in dayElement.Elements("Rate"))
        {
            var currencyCode = rateElement.Attribute("curr")?.Value;
            var unitText = rateElement.Attribute("unit")?.Value ?? "1";
            if (currencyCode is null) continue;

            // Az MNB vesszőt használ tizedesjelölőként (pl. "365,03000"), és néhány devizánál
            // (pl. JPY, IDR) az árfolyam 100 egységre vonatkozik, nem 1-re — ezt jelzi a "unit" attribútum.
            var rawValue = decimal.Parse(rateElement.Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var unit = decimal.Parse(unitText, CultureInfo.InvariantCulture);
            var rateToHuf = rawValue / unit;

            rates.Add(new MnbRate(currencyCode, rateToHuf, rateDate));
        }

        return rates;
    }
}
