using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Shop;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Shop;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Shop;
using VibeVHS.Web.Sicherheit;
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>Integrationstests der Buchungs-API über echtes HTTP (Kestrel im Testprozess, SQLite-Datei).</summary>
public sealed class ShopApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> KorbAsync(ShopTestHost h, params string[] nummern)
        => h.SendenAsync(HttpMethod.Post, "warenkorb", new { kursnummern = nummern }, nachweis: true);

    [Fact]
    public async Task Reservierung_haelt_den_Platz_und_verfaellt_nach_15_Minuten_per_Job()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-901", max: 1, Buchungsrichtlinie.Oeffentlich with { Warteliste = false });

        var a = await KorbAsync(h, "26H-901");
        a.StatusCode.ShouldBe(HttpStatusCode.Created);
        var korbA = await JsonAsync(a);
        korbA.GetProperty("positionen")[0].GetProperty("warteliste").GetBoolean().ShouldBeFalse();
        korbA.GetProperty("restSekunden").GetInt32().ShouldBeInRange(890, 900);

        // Der einzige Platz ist reserviert: ein zweiter Warenkorb bekommt ihn nicht (keine Warteliste).
        var b = await KorbAsync(h, "26H-901");
        b.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(b)).GetProperty("buchungscode").GetString().ShouldBe("ausgebucht");
        var v = await JsonAsync(await h.Http.GetAsync("kurse/26H-901/verfuegbarkeit", Ct));
        v.GetProperty("reserviert").GetInt32().ShouldBe(1);
        v.GetProperty("frei").GetInt32().ShouldBe(0);

        // Auch der Tresen sieht die Reservierung (dieselbe Buchungsprüfung).
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            (await bereich.ServiceProvider.GetRequiredService<IPlatzreservierungen>().ReserviertAsync(
                (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Kurse.SingleAsync(k => k.Kursnummer == "26H-901", Ct)).Id, null, Ct)).ShouldBe(1);
        }

        // 15 Minuten später gibt der Job den Platz frei und protokolliert das.
        h.Uhr.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = ShopApi.Webshop;
            (await bereich.ServiceProvider.GetRequiredService<ShopDienst>().VerfallenAsync(Ct)).ShouldBe(1);
            (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Protokoll.CountAsync(p => p.Aktion == "Shop.ReservierungVerfallen", Ct)).ShouldBe(1);
        }

        var abgelaufen = await h.SendenAsync(HttpMethod.Post, $"warenkorb/{korbA.GetProperty("id").GetGuid()}/buchen", Anfrage(), nachweis: true, idempotenz: Guid.NewGuid().ToString());
        abgelaufen.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await JsonAsync(abgelaufen)).GetProperty("code").GetString().ShouldBe("reservierung-abgelaufen");
        (await KorbAsync(h, "26H-901")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Buchen_ist_idempotent_und_lehnt_abweichende_Wiederholung_ab()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-902", max: 5);
        var korb = (await JsonAsync(await KorbAsync(h, "26H-902"))).GetProperty("id").GetGuid();
        var schluessel = Guid.NewGuid().ToString();

        (await h.SendenAsync(HttpMethod.Post, $"warenkorb/{korb}/buchen", Anfrage("lastschrift"), nachweis: false, idempotenz: schluessel))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        var erst = await h.SendenAsync(HttpMethod.Post, $"warenkorb/{korb}/buchen", Anfrage("lastschrift"), nachweis: true, idempotenz: schluessel);
        erst.StatusCode.ShouldBe(HttpStatusCode.Created);
        var e = await JsonAsync(erst);
        e.GetProperty("mandatsreferenz").GetString().ShouldStartWith("VHS-");
        e.GetProperty("positionen")[0].GetProperty("status").GetString().ShouldBe("angemeldet");

        // Gleiche Anfrage, gleicher Schlüssel (z. B. Netzabbruch): dieselbe Buchung, kein zweiter Arbeitsnachweis nötig.
        var nochmal = await h.SendenAsync(HttpMethod.Post, $"warenkorb/{korb}/buchen", Anfrage("lastschrift"), idempotenz: schluessel);
        nochmal.StatusCode.ShouldBe(HttpStatusCode.OK);
        var n = await JsonAsync(nochmal);
        n.GetProperty("wiederholt").GetBoolean().ShouldBeTrue();
        n.GetProperty("buchungId").GetGuid().ShouldBe(e.GetProperty("buchungId").GetGuid());

        // Gleicher Schlüssel, andere Anfrage: abgelehnt.
        var anders = await h.SendenAsync(HttpMethod.Post, $"warenkorb/{korb}/buchen", Anfrage("rechnung"), idempotenz: schluessel);
        anders.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(anders)).GetProperty("code").GetString().ShouldBe("idempotenz-konflikt");

        // Neuer Schlüssel für denselben Warenkorb: bereits gebucht.
        (await h.SendenAsync(HttpMethod.Post, $"warenkorb/{korb}/buchen", Anfrage("lastschrift"), nachweis: true, idempotenz: Guid.NewGuid().ToString()))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var bereich = h.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        (await db.Belegungen.CountAsync(Ct)).ShouldBe(1);
        (await db.Set<VibeVHS.Domain.Finanzen.SepaMandat>().CountAsync(Ct)).ShouldBe(1);
        var buchung = await db.Set<Shopbuchung>().SingleAsync(Ct);
        buchung.Einwilligungen.Select(x => $"{x.Art}:{x.Version}:{x.Erteilt}").ShouldBe(["agb:2026-08:True", "datenschutz:2026-08:True", "sepa:2026-08:True", "werbung:2026-08:True"]);
        var belegung = await db.Belegungen.SingleAsync(Ct);
        belegung.Weg.ShouldBe(Buchungsweg.Online);
        // Bestätigungsmail mit Übersetzungslink im Postausgang
        (await db.Postausgang.Where(m => m.PersonId == belegung.PersonId).Select(m => m.Inhalt).ToListAsync(Ct)).ShouldContain(i => i.Contains("uebersetzen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Parallele_Warenkoerbe_ueberbuchen_nicht()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-903", max: 3, Buchungsrichtlinie.Oeffentlich with { Warteliste = false });
        var nachweise = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            nachweise.Add(await h.NachweisAsync());
        }

        var antworten = await Task.WhenAll(nachweise.Select(async pow =>
        {
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, "warenkorb") { Content = System.Net.Http.Json.JsonContent.Create(new { kursnummern = new[] { "26H-903" } }) };
            anfrage.Headers.Add("X-VHS-PoW", pow);
            return (await h.Http.SendAsync(anfrage, Ct)).StatusCode;
        }));
        antworten.Count(s => s == HttpStatusCode.Created).ShouldBe(3);
        antworten.Count(s => s == HttpStatusCode.Conflict).ShouldBe(17);

        // Alle drei buchen parallel: genau drei Anmeldungen, keine Überbuchung.
        await using var bereich = h.App.Services.CreateAsyncScope();
        var koerbe = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Set<Warenkorb>().Select(w => w.Id).ToListAsync(Ct);
        var pows = new List<string>();
        foreach (var _ in koerbe)
        {
            pows.Add(await h.NachweisAsync());
        }

        var buchungen = await Task.WhenAll(koerbe.Zip(pows).Select(async x =>
        {
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, $"warenkorb/{x.First}/buchen") { Content = System.Net.Http.Json.JsonContent.Create(Anfrage(), options: Json) };
            anfrage.Headers.Add("X-VHS-PoW", x.Second);
            anfrage.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return (await h.Http.SendAsync(anfrage, Ct)).StatusCode;
        }));
        buchungen.ShouldAllBe(s => s == HttpStatusCode.Created);
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        (await db.Belegungen.CountAsync(b => b.Status == Belegungsstatus.Angemeldet, Ct)).ShouldBe(3);
        var v = await JsonAsync(await h.Http.GetAsync("kurse/26H-903/verfuegbarkeit", Ct));
        v.GetProperty("code").GetString().ShouldBe("ausgebucht");
        v.GetProperty("buchbar").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Nur_oeffentliche_Kurse_und_nur_nach_Buchungsrichtlinie()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-910", max: 5);
        await h.KursAsync("26H-911", max: 5, sichtbarkeit: Sichtbarkeit.Intern);
        await h.KursAsync("26H-912", max: 5, status: Kursstatus.Entwurf);
        await h.KursAsync("26H-913", max: 5, new Buchungsrichtlinie { Rechte = [new(Buchungsweg.Tresen, true)] });

        var liste = await JsonAsync(await h.Http.GetAsync("kurse", Ct));
        liste.GetProperty("kurse").EnumerateArray().Select(k => k.GetProperty("nummer").GetString()).Order().ShouldBe(["26H-910", "26H-913"]);
        (await h.Http.GetAsync("kurse/26H-911", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await KorbAsync(h, "26H-912")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nur am Tresen buchbar: veröffentlicht, aber online nicht buchbar – mit Code und Begründung aus der Buchungsprüfung.
        var v = await JsonAsync(await h.Http.GetAsync("kurse/26H-913/verfuegbarkeit", Ct));
        v.GetProperty("code").GetString().ShouldBe("weg-nicht-erlaubt");
        v.GetProperty("text").GetString()!.ShouldContain("Tresen");
        var korb = await KorbAsync(h, "26H-913");
        korb.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(korb)).GetProperty("buchungscode").GetString().ShouldBe("weg-nicht-erlaubt");

        // Pflicht-Einwilligungen in der aktuellen Fassung
        var k = (await JsonAsync(await KorbAsync(h, "26H-910"))).GetProperty("id").GetGuid();
        var veraltet = await h.SendenAsync(HttpMethod.Post, $"warenkorb/{k}/buchen", Anfrage(agb: "2020-01"), nachweis: true, idempotenz: Guid.NewGuid().ToString());
        veraltet.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(veraltet)).GetProperty("code").GetString().ShouldBe("einwilligung-veraltet");
    }

    [Fact]
    public async Task Kursliste_liefert_Delta_mit_entfernten_Kursen_und_ETag()
    {
        await using var h = await StartenAsync();
        var a = await h.KursAsync("26H-920", max: 5);
        await h.KursAsync("26H-921", max: 5);

        var erste = await h.Http.GetAsync("kurse", Ct);
        var etag = erste.Headers.ETag!.Tag;
        var stand = (await JsonAsync(erste)).GetProperty("stand").GetString();
        using (var bedingt = new HttpRequestMessage(HttpMethod.Get, "kurse"))
        {
            bedingt.Headers.TryAddWithoutValidation("If-None-Match", etag);
            (await h.Http.SendAsync(bedingt, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotModified);
        }

        h.Uhr.Advance(TimeSpan.FromMinutes(5));
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            (await db.Kurse.SingleAsync(k => k.Id == a.Id, Ct)).MitBeschreibung("Neu: mit Matten.");
            (await db.Kurse.SingleAsync(k => k.Kursnummer == "26H-921", Ct)).StatusSetzen(Kursstatus.Abgesagt);
            await db.SaveChangesAsync(Ct);
        }

        var delta = await JsonAsync(await h.Http.GetAsync($"kurse?geaendertSeit={Uri.EscapeDataString(stand!)}", Ct));
        delta.GetProperty("kurse").EnumerateArray().Select(k => k.GetProperty("nummer").GetString()).ShouldBe(["26H-920"]);
        delta.GetProperty("entfernt").EnumerateArray().Select(x => x.GetString()).ShouldBe(["26H-921"]);
        var detail = await JsonAsync(await h.Http.GetAsync("kurse/26H-920", Ct));
        detail.GetProperty("beschreibung").GetString().ShouldBe("Neu: mit Matten.");
        detail.GetProperty("orte")[0].GetProperty("wegbeschreibung").GetString()!.ShouldContain("Bildungsweg 1");
        detail.GetProperty("termine").GetArrayLength().ShouldBe(6);
        detail.GetProperty("kurs").GetProperty("slug").GetString().ShouldBe("26h-920-yoga-26h-920");
    }

    [Fact]
    public async Task Registrierung_mit_Double_Opt_in_Rate_Limit_Konto_und_Storno()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-930", max: 5);
        var daten = new { eMail = "Konto@Example.org", vorname = "Kai", nachname = "Konto", sprache = "tr" };
        (await h.SendenAsync(HttpMethod.Post, "konto/registrierung", daten)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await h.SendenAsync(HttpMethod.Post, "konto/registrierung", daten, nachweis: true)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        // Vor der Bestätigung keine Anmeldung
        (await h.SendenAsync(HttpMethod.Post, "konto/anmelden", new { eMail = "konto@example.org" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        string link;
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var mail = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.SingleAsync(m => m.Empfaenger == "konto@example.org", Ct);
            mail.Inhalt.ShouldContain("/uebersetzen/");
            link = Regex.Match(mail.Inhalt, @"token=([A-Za-z0-9_\-%]+)").Groups[1].Value;
        }

        // Weitere Registrierungen derselben Adresse antworten gleich; der fünfte Versuch trifft das Rate-Limit (4 je Stunde und IP).
        (await h.SendenAsync(HttpMethod.Post, "konto/registrierung", daten, nachweis: true)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await h.SendenAsync(HttpMethod.Post, "konto/registrierung", daten, nachweis: true)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var zuViel = await h.SendenAsync(HttpMethod.Post, "konto/registrierung", daten, nachweis: true);
        zuViel.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        zuViel.Headers.RetryAfter.ShouldNotBeNull();

        // Der zuletzt verschickte Link gilt (neuer Token je Registrierung).
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var letzte = (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.Where(m => m.Empfaenger == "konto@example.org").ToListAsync(Ct))
                .OrderBy(m => m.ErstelltAm).Last();
            link = Regex.Match(letzte.Inhalt, @"token=([A-Za-z0-9_\-%]+)").Groups[1].Value;
            bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = ShopApi.Webshop;
            await bereich.ServiceProvider.GetRequiredService<ShopDienst>().BestaetigenAsync(Uri.UnescapeDataString(link), Ct);
        }

        var anmeldung = await JsonAsync(await h.SendenAsync(HttpMethod.Post, "konto/anmelden", new { eMail = "konto@example.org" }));
        var token = anmeldung.GetProperty("accessToken").GetString()!;
        (await h.SendenAsync(HttpMethod.Get, "konto/buchungen")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Mit Konto buchen: ohne Teilnehmerangaben, die Person kommt aus dem Konto.
        var korb = (await JsonAsync(await h.SendenAsync(HttpMethod.Post, "warenkorb", new { kursnummern = new[] { "26H-930" } }, nachweis: true, token: token)))
            .GetProperty("id").GetGuid();
        var anfrage = new
        {
            zahlart = "rechnung",
            einwilligungen = new[] { new { art = "agb", version = "2026-08", erteilt = true }, new { art = "datenschutz", version = "2026-08", erteilt = true } },
        };
        (await h.SendenAsync(HttpMethod.Post, $"warenkorb/{korb}/buchen", anfrage, nachweis: true, idempotenz: Guid.NewGuid().ToString()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await h.SendenAsync(HttpMethod.Post, $"warenkorb/{korb}/buchen", anfrage, nachweis: true, idempotenz: Guid.NewGuid().ToString(), token: token))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var buchungen = await JsonAsync(await h.SendenAsync(HttpMethod.Get, "konto/buchungen", token: token));
        var b = buchungen[0];
        b.GetProperty("kursnummer").GetString().ShouldBe("26H-930");
        b.GetProperty("stornierbar").GetBoolean().ShouldBeTrue();
        b.GetProperty("stornoText").GetString()!.ShouldContain("Rechtzeitig");
        var storno = await h.SendenAsync(HttpMethod.Post, $"konto/buchungen/{b.GetProperty("belegungId").GetGuid()}/storno", new { grund = "verhindert" }, token: token);
        storno.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(await h.SendenAsync(HttpMethod.Get, "konto/buchungen", token: token)))[0].GetProperty("status").GetString().ShouldBe("storniert");
    }

    [Fact]
    public async Task Unbestaetigte_Registrierungen_werden_nach_Ablauf_geloescht()
    {
        await using var h = await StartenAsync();
        (await h.SendenAsync(HttpMethod.Post, "konto/registrierung", new { eMail = "weg@example.org", vorname = "Wilma", nachname = "Weg" }, nachweis: true))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        h.Uhr.Advance(TimeSpan.FromHours(49));
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = ShopApi.Webshop;
        (await bereich.ServiceProvider.GetRequiredService<ShopDienst>().UnbestaetigteLoeschenAsync(Ct)).ShouldBe(1);
        (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Set<Shopkonto>().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Cors_nur_fuer_konfigurierte_Origins_und_Arbeitsnachweis_einmalig()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-940", max: 5);
        using (var erlaubt = new HttpRequestMessage(HttpMethod.Options, "warenkorb"))
        {
            erlaubt.Headers.Add("Origin", "https://www.vhs-test.example");
            erlaubt.Headers.Add("Access-Control-Request-Method", "POST");
            erlaubt.Headers.Add("Access-Control-Request-Headers", "content-type,x-vhs-pow,idempotency-key");
            var r = await h.Http.SendAsync(erlaubt, Ct);
            r.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["https://www.vhs-test.example"]);
        }

        var fremd = await h.SendenAsync(HttpMethod.Get, "kurse", origin: "https://boese.example");
        fremd.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();

        var pow = await h.NachweisAsync();
        foreach (var erwartet in new[] { HttpStatusCode.Created, HttpStatusCode.PreconditionRequired })
        {
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, "warenkorb") { Content = System.Net.Http.Json.JsonContent.Create(new { kursnummern = new[] { "26H-940" } }) };
            anfrage.Headers.Add("X-VHS-PoW", pow);
            (await h.Http.SendAsync(anfrage, Ct)).StatusCode.ShouldBe(erwartet);
        }

        using var falsch = new HttpRequestMessage(HttpMethod.Post, "warenkorb") { Content = System.Net.Http.Json.JsonContent.Create(new { kursnummern = new[] { "26H-940" } }) };
        falsch.Headers.Add("X-VHS-PoW", "erfunden:1");
        var f = await h.Http.SendAsync(falsch, Ct);
        f.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await JsonAsync(f)).GetProperty("code").GetString().ShouldBe("pow-ungueltig");
    }

    [Fact]
    public async Task OpenApi_beschreibt_alle_Routen_der_Buchungs_API()
    {
        await using var h = await StartenAsync();
        var dokument = await JsonAsync(await h.Http.GetAsync("openapi.json", Ct));
        dokument.GetProperty("openapi").GetString().ShouldBe("3.1.0");
        var pfade = dokument.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToHashSet();
        var routen = h.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText!).Where(r => r.StartsWith(ShopApi.Basis, StringComparison.Ordinal))
            .Select(r => Regex.Replace(r[ShopApi.Basis.Length..], @"\{(\w+)(:\w+)?\}", m => m.Groups[1].Value is "belegungId" ? "{id}" : $"{{{m.Groups[1].Value}}}"))
            .Distinct().ToList();
        routen.Count.ShouldBeGreaterThan(10);
        routen.Where(r => !pfade.Contains(r)).ShouldBeEmpty();
    }
}
