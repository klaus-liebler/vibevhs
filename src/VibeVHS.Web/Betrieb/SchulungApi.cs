using System.Security.Cryptography;
using System.Text;
using VibeVHS.Application.Betrieb;
using VibeVHS.Infrastructure.Betrieb;

namespace VibeVHS.Web.Betrieb;

/// <summary>
/// Steuerung des Schulungsbetriebs (QA-68): Wartungsseite während eines Sprungs und die Schnittstelle für das Werkzeug der Kursleitung
/// (tools/instanzen.ps1), geschützt mit dem Leitstandschlüssel im Kopf „X-Schulung-Schluessel“.
/// </summary>
public static class SchulungApi
{
    public const string Kopf = "X-Schulung-Schluessel";

    /// <summary>Während eines Sprungs: kurze Seite „Zeitsprung läuft“, die sich selbst neu lädt (nur Seitenaufrufe, keine Dateien).</summary>
    public static void UseSchulungWartung(this WebApplication app)
    {
        var uhr = app.Services.GetService<Spielzeit>();
        if (uhr is null)
        {
            return;
        }

        app.Use(async (kontext, weiter) =>
        {
            if (uhr.Wartung && !kontext.Request.Path.StartsWithSegments("/schulung") && HttpMethods.IsGet(kontext.Request.Method)
                && !Path.HasExtension(kontext.Request.Path.Value))
            {
                kontext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                kontext.Response.Headers.RetryAfter = "2";
                kontext.Response.ContentType = "text/html; charset=utf-8";
                await kontext.Response.WriteAsync("<!doctype html><meta charset=utf-8><meta http-equiv=refresh content=2><title>Zeitsprung</title>" +
                                                  "<p style=\"font:20px sans-serif;margin:20vh auto;text-align:center\">Zeitsprung läuft …</p>");
                return;
            }

            await weiter();
        });
    }

    public static void MapSchulungApi(this WebApplication app)
    {
        if (app.Services.GetService<Schulungsbetrieb>() is null)
        {
            return;
        }

        var gruppe = app.MapGroup("/schulung").AllowAnonymous().AddEndpointFilter(async (kontext, weiter) =>
        {
            var betrieb = kontext.HttpContext.RequestServices.GetRequiredService<Schulungsbetrieb>();
            var erwartet = betrieb.Optionen.Schluessel;
            var gesendet = kontext.HttpContext.Request.Headers[Kopf].ToString();
            if (string.IsNullOrEmpty(erwartet)
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(erwartet), Encoding.UTF8.GetBytes(gesendet)))
            {
                return Results.Problem("Leitstandschlüssel fehlt oder ist falsch.", statusCode: StatusCodes.Status403Forbidden);
            }

            return await weiter(kontext);
        });

        gruppe.MapGet("/stand", (Schulungsbetrieb b) => Results.Ok(b.Stand));
        gruppe.MapPost("/anhalten", (Schulungsbetrieb b) => { b.Uhr.Anhalten(); return Results.Ok(b.Stand); });
        gruppe.MapPost("/weiter", (Schulungsbetrieb b) => { b.Uhr.Weiter(); return Results.Ok(b.Stand); });
        gruppe.MapPost("/tempo/{wert}", (Schulungsbetrieb b, string wert) =>
            double.TryParse(wert.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t)
                ? Results.Ok(Tempo(b, t))
                : Results.BadRequest("Tempo als Zahl, z. B. 1, 0.1 oder 60."));
        gruppe.MapPost("/springen/{marke}", async (Schulungsbetrieb b, string marke, CancellationToken abbruch) =>
        {
            try
            {
                await b.SpringenAsync(marke, abbruch);
                return Results.Ok(b.Stand);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });
        gruppe.MapPost("/sperren", (Schulungsbetrieb b) => { b.TeilnehmerSteuernSetzen(false); return Results.Ok(b.Stand); });
        gruppe.MapPost("/freigeben", (Schulungsbetrieb b) => { b.TeilnehmerSteuernSetzen(true); return Results.Ok(b.Stand); });
    }

    private static Spielzeitstand Tempo(Schulungsbetrieb b, double t)
    {
        b.Uhr.TempoSetzen(t);
        return b.Stand;
    }
}
