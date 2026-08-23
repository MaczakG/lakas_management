using System.Net;

namespace Lakaskezelo.Api.Notifications;

// Egyszerű, márkajelzés nélküli e-mail sablon minden kimenő levélhez — sima inline-stílusú
// HTML (email kliensek a külső CSS-t/JS-t figyelmen kívül hagyják), lekerekítés nélkül, a felület
// design-elveivel összhangban.
public static class EmailTemplate
{
    private const string AccentColor = "#2563EB";

    public static string Render(string preheader, string heading, string bodyHtml, (string Text, string Url)? cta = null)
    {
        var ctaHtml = cta is { } c ? $"""
            <tr>
              <td align="center" style="padding:26px 0 6px;">
                <a href="{c.Url}" style="display:inline-block;background:{AccentColor};color:#ffffff;
                  text-decoration:none;font-weight:700;font-size:14px;padding:13px 30px;">{WebUtility.HtmlEncode(c.Text)}</a>
              </td>
            </tr>
            """ : "";

        return $"""
            <!DOCTYPE html>
            <html lang="hu">
            <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"><title>Lakáskezelő</title></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <span style="display:none;font-size:1px;color:#f1f5f9;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{WebUtility.HtmlEncode(preheader)}</span>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f1f5f9;padding:32px 16px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;">
                    <tr><td style="background:{AccentColor};padding:18px 28px;"><span style="color:#ffffff;font-weight:700;font-size:15px;">Lakáskezelő</span></td></tr>
                    <tr>
                      <td style="padding:28px 28px 6px;">
                        <h1 style="margin:0 0 16px;font-size:19px;font-weight:700;color:#0f172a;">{WebUtility.HtmlEncode(heading)}</h1>
                        <div style="font-size:14.5px;line-height:1.65;color:#334155;">{bodyHtml}</div>
                      </td>
                    </tr>
                    {ctaHtml}
                    <tr>
                      <td style="padding:20px 28px 26px;">
                        <hr style="border:none;border-top:1px solid #e2e8f0;margin:0 0 16px;">
                        <p style="margin:0;font-size:12px;color:#94a3b8;">Ezt az üzenetet a Lakáskezelő rendszer küldte automatikusan.</p>
                      </td>
                    </tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    // Egy ismétlődő értesítés (havi számla, rezsi emlékeztető) minden alkalommal ugyanazt a
    // tárgyat használná — időbélyeg nélkül a levelezőprogramok (Gmail stb.) egyetlen szálba
    // vonnák össze őket, és minden korábbi a legújabb alá tűnne el.
    public static string UniqueSubject(string subject) => $"{subject} · {DateTime.UtcNow:yyyy.MM.dd. HH:mm:ss}";
}
