using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VibeVHS.Application.Shop;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Web.Sicherheit;

namespace VibeVHS.Web.Shop;

/// <summary>
/// Öffentliche Buchungs-API für Webshop, TYPO3 und Portale unter <c>/api/v1/shop</c> (versioniert; Vertrag in <c>openapi-v1.json</c>).
/// CORS nur für konfigurierte Origins, Bot-Schutz per Proof-of-Work ohne Drittanbieter, Rate-Limits je IP.
/// </summary>
public static class ShopApi
{
    public const string Basis = "/api/v1/shop";
    public const string CorsRichtlinie = "shop";
    public const string Lesen = "shop-lesen";
    public const string Schreiben = "shop-schreiben";
    public const string Registrierung = "shop-registrierung";

    /// <summary>Benutzer im Protokoll für alles, was über die öffentliche API geschieht (keine Rolle, keine Automatik-Rechte).</summary>
    public static readonly Benutzer Webshop = new("webshop", "Webshop", null);

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddShopApi(this IServiceCollection dienste, IConfiguration konfiguration, IHostEnvironment umgebung)
    {
        var optionen = konfiguration.GetSection(ShopOptionen.Abschnitt).Get<ShopOptionen>() ?? new ShopOptionen();
        dienste.AddSingleton(optionen);
        var origins = konfiguration.GetSection("Shop:ErlaubteOrigins").Get<string[]>() ?? [];
        dienste.AddCors(o => o.AddPolicy(CorsRichtlinie, p => p
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT")
            .WithHeaders("Content-Type", "Idempotency-Key", "X-VHS-PoW", "Authorization", "If-None-Match", "Accept-Language")
            .WithExposedHeaders("ETag", "Location", "Retry-After")
            .SetPreflightMaxAge(TimeSpan.FromHours(1))));
        dienste.AddSingleton(new Arbeitsnachweis.Einstellungen(konfiguration.GetValue("Shop:PowBits", 16)));
        dienste.AddSingleton<Arbeitsnachweis>();
        dienste.AddSingleton(new KontoAnmeldung.Einstellungen(konfiguration.GetValue("Shop:MockIdp", umgebung.IsDevelopment())));
        dienste.AddSingleton<KontoAnmeldung>();
        dienste.Configure<RateLimiterOptions>(o =>
        {
            void Policy(string name, int erlaubt, TimeSpan fenster) => o.AddPolicy(name, k => RateLimitPartition.GetFixedWindowLimiter(
                $"{name}|{k.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions { PermitLimit = erlaubt, Window = fenster, QueueLimit = 0 }));
            Policy(Lesen, konfiguration.GetValue("Shop:RateLimit:LesenProMinute", 300), TimeSpan.FromMinutes(1));
            Policy(Schreiben, konfiguration.GetValue("Shop:RateLimit:SchreibenProMinute", 30), TimeSpan.FromMinutes(1));
            Policy(Registrierung, konfiguration.GetValue("Shop:RateLimit:RegistrierungProStunde", 5), TimeSpan.FromHours(1));
        });
        return dienste;
    }

    public static IEndpointRouteBuilder MapShopApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Basis).RequireCors(CorsRichtlinie).AddEndpointFilter(async (k, weiter) =>
        {
            k.HttpContext.RequestServices.GetRequiredService<BenutzerKontext>().Gesetzt = Webshop;
            try
            {
                return await weiter(k);
            }
            catch (ShopFehler f)
            {
                return Problem(f.Status, f.Code, f.Message, f.Details);
            }
            catch (JsonException)
            {
                return Problem(400, "json", "Die Anfrage ist kein gültiges JSON.");
            }
        });
        var lesen = api.MapGroup("").RequireRateLimiting(Lesen);
        var schreiben = api.MapGroup("").RequireRateLimiting(Schreiben);

        lesen.MapGet("/openapi.json", (IWebHostEnvironment env) =>
            Results.File(Path.Combine(AppContext.BaseDirectory, "Shop", "openapi-v1.json"), "application/json"));
        lesen.MapGet("/einstellungen", async (ShopDienst shop, Arbeitsnachweis pow, HttpContext http) =>
        {
            var e = await shop.EinstellungenAsync(http.RequestAborted);
            http.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Ok(new { e.AgbVersion, e.AgbUrl, e.DatenschutzVersion, e.DatenschutzUrl, e.MandatVersion, e.MandatText, e.ReservierungMinuten, e.Einrichtung, PowBits = pow.Bits });
        });
        lesen.MapGet("/pow", (Arbeitsnachweis pow, HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(pow.Ausstellen());
        });
        lesen.MapGet("/kurse", async (ShopDienst shop, HttpContext http, string? geaendertSeit, int? fachbereich, string? q, string? ort) =>
        {
            DateTimeOffset? seit = null;
            if (!string.IsNullOrEmpty(geaendertSeit))
            {
                seit = DateTimeOffset.TryParse(geaendertSeit, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var s)
                    ? s
                    : throw new ShopFehler("validierung", "geaendertSeit: Zeitpunkt im Format ISO 8601 angeben (z. B. 2026-11-15T08:00:00Z).", 400);
            }

            return MitEtag(http, await shop.KurseAsync(fachbereich, q, ort, seit, http.RequestAborted), "public, max-age=60");
        });
        lesen.MapGet("/kurse/{nummer}", async (string nummer, ShopDienst shop, HttpContext http) =>
            MitEtag(http, await shop.KursAsync(nummer, http.RequestAborted), "public, max-age=60"));
        lesen.MapGet("/kurse/{nummer}/verfuegbarkeit", async (string nummer, ShopDienst shop, HttpContext http) =>
        {
            var v = await shop.VerfuegbarkeitAsync(nummer, http.RequestAborted);
            http.Response.Headers.CacheControl = "public, max-age=30";
            return Results.Ok(v);
        });

        schreiben.MapPost("/warenkorb", async (WarenkorbEingabe eingabe, ShopDienst shop, Arbeitsnachweis pow, KontoAnmeldung konto, HttpContext http) =>
        {
            pow.Pruefen(http);
            var korb = await shop.WarenkorbAnlegenAsync(eingabe.Kursnummern ?? [], konto.KontoId(http), http.RequestAborted);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Created($"{Basis}/warenkorb/{korb.Id}", korb);
        });
        schreiben.MapGet("/warenkorb/{id:guid}", async (Guid id, ShopDienst shop, HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await shop.WarenkorbAsync(id, http.RequestAborted));
        });
        schreiben.MapPut("/warenkorb/{id:guid}/positionen", async (Guid id, WarenkorbEingabe eingabe, ShopDienst shop, HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await shop.PositionenSetzenAsync(id, eingabe.Kursnummern ?? [], http.RequestAborted));
        });
        schreiben.MapPost("/warenkorb/{id:guid}/buchen", async (Guid id, BuchungsAnfrage anfrage, ShopDienst shop, Arbeitsnachweis pow, KontoAnmeldung konto,
            HttpContext http, [FromHeader(Name = "Idempotency-Key")] string? schluessel) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            // Wiederholungen mit bekanntem Schlüssel brauchen keinen neuen Arbeitsnachweis (Netzabbruch, Doppelklick).
            var antwort = await shop.BuchenAsync(id, anfrage, schluessel, konto.KontoId(http), http.RequestAborted, () => pow.Pruefen(http));
            return antwort.Wiederholt ? Results.Ok(antwort) : Results.Created($"{Basis}/buchungen/{antwort.BuchungId}", antwort);
        });

        api.MapPost("/konto/registrierung", async (RegistrierungEingabe e, ShopDienst shop, Arbeitsnachweis pow, Application.Durchfuehrung.OeffentlicheAdresse adresse,
            HttpContext http) =>
        {
            pow.Pruefen(http);
            await shop.RegistrierenAsync(e.EMail ?? "", e.Vorname ?? "", e.Nachname ?? "", e.Geburtsdatum, e.Sprache,
                token => adresse.Link($"shop/konto/bestaetigen?token={Uri.EscapeDataString(token)}"), http.RequestAborted);
            return Results.Accepted(value: new { Hinweis = "Wenn die Adresse gültig ist, erhalten Sie eine E-Mail mit einem Bestätigungslink." });
        }).RequireRateLimiting(Registrierung);
        schreiben.MapPost("/konto/anmelden", async (AnmeldeEingabe e, ShopDienst shop, KontoAnmeldung konto) =>
        {
            if (!konto.MockAktiv)
            {
                return Problem(501, "idp", "Die Anmeldung erfolgt über den Identitätsdienst der Kommune (OIDC); der Test-Identitätsdienst ist abgeschaltet.");
            }

            return await shop.AktivesKontoAsync(e.EMail ?? "") is { } k
                ? Results.Ok(konto.Ausstellen(k.Id))
                : Problem(401, "anmeldung", "Kein bestätigtes Kundenkonto mit dieser Adresse.");
        });
        schreiben.MapGet("/konto/buchungen", async (ShopDienst shop, KontoAnmeldung konto, HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return konto.KontoId(http) is { } id ? Results.Ok(await shop.KontoBuchungenAsync(id, http.RequestAborted)) : NichtAngemeldet();
        });
        schreiben.MapPost("/konto/buchungen/{belegungId:guid}/storno", async (Guid belegungId, StornoEingabe? e, ShopDienst shop, KontoAnmeldung konto, HttpContext http) =>
            konto.KontoId(http) is { } id ? Results.Ok(await shop.KontoStornierenAsync(id, belegungId, e?.Grund, http.RequestAborted)) : NichtAngemeldet());

        // Kurs-QR (Verwaltung): einzeln als PNG/SVG und als Sammel-PDF fürs Programmheft. Inhalt immer …/k/{nummer}.
        app.MapGet("/kurse/qr/{nummer}.{format}", (string nummer, string format, Application.Durchfuehrung.OeffentlicheAdresse adresse,
            Application.Dokumente.IQrCode qr) => format switch
        {
            "png" => Results.File(qr.Png(KursQrAblauf.QrInhalt(adresse, nummer)), "image/png", $"kurs-qr-{nummer}.png"),
            "svg" => Results.File(Encoding.UTF8.GetBytes(qr.Svg(KursQrAblauf.QrInhalt(adresse, nummer))), "image/svg+xml", $"kurs-qr-{nummer}.svg"),
            _ => Results.NotFound(),
        }).RequireAuthorization();
        // Kurs-QR-Bogen fürs Programmheft; ?schnitt=1: gestrichelte Kartenränder zum Ausschneiden (Aushang).
        app.MapGet("/kurse/qr-bogen.pdf", async (Guid? semester, int? fb, int? schnitt, Application.Programm.IProgrammSpeicher programm,
            Application.Stammdaten.IStammdaten stammdaten, Application.Durchfuehrung.OeffentlicheAdresse adresse, IKursQrBogen bogen, TimeProvider uhr) =>
        {
            var semesterName = semester is { } sid ? (await programm.SemesterAsync()).FirstOrDefault(x => x.Id == sid)?.Name : null;
            // Entgelt zum heutigen Buchungsstand (nur mit Semester: dort sind die Entgelte geladen).
            var jetzt = uhr.GetLocalNow();
            var preise = semester is { } s2
                ? (await programm.KurseMitTerminenAsync(s2)).ToDictionary(k => k.Id, k => k.EntgeltFuerBuchung(jetzt))
                : [];
            static string? Preis(Domain.Programm.KursEntgelt? e) => e is null || e.Cent <= 0 ? null
                : $"{new Domain.Gemeinsam.Geld(e.Cent)}{(e.ErmaessigtCent is { } erm && erm < e.Cent ? $" · ermäßigt {new Domain.Gemeinsam.Geld(erm)}" : "")}";
            var kurse = (await programm.KurseAsync(new Application.Programm.KursFilter(semester, fb)))
                .Where(k => k.Sichtbarkeit == Domain.Programm.Sichtbarkeit.Oeffentlich && k.Status != Domain.Programm.Kursstatus.Abgesagt)
                .OrderBy(k => k.Fachbereich).ThenBy(k => k.Kursnummer, StringComparer.Ordinal)
                .Select(k => new KursQrEintrag(k.Kursnummer, k.Titel, k.ErsterTermin is { } b ? $"ab {b:dd.MM.yyyy, HH:mm} Uhr{(k.Ort is null ? "" : $" · {k.Ort}")}" : k.Ort,
                    KursQrAblauf.QrInhalt(adresse, k.Kursnummer), k.Fachbereich, Preis(preise.GetValueOrDefault(k.Id))))
                .ToList();
            var fachbereiche = (await stammdaten.FachbereicheAsync()).ToDictionary(f => f.Nr, f => f.Name);
            var titel = $"Kursprogramm{(semesterName is null ? "" : $" {semesterName}")}";
            var untertitel = $"Direkt anmelden per QR-Code{(fb is { } f && fachbereiche.TryGetValue(f, out var fn) ? $" · Fachbereich {f} {fn}" : "")}";
            return Results.File(bogen.Erzeugen(titel, kurse, new KursQrBogenOptionen(untertitel, fachbereiche, schnitt == 1)), "application/pdf",
                $"kurs-qr{(fb is { } g ? $"-fb{g}" : "")}{(schnitt == 1 ? "-aushang" : "")}.pdf");
        }).RequireAuthorization();

        // Bestätigungslink aus der Mail: erst eine Seite mit Knopf (Mail-Scanner öffnen Links, bestätigen aber nicht), dann POST.
        app.MapGet("/shop/konto/bestaetigen", (string? token) => Seite("Kundenkonto bestätigen",
            $"""<p>Bitte bestätigen Sie Ihre E-Mail-Adresse, um Ihr Kundenkonto anzulegen.</p><form method="post"><input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}"><button type="submit">Konto bestätigen</button></form>"""));
        app.MapPost("/shop/konto/bestaetigen", async ([FromForm] string token, IServiceProvider dienste) =>
        {
            dienste.GetRequiredService<BenutzerKontext>().Gesetzt = Webshop;
            try
            {
                await dienste.GetRequiredService<ShopDienst>().BestaetigenAsync(token);
                return Seite("Kundenkonto bestätigt", "<p role=\"status\">Vielen Dank! Ihr Kundenkonto ist bestätigt. Sie können sich jetzt anmelden und Kurse buchen – Ihre Buchungen finden Sie unter <a href=\"/portal/teilnehmende\">Meine vhs</a>.</p>");
            }
            catch (ShopFehler f)
            {
                return Seite("Bestätigung nicht möglich", $"<p role=\"alert\">{WebUtility.HtmlEncode(f.Message)}</p>", f.Status);
            }
        }).DisableAntiforgery().RequireRateLimiting(Registrierung);
        return app;
    }

    private static IResult NichtAngemeldet() => Problem(401, "anmeldung", "Bitte anmelden (Authorization: Bearer …).");

    internal static IResult Problem(int status, string code, string text, IReadOnlyDictionary<string, object?>? details = null)
    {
        var erweiterungen = new Dictionary<string, object?>(details ?? new Dictionary<string, object?>()) { ["code"] = code };
        return Results.Problem(text, statusCode: status, title: code, type: $"https://vibevhs.de/probleme/{code}", extensions: erweiterungen);
    }

    /// <summary>Antwort mit starkem ETag; passt If-None-Match, kommt 304 ohne Inhalt.</summary>
    private static IResult MitEtag<T>(HttpContext http, T wert, string cache)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(wert, Json);
        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"";
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = cache;
        http.Response.Headers.Vary = "Origin";
        return http.Request.Headers.IfNoneMatch.Any(v => v is not null && v.Split(',').Select(x => x.Trim()).Contains(etag))
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Bytes(bytes, "application/json; charset=utf-8");
    }

    private static IResult Seite(string titel, string inhalt, int status = 200)
        => Results.Content($"""
            <!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{titel}</title><link rel="stylesheet" href="/shop/seite.css"></head>
            <body><main><h1>{titel}</h1>{inhalt}</main></body></html>
            """, "text/html; charset=utf-8", Encoding.UTF8, status);

    public sealed record WarenkorbEingabe(IReadOnlyList<string>? Kursnummern);

    public sealed record RegistrierungEingabe(string? EMail, string? Vorname, string? Nachname, DateOnly? Geburtsdatum, string? Sprache);

    public sealed record AnmeldeEingabe(string? EMail);

    public sealed record StornoEingabe(string? Grund);
}

