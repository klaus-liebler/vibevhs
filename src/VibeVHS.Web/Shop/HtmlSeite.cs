using System.Net;
using System.Text;

namespace VibeVHS.Web.Shop;

/// <summary>
/// Schlichte HTML-Antwortseite (Bestätigungen, Fehler) für Shop- und Portal-Links. Das Stylesheet wird über die Pfad-Basis
/// adressiert, damit die Seite auch unter einem Unterpfad (z. B. /vibevhs) gestaltet ist.
/// </summary>
internal sealed class HtmlSeite(string titel, string inhalt, int status, string titelZusatz = "") : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var css = WebUtility.HtmlEncode($"{httpContext.Request.PathBase}/shop/seite.css");
        return Results.Content($"""
            <!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{titel}{titelZusatz}</title><link rel="stylesheet" href="{css}"></head>
            <body><main><h1>{titel}</h1>{inhalt}</main></body></html>
            """, "text/html; charset=utf-8", Encoding.UTF8, status).ExecuteAsync(httpContext);
    }
}
