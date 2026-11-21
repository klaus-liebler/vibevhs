using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using VibeVHS.Adapters.Mocks;
using VibeVHS.Application.Portal;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Web.Portal;

/// <summary>Endpunkte des Dozierendenportals: Anmeldelink, Kalender, Downloads, Uploads (Formulare mit Antiforgery).</summary>
public static class PortalApi
{
    public const string Basis = "/portal/dozierende";

    public static IEndpointRouteBuilder MapPortalApi(this IEndpointRouteBuilder app)
    {
        // Anmeldelink aus der Mail: erst eine Seite mit Knopf (Mail-Scanner öffnen Links), dann POST → Anmeldung.
        app.MapGet($"{Basis}/link/{{token}}", (string token) => Seite("Anmelden",
            $"""<p>Willkommen im Dozierendenportal. Tippen Sie auf „Anmelden“, um fortzufahren.</p><form method="post"><input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}"><button type="submit">Anmelden</button></form>"""));
        app.MapPost($"{Basis}/link/{{token}}", async ([FromForm] string token, HttpContext http, DozentenportalDienst portal) =>
        {
            var konto = await portal.LinkEinloesenAsync(token);
            if (konto is null)
            {
                return Seite("Link ungültig", $"""<p role="alert">Der Anmeldelink ist abgelaufen oder wurde schon benutzt. <a href="{Basis.TrimStart('/')}/anmelden">Neuen Link anfordern</a>.</p>""", 410);
            }

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                MockAnmeldung.Anmelden(konto.AlsBenutzer(Rolle.Dozierende), CookieAuthenticationDefaults.AuthenticationScheme));
            return Results.LocalRedirect("~" + Basis);
        }).DisableAntiforgery();

        // Kalender-Abo (ohne Anmeldung, geheime Adresse)
        app.MapGet($"{Basis}/kalender/{{token}}.ics", async (string token, DozentenportalDienst portal, HttpContext http) =>
        {
            var ics = await portal.KalenderAsync(token);
            http.Response.Headers.CacheControl = "no-store";
            return ics is null ? Results.NotFound() : Results.Text(ics, "text/calendar; charset=utf-8", Encoding.UTF8);
        });

        app.MapGet($"{Basis}/termin/{{id:guid}}.ics", (Guid id, DozentenportalDienst portal) => Ausfuehren(async () =>
        {
            var (datei, inhalt) = await portal.TerminIcsAsync(id);
            return Results.File(Encoding.UTF8.GetBytes(inhalt), "text/calendar", datei);
        })).RequireAuthorization();

        app.MapGet($"{Basis}/honorar/{{id:guid}}.pdf", (Guid id, DozentenportalDienst portal) => Ausfuehren(async () =>
        {
            var (datei, pdf) = await portal.AbrechnungPdfAsync(id);
            return Results.File(pdf, "application/pdf", datei);
        })).RequireAuthorization();

        app.MapPost($"{Basis}/kurs/{{id:guid}}/unterlagen", async (Guid id, [FromForm] string? titel, [FromForm] string? verteilen, IFormFile? datei,
            DozentenportalDienst portal) =>
        {
            var zurueck = $"~{Basis}/kurs/{id}";
            try
            {
                if (datei is null)
                {
                    throw new PortalFehler("Bitte eine Datei wählen.");
                }

                var n = await portal.UnterlageHochladenAsync(id, titel ?? "", datei.FileName, await LesenAsync(datei), verteilen == "ja");
                return Results.LocalRedirect($"{zurueck}?ok={Uri.EscapeDataString(verteilen == "ja" ? $"Hochgeladen und an {n} Teilnehmende verschickt." : "Hochgeladen.")}");
            }
            catch (PortalFehler f) when (f.Status == 422)
            {
                return Results.LocalRedirect($"{zurueck}?fehler={Uri.EscapeDataString(f.Message)}");
            }
            catch (PortalFehler f)
            {
                return Results.Problem(f.Message, statusCode: f.Status);
            }
        }).RequireAuthorization();

        app.MapPost($"{Basis}/nachweis", async ([FromForm] string? merkmal, [FromForm] string? wert, [FromForm] DateOnly? bis, IFormFile? datei,
            DozentenportalDienst portal) =>
        {
            try
            {
                if (datei is null)
                {
                    throw new PortalFehler("Bitte eine Datei wählen.");
                }

                await portal.NachweisHochladenAsync(merkmal ?? "", wert, bis, datei.FileName, await LesenAsync(datei));
                return Results.LocalRedirect($"~{Basis}/daten?ok={Uri.EscapeDataString("Nachweis eingereicht – die Fachbereichsleitung prüft ihn.")}");
            }
            catch (PortalFehler f) when (f.Status == 422)
            {
                return Results.LocalRedirect($"~{Basis}/daten?fehler={Uri.EscapeDataString(f.Message)}");
            }
        }).RequireAuthorization();

