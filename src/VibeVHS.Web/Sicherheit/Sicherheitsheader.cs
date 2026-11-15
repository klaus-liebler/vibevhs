using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace VibeVHS.Web.Sicherheit;

/// <summary>
/// Sicherheitsheader (OWASP Secure Headers) und Rate-Limits für die öffentlich erreichbaren Seiten
/// (Anmeldung, Check-in, Übersetzungslinks). Einstellbar über <c>Sicherheit:RateLimitProMinute</c>.
/// </summary>
internal static class Sicherheitsheader
{
    /// <summary>
    /// Content-Security-Policy: nur eigene Quellen. 'unsafe-inline' bleibt vorerst für Inline-Styles (Radzen) und
    /// zwei Inline-Handler (Rollenwechsel, Formulare) nötig – offener Punkt in docs/sicherheit.md.
    /// </summary>
    public const string Csp = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; "
                              + "font-src 'self' data:; connect-src 'self' ws: wss:; media-src 'self' blob: mediastream:; worker-src 'self' blob:; "
                              + "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public static readonly string[] OeffentlichePfade = ["/anmelden", "/checkin", "/brief", "/uebersetzen", "/k"];

    public static IApplicationBuilder UseSicherheitsheader(this IApplicationBuilder app)
        => app.Use(async (kontext, weiter) =>
        {
            kontext.Response.OnStarting(() =>
            {
                var h = kontext.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "DENY";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=(), payment=(), usb=()";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                h["Content-Security-Policy"] = Csp;
                if (kontext.Request.Path.StartsWithSegments("/dokumente") || kontext.Request.Path.StartsWithSegments("/finanzen")
                    || kontext.Request.Path.StartsWithSegments("/personen") || kontext.Request.Path.StartsWithSegments("/auswertungen"))
                {
                    // Personen- und Finanzdaten nicht im Browser- oder Proxy-Cache ablegen.
                    h["Cache-Control"] = "no-store";
                }

                if (kontext.Request.Path.Equals("/shop/shop.js", StringComparison.OrdinalIgnoreCase))
                {
                    // Die Web-Komponente wird als ES-Modul von fremden Websites (z. B. TYPO3) geladen; Module brauchen CORS.
                    // Unbedenklich: öffentliche, statische Datei ohne Zugangsdaten.
                    h["Access-Control-Allow-Origin"] = "*";
                    h["Cross-Origin-Resource-Policy"] = "cross-origin";
                }

                return Task.CompletedTask;
            });
            await weiter();
        });

    public static IServiceCollection AddRateLimits(this IServiceCollection dienste, IConfiguration konfiguration)
    {
        var proMinute = konfiguration.GetValue("Sicherheit:RateLimitProMinute", 60);
        return dienste.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (k, _) =>
            {
                k.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(k =>
            {
                var pfad = OeffentlichePfade.FirstOrDefault(p => k.Request.Path.StartsWithSegments(p));
                return pfad is null
                    ? RateLimitPartition.GetNoLimiter("frei")
                    : RateLimitPartition.GetFixedWindowLimiter($"{pfad}|{k.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = proMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });
    }
}
