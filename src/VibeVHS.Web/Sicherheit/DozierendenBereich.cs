using VibeVHS.Adapters.Mocks;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Web.Sicherheit;

/// <summary>
/// Kursleitungen (aktive Rolle „Dozierende“) und Teilnehmende arbeiten nur in ihrem Portal: Verwaltungsseiten (Personen, Belegungen,
/// Finanzen …) leiten dorthin um, Downloads der Verwaltung antworten mit 403. Die Dienste prüfen zusätzlich selbst (nur eigene Daten).
/// </summary>
internal static class DozierendenBereich
{
    private static readonly string[] Allgemein =
    [
        "/_blazor", "/_framework", "/_content", "/lib", "/abmelden", "/rolle-wechseln", "/anmelden", "/checkin", "/k", "/u", "/shop",
        "/api/v1/shop", "/brief", "/uebersetzen", "/not-found", "/Error",
    ];

    private static readonly Dictionary<string, string> Portal = new(StringComparer.Ordinal)
    {
        [nameof(Rolle.Dozierende)] = "/portal/dozierende",
        [nameof(Rolle.Teilnehmende)] = "/portal/teilnehmende",
    };

    /// <summary>Statische Dateien liegen im Wurzelverzeichnis (app.css, scanner.js …); Downloads wie /personen/…/auskunft.json bleiben gesperrt.</summary>
    private static bool StatischeDatei(PathString pfad) => pfad.Value is { } p && p.LastIndexOf('/') == 0 && Path.HasExtension(p);

    public static IApplicationBuilder UseDozierendenBereich(this IApplicationBuilder app)
        => app.Use(async (k, weiter) =>
        {
            if (k.User.Identity?.IsAuthenticated == true && k.User.FindFirst(MockAnmeldung.RollenClaim)?.Value is { } rolle && Portal.TryGetValue(rolle, out var portal)
                && !k.Request.Path.StartsWithSegments(portal) && !Allgemein.Any(p => k.Request.Path.StartsWithSegments(p)) && !StatischeDatei(k.Request.Path))
            {
                if (HttpMethods.IsGet(k.Request.Method) && (k.Request.Path == "/" || k.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.Ordinal)))
                {
                    k.Response.Redirect($"{k.Request.PathBase}{portal}");
                }
                else
                {
                    k.Response.StatusCode = StatusCodes.Status403Forbidden;
                }

                return;
            }

            await weiter();
        });
}
