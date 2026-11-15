using VibeVHS.Adapters.Mocks;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Web.Sicherheit;

/// <summary>
/// Kursleitungen (aktive Rolle „Dozierende“) arbeiten nur im Dozierendenportal: Verwaltungsseiten (Personen, Belegungen, Finanzen …)
/// leiten dorthin um, Downloads der Verwaltung antworten mit 403. Die Dienste prüfen zusätzlich selbst (nur eigene Kurse und Termine).
/// </summary>
internal static class DozierendenBereich
{
    private static readonly string[] Erlaubt =
    [
        "/portal/dozierende", "/_blazor", "/_framework", "/_content", "/lib", "/abmelden", "/rolle-wechseln", "/anmelden", "/checkin", "/k", "/u", "/shop",
        "/api/v1/shop", "/brief", "/uebersetzen", "/not-found", "/Error",
    ];

    /// <summary>Statische Dateien liegen im Wurzelverzeichnis (app.css, scanner.js …); Downloads wie /personen/…/auskunft.json bleiben gesperrt.</summary>
    private static bool StatischeDatei(PathString pfad) => pfad.Value is { } p && p.LastIndexOf('/') == 0 && Path.HasExtension(p);

    public static IApplicationBuilder UseDozierendenBereich(this IApplicationBuilder app)
        => app.Use(async (k, weiter) =>
        {
            if (k.User.Identity?.IsAuthenticated == true && k.User.FindFirst(MockAnmeldung.RollenClaim)?.Value == nameof(Rolle.Dozierende)
                && !Erlaubt.Any(p => k.Request.Path.StartsWithSegments(p)) && !StatischeDatei(k.Request.Path) && k.Request.Path != "/")
            {
                if (HttpMethods.IsGet(k.Request.Method) && k.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.Ordinal))
                {
                    k.Response.Redirect($"{k.Request.PathBase}/portal/dozierende");
                }
                else
                {
                    k.Response.StatusCode = StatusCodes.Status403Forbidden;
                }

                return;
            }

            if (k.User.Identity?.IsAuthenticated == true && k.User.FindFirst(MockAnmeldung.RollenClaim)?.Value == nameof(Rolle.Dozierende) && k.Request.Path == "/")
            {
                k.Response.Redirect($"{k.Request.PathBase}/portal/dozierende");
                return;
            }

            await weiter();
        });
}