/// <summary>
/// Proof-of-Work als Bot-Schutz ohne Drittanbieter: Der Server stellt eine signierte, 5 Minuten gültige Aufgabe aus; der Browser sucht eine Zahl,
/// sodass SHA-256(aufgabe + ":" + zahl) mit <see cref="Bits"/> Null-Bits beginnt (Header <c>X-VHS-PoW: aufgabe:zahl</c>). Jede Aufgabe gilt einmal.
/// </summary>
public sealed class Arbeitsnachweis(IDataProtectionProvider schutz, TimeProvider uhr, Arbeitsnachweis.Einstellungen einstellungen)
{
    public sealed record Einstellungen(int Bits);

    public sealed record Aufgabe(string Challenge, int Bits, int GueltigSekunden);

    private static readonly TimeSpan Gueltig = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _verbraucht = new(StringComparer.Ordinal);
    private readonly IDataProtector _schutz = schutz.CreateProtector("VibeVHS.Shop.Arbeitsnachweis");

    public int Bits => einstellungen.Bits;

    public Aufgabe Ausstellen()
    {
        var inhalt = $"{uhr.GetUtcNow().ToUnixTimeSeconds()}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
        var challenge = Convert.ToBase64String(_schutz.Protect(Encoding.UTF8.GetBytes(inhalt))).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return new Aufgabe(challenge, Bits, (int)Gueltig.TotalSeconds);
    }