        // Unterlage für Teilnehmende (Link aus der Mail, ohne Anmeldung)
        app.MapGet("/u/{token}", async (string token, DozentenportalDienst portal, HttpContext http) =>
        {
            var d = await portal.UnterlageAbrufenAsync(token);
            http.Response.Headers.CacheControl = "no-store";
            return d is null
                ? Seite("Unterlage nicht verfügbar", "<p>Diese Unterlage ist nicht (mehr) abrufbar. Bitte wenden Sie sich an Ihre Kursleitung.</p>", 404)
                : Results.File(d.Inhalt, d.Typ, d.Name);
        });

        // ---------- Teilnehmendenportal ----------
        const string Tn = "/portal/teilnehmende";
        app.MapGet($"{Tn}/link/{{token}}", (string token) => Seite("Meine vhs",
            $"""<p>Willkommen! Tippen Sie auf „Weiter“, um zu Ihren Kursen zu kommen.</p><form method="post"><input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}"><button type="submit">Weiter</button></form>"""));
        app.MapPost($"{Tn}/link/{{token}}", async ([FromForm] string token, HttpContext http, TeilnehmendenportalDienst portal) =>
        {
            var konto = await portal.LinkEinloesenAsync(token);
            if (konto is null)
            {
                return Seite("Link ungültig", $"""<p role="alert">Der Link ist abgelaufen oder wurde schon benutzt. <a href="{Tn.TrimStart('/')}/anmelden">Neuen Link anfordern</a>.</p>""", 410);
            }

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                MockAnmeldung.Anmelden(konto.AlsBenutzer(Rolle.Teilnehmende), CookieAuthenticationDefaults.AuthenticationScheme));
            return Results.LocalRedirect("~" + Tn);
        }).DisableAntiforgery();
        app.MapGet($"{Tn}/kalender/{{token}}.ics", async (string token, TeilnehmendenportalDienst portal, HttpContext http) =>
        {
            var ics = await portal.KalenderAsync(token);
            http.Response.Headers.CacheControl = "no-store";
            return ics is null ? Results.NotFound() : Results.Text(ics, "text/calendar; charset=utf-8", Encoding.UTF8);
        });
        app.MapGet($"{Tn}/termin/{{id:guid}}.ics", (Guid id, TeilnehmendenportalDienst portal) => Ausfuehren(async () =>
        {
            var (datei, inhalt) = await portal.TerminIcsAsync(id);
            return Results.File(Encoding.UTF8.GetBytes(inhalt), "text/calendar", datei);
        })).RequireAuthorization();
        app.MapGet($"{Tn}/dokumente/{{id:guid}}.pdf", (Guid id, TeilnehmendenportalDienst portal, HttpContext http) => Ausfuehren(async () =>
        {
            var (datei, pdf) = await portal.DokumentPdfAsync(id);
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(pdf, "application/pdf", datei);
        })).RequireAuthorization();
        app.MapGet($"{Tn}/unterlagen/{{id:guid}}", (Guid id, TeilnehmendenportalDienst portal, HttpContext http) => Ausfuehren(async () =>
        {
            var d = await portal.UnterlageAsync(id);
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(d.Inhalt, d.Typ, d.Name);
        })).RequireAuthorization();
        app.MapGet($"{Tn}/email/{{token}}", (string token) => Seite("E-Mail-Adresse bestätigen",
            $"""<p>Tippen Sie auf „Bestätigen“, damit wir Ihnen an diese Adresse schreiben.</p><form method="post"><input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}"><button type="submit">Bestätigen</button></form>"""));
        app.MapPost($"{Tn}/email/{{token}}", async ([FromForm] string token, TeilnehmendenportalDienst portal) =>
            await portal.EMailBestaetigenAsync(token)
                ? Seite("Danke!", """<p role="status">Ihre neue E-Mail-Adresse ist bestätigt.</p>""")
                : Seite("Link ungültig", """<p role="alert">Der Link ist abgelaufen oder wurde schon benutzt. Bitte ändern Sie die Adresse in „Meine Daten“ noch einmal.</p>""", 410))
            .DisableAntiforgery();

        // Datei zu einem Anliegen (Verwaltung)
        app.MapGet("/portal-anliegen/{id:guid}/datei", async (Guid id, PortalVerwaltung verwaltung) =>
        {
            try
            {
                var a = await verwaltung.AntragAsync(id);
                return a.DateiId is { } did && await verwaltung.DateiAsync(did) is { } d ? Results.File(d.Inhalt, d.Typ, d.Name) : Results.NotFound();
            }
            catch (PortalFehler f)
            {
                return Results.Problem(f.Message, statusCode: f.Status);
            }
        }).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Ausfuehren(Func<Task<IResult>> arbeit)
    {
        try
        {
            return await arbeit();
        }
        catch (PortalFehler f)
        {
            return Results.Problem(f.Message, statusCode: f.Status);
        }
    }

    private static async Task<byte[]> LesenAsync(IFormFile datei)
    {
        if (datei.Length > Uploadregeln.MaxBytes)
        {
            throw new PortalFehler($"Die Datei ist zu groß. {Uploadregeln.Hinweis}");
        }

        using var ms = new MemoryStream();
        await datei.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static IResult Seite(string titel, string inhalt, int status = 200)
        => new VibeVHS.Web.Shop.HtmlSeite(titel, inhalt, status, " · Dozierendenportal");
}