    /// <summary>Prüft den Header; wirft <see cref="ShopFehler"/> mit Status 428 bei fehlendem oder falschem Nachweis.</summary>
    public void Pruefen(HttpContext http)
    {
        if (Bits <= 0)
        {
            return;
        }

        var kopf = http.Request.Headers["X-VHS-PoW"].ToString();
        var trenner = kopf.LastIndexOf(':');
        if (trenner <= 0)
        {
            throw new ShopFehler("pow-fehlt", "Bitte zuerst die Aufgabe unter /pow lösen (Header X-VHS-PoW).", 428);
        }

        var challenge = kopf[..trenner];
        var jetzt = uhr.GetUtcNow();
        try
        {
            var b64 = challenge.Replace('-', '+').Replace('_', '/');
            var inhalt = Encoding.UTF8.GetString(_schutz.Unprotect(Convert.FromBase64String(b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '='))));
            var ausgestellt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(inhalt.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture));
            if (jetzt - ausgestellt > Gueltig)
            {
                throw new ShopFehler("pow-abgelaufen", "Die Aufgabe ist abgelaufen; bitte eine neue holen.", 428);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or OverflowException)
        {
            throw new ShopFehler("pow-ungueltig", "Die Aufgabe ist ungültig.", 428);
        }

        if (!Geloest(kopf, Bits))
        {
            throw new ShopFehler("pow-falsch", "Der Arbeitsnachweis stimmt nicht.", 428);
        }

        foreach (var alt in _verbraucht.Where(v => jetzt - v.Value > Gueltig).Select(v => v.Key).ToList())
        {
            _verbraucht.TryRemove(alt, out _);
        }

        if (!_verbraucht.TryAdd(challenge, jetzt))
        {
            throw new ShopFehler("pow-verbraucht", "Diese Aufgabe wurde schon verwendet; bitte eine neue holen.", 428);
        }
    }

    /// <summary>true, wenn SHA-256 von „aufgabe:zahl“ mit mindestens <paramref name="bits"/> Null-Bits beginnt.</summary>
    public static bool Geloest(string loesung, int bits)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(loesung));
        for (var i = 0; i < bits; i++)
        {
            if ((hash[i / 8] & (0x80 >> (i % 8))) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Löst eine Aufgabe (für Tests und Server-zu-Server-Clients wie TYPO3).</summary>
    public static string Loesen(string challenge, int bits)
    {
        for (long n = 0; ; n++)
        {
            var kandidat = $"{challenge}:{n}";
            if (Geloest(kandidat, bits))
            {
                return kandidat;
            }
        }
    }
}

/// <summary>
/// Anmeldung am Kundenkonto. Vorgesehen ist der Identitätsdienst der Kommune (OIDC, Access-Token als Bearer); bis dahin stellt ein
/// Test-Identitätsdienst (nur mit <c>Shop:MockIdp</c>) nach Angabe der E-Mail eines bestätigten Kontos ein signiertes, 1 Stunde gültiges Token aus.
/// </summary>
public sealed class KontoAnmeldung(IDataProtectionProvider schutz, KontoAnmeldung.Einstellungen einstellungen)
{
    public sealed record Einstellungen(bool MockIdp);

    public sealed record Token(string AccessToken, string TokenType, int ExpiresIn);

    private readonly ITimeLimitedDataProtector _schutz = schutz.CreateProtector("VibeVHS.Shop.Konto").ToTimeLimitedDataProtector();

    public bool MockAktiv => einstellungen.MockIdp;

    public Token Ausstellen(Guid kontoId) => new(_schutz.Protect(kontoId.ToString("N"), TimeSpan.FromHours(1)), "Bearer", 3600);

    public Guid? KontoId(HttpContext http)
    {
        var kopf = http.Request.Headers.Authorization.ToString();
        if (!kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return Guid.TryParseExact(_schutz.Unprotect(kopf[7..].Trim()), "N", out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
