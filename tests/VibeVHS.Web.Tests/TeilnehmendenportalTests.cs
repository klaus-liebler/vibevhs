using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Portal;
using VibeVHS.Application.Shop;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Dokumente;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Portal;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Sicherheit;
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>Teilnehmendenportal: nur eigene Daten bzw. per Vollmacht, Storno-Fristen, Nachrückangebot, Umbuchung, Datenschutz-Selbstbedienung.</summary>
public sealed class TeilnehmendenportalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Benutzer Olga = new("tn.olga", "Olga Ost", Rolle.Teilnehmende, [Rolle.Teilnehmende]);

    private static readonly Benutzer Verwaltung = new("vl", "Vera Leitung", Rolle.Verwaltungsleitung, [Rolle.Verwaltungsleitung]);

    private sealed record Welt(ShopTestHost H, Guid Olga, Guid Kind, Guid Fremd) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => H.DisposeAsync();
    }

    private static Buchungsrichtlinie MitGebuehr => Buchungsrichtlinie.Oeffentlich with { BearbeitungsgebuehrCent = 500, StornofristTage = 7 };

    private static async Task<Welt> AufbauenAsync()
    {
        var h = await StartenAsync();
        await using var bereich = h.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var jetzt = h.Uhr.GetLocalNow();
        Person P(string v, string n, string? mail)
        {
            var p = Person.Anlegen(v, n, new DateOnly(2024, 1, 1), jetzt.AddYears(-2), null, mail);
            p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-2)));
            db.Personen.Add(p);
            return p;
        }

        var olga = P("Olga", "Ost", "olga@example.org");
        var kind = P("Kim", "Ost", null);
        kind.VollmachtErteilen(Bevollmaechtigtenart.Person, olga.Id, "Olga Ost", "Geburtsurkunde", new DateOnly(2024, 1, 1), null, Erfassung.Erst(jetzt.AddYears(-1)));
        var fremd = P("Sven", "Fremd", "sven@example.org");
        db.Benutzerkonten.Add(new Benutzerkonto("tn.olga", "Olga Ost", null, [Rolle.Teilnehmende], [], olga.Id));
        db.Benutzerkonten.Add(new Benutzerkonto("vl", "Vera Leitung", null, [Rolle.Verwaltungsleitung], [], null));
        await db.SaveChangesAsync(Ct);
        return new Welt(h, olga.Id, kind.Id, fremd.Id);
    }

    private static async Task<T> MitAsync<T>(ShopTestHost h, Benutzer wer, Func<IServiceProvider, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider);
    }

    private static Task<T> PortalAsync<T>(Welt w, Func<TeilnehmendenportalDienst, Task<T>> arbeit)
        => MitAsync(w.H, Olga, d => arbeit(d.GetRequiredService<TeilnehmendenportalDienst>()));

    private static Task<Belegung> AnmeldenAsync(Welt w, Guid person, Guid kurs)
        => MitAsync(w.H, Benutzer.Automatik, async d => (await d.GetRequiredService<AnmeldungDienst>().AnmeldenAsync(new AnmeldeAuftrag(person, kurs, Zahlart.Ueberweisung), Ct)).Belegung);

    [Fact]
    public async Task Nur_eigene_Buchungen_und_die_per_Vollmacht_fremde_ergeben_403()
    {
        await using var w = await AufbauenAsync();
        var kurs = await w.H.KursAsync("26H-970", 10, MitGebuehr);
        var anderer = await w.H.KursAsync("26H-972", 10, MitGebuehr);
        await AnmeldenAsync(w, w.Olga, kurs.Id);
        await AnmeldenAsync(w, w.Kind, kurs.Id);
        var fremd = await AnmeldenAsync(w, w.Fremd, anderer.Id);

        var liste = await PortalAsync(w, p => p.BuchungenAsync(Ct));
        liste.Select(b => b.Fuer).Order().ShouldBe(["Kim Ost", "Sie"]);
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, p => p.StornierenAsync(fremd.Id, Ct)))).Status.ShouldBe(403);
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, async p => { await p.AngebotAsync(fremd.Id, true, Ct); return 0; }))).Status.ShouldBe(403);
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, p => p.TerminIcsAsync(anderer.Veranstaltungen.First().Id, Ct)))).Status.ShouldBe(403);
        (await PortalAsync(w, p => p.TerminIcsAsync(kurs.Veranstaltungen.First().Id, Ct))).Inhalt.ShouldContain("BEGIN:VEVENT");
        (await PortalAsync(w, p => p.TermineAsync(Ct))).ShouldAllBe(t => t.Kursnummer == "26H-970");

        // Dokumente anderer Personen: 403
        Guid fremdesDokument;
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var d = new Dokument(Ids.Neu(), Dokumentart.Teilnahmebescheinigung, w.Fremd, anderer.Id, fremd.Id, "Bescheinigung Sven", "%PDF-1.4 x"u8.ToArray(), "{}", "Bescheinigung",
                "tok-x", w.H.Uhr.GetUtcNow(), "Test", TimeSpan.FromDays(180));
            db.Add(d);
            await db.SaveChangesAsync(Ct);
            fremdesDokument = d.Id;
        }

        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, p => p.DokumentPdfAsync(fremdesDokument, Ct)))).Status.ShouldBe(403);
        (await Should.ThrowAsync<PortalFehler>(() => MitAsync(w.H, new Benutzer("katja", "Katja", Rolle.Teilnehmerservice),
            d => d.GetRequiredService<TeilnehmendenportalDienst>().BuchungenAsync(Ct)))).Status.ShouldBe(403);
    }

    [Fact]
    public async Task Storno_nach_AGB_rechtzeitig_Bearbeitungsgebuehr_spaeter_volles_Entgelt()
    {
        await using var w = await AufbauenAsync();
        var frueh = await w.H.KursAsync("26H-973", 10, MitGebuehr, startInTagen: 30);
        var spaet = await w.H.KursAsync("26H-974", 10, MitGebuehr, startInTagen: 3);
        await AnmeldenAsync(w, w.Olga, frueh.Id);
        await AnmeldenAsync(w, w.Olga, spaet.Id);
        var liste = await PortalAsync(w, p => p.BuchungenAsync(Ct));
        var a = liste.Single(b => b.Kursnummer == "26H-973");
        var b = liste.Single(b => b.Kursnummer == "26H-974");
        a.StornoGebuehrCent.ShouldBe(500);
        b.StornoGebuehrCent.ShouldBe(8400);
        b.StornoText!.ShouldContain("AGB");
        (await PortalAsync(w, p => p.StornierenAsync(a.BelegungId, Ct))).GebuehrCent.ShouldBe(500);
        (await PortalAsync(w, p => p.StornierenAsync(b.BelegungId, Ct))).GebuehrCent.ShouldBe(8400);
        (await PortalAsync(w, p => p.BuchungenAsync(Ct))).ShouldAllBe(x => x.Vergangen && x.Status == Belegungsstatus.Storniert);
    }

    [Fact]
    public async Task Fuer_Vollmachtgeber_buchen_aber_nicht_fuer_Fremde()
    {
        await using var w = await AufbauenAsync();
        await w.H.KursAsync("26H-975", 10);
        (await PortalAsync(w, p => p.BuchbarFuerAsync(Ct))).Select(p => p.Id).ShouldBe([w.Olga, w.Kind]);
        var (pid, bev) = await PortalAsync(w, p => p.BuchenFuerAsync(w.Kind, Ct));
        bev.ShouldBe(w.Olga);
        var e = await MitAsync(w.H, Olga, d => d.GetRequiredService<Schnellanmeldedienst>().AnmeldenAsync(pid, "26H-975", "2026-08", "2026-08", "Portal", t => new Uri("https://x/" + t), Ct, bev));
        e.Status.ShouldBe("angemeldet");
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var b = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Belegungen.SingleAsync(Ct);
            b.PersonId.ShouldBe(w.Kind);
            b.BevollmaechtigterId.ShouldBe(w.Olga);
        }

        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, p => p.BuchenFuerAsync(w.Fremd, Ct)))).Status.ShouldBe(403);
    }

    [Fact]
    public async Task Nachrueckangebot_annehmen_und_Umbuchen()
    {
        await using var w = await AufbauenAsync();
        var voll = await w.H.KursAsync("26H-976", 1, titel: "Yoga am Morgen");
        var ziel = await w.H.KursAsync("26H-977", 5, titel: "Yoga am Abend");
        var fremd = await AnmeldenAsync(w, w.Fremd, voll.Id);
        await AnmeldenAsync(w, w.Olga, voll.Id);
        var wartend = (await PortalAsync(w, p => p.BuchungenAsync(Ct))).Single();
        wartend.Status.ShouldBe(Belegungsstatus.Warteliste);
        wartend.Warteplatz.ShouldBe(1);
        wartend.Umbuchbar.ShouldBeFalse();

        // Sven meldet sich ab (am Tresen) → Nachrückangebot an Olga
        await MitAsync(w.H, Benutzer.Automatik, async d => await d.GetRequiredService<AnmeldungDienst>().StornierenAsync(fremd.Id, "verhindert", Ct));
        var angebot = (await PortalAsync(w, p => p.BuchungenAsync(Ct))).Single();
        angebot.Status.ShouldBe(Belegungsstatus.Nachrueckangebot);
        angebot.AngebotBis.ShouldNotBeNull();
        await PortalAsync(w, async p => { await p.AngebotAsync(angebot.BelegungId, true, Ct); return 0; });
        var fest = (await PortalAsync(w, p => p.BuchungenAsync(Ct))).Single();
        fest.Status.ShouldBe(Belegungsstatus.Angemeldet);
        fest.Umbuchbar.ShouldBeTrue();

        var optionen = await PortalAsync(w, p => p.UmbuchungsoptionenAsync(fest.BelegungId, Ct));
        optionen.Select(o => o.Kurs.Kursnummer).ShouldContain("26H-977");
        await PortalAsync(w, async p => { await p.UmbuchenAsync(fest.BelegungId, ziel.Id, Ct); return 0; });
        var nachher = await PortalAsync(w, p => p.BuchungenAsync(Ct));
        nachher.Single(b => !b.Vergangen).Kursnummer.ShouldBe("26H-977");
    }

    [Fact]
    public async Task Auskunft_und_Loeschung_laufen_als_Vorgang_ueber_die_Verwaltung()
    {
        await using var w = await AufbauenAsync();
        await PortalAsync(w, async p => { await p.AuskunftAnfordernAsync(Ct); return 0; });
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, async p => { await p.AuskunftAnfordernAsync(Ct); return 0; }))).Message.ShouldContain("schon");
        await PortalAsync(w, async p => { await p.LoeschungAnfordernAsync("Bitte alles löschen", Ct); return 0; });

        Guid auskunft;
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            (await db.Aufgaben.CountAsync(a => a.Typ == Aufgabentypen.PortalDatenschutz && a.Status == Domain.Aufgaben.Aufgabenstatus.Offen, Ct)).ShouldBe(2);
            auskunft = (await db.Set<Portalantrag>().SingleAsync(a => a.Art == Antragsart.Auskunft, Ct)).Id;
            // Kein Sofortlöschen: die Person ist noch aktiv.
            (await db.Personen.SingleAsync(p => p.Id == w.Olga, Ct)).Datenschutz.ShouldBe(Domain.Datenschutz.Datenschutzstatus.Aktiv);
        }

        await MitAsync(w.H, Verwaltung, async d => { await d.GetRequiredService<PortalVerwaltung>().EntscheidenAsync(auskunft, true, null, Ct); return 0; });
        var dokumente = await PortalAsync(w, p => p.DokumenteAsync(Ct));
        var dok = dokumente.Single(d => d.Titel.Contains("Auskunft", StringComparison.Ordinal));
        var (_, pdf) = await PortalAsync(w, p => p.DokumentPdfAsync(dok.Id, Ct));
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public async Task Eigene_Daten_EMail_mit_Bestaetigung_Einwilligungen_und_Mandat()
    {
        await using var w = await AufbauenAsync();
        await PortalAsync(w, async p => { await p.EMailAendernAsync("olga.neu@example.org", Ct); return 0; });
        (await PortalAsync(w, p => p.DatenAsync(Ct))).EMail.ShouldBe("olga@example.org"); // noch nicht bestätigt
        string token;
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var mail = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.SingleAsync(m => m.Empfaenger == "olga.neu@example.org", Ct);
            token = Regex.Match(mail.Inhalt, @"teilnehmende/email/([A-Za-z0-9_\-]+)").Groups[1].Value;
        }

        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<TeilnehmendenportalDienst>().EMailBestaetigenAsync("falsch", Ct))).ShouldBeFalse();
        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<TeilnehmendenportalDienst>().EMailBestaetigenAsync(token, Ct))).ShouldBeTrue();
        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<TeilnehmendenportalDienst>().EMailBestaetigenAsync(token, Ct))).ShouldBeFalse(); // nur einmal
        (await PortalAsync(w, p => p.DatenAsync(Ct))).EMail.ShouldBe("olga.neu@example.org");

        await PortalAsync(w, async p => { await p.EinwilligungAsync(Kontaktzweck.Werbung, true, Ct); await p.EinwilligungAsync(Kontaktzweck.Fotos, true, Ct); return 0; });
        var d1 = await PortalAsync(w, p => p.DatenAsync(Ct));
        d1.Newsletter.ShouldBeTrue();
        d1.Fotos.ShouldBeTrue();
        await PortalAsync(w, async p => { await p.EinwilligungAsync(Kontaktzweck.Fotos, false, Ct); return 0; });
        (await PortalAsync(w, p => p.DatenAsync(Ct))).Fotos.ShouldBeFalse();

        await PortalAsync(w, async p => { await p.MandatErteilenAsync("Olga Ost", "DE89 3704 0044 0532 0130 00", null, Ct); return 0; });
        var mandat = (await PortalAsync(w, p => p.DatenAsync(Ct))).Mandate.Single();
        mandat.Aktiv.ShouldBeTrue();
        await PortalAsync(w, async p => { await p.MandatWiderrufenAsync(mandat.Id, Ct); return 0; });
        (await PortalAsync(w, p => p.DatenAsync(Ct))).Mandate.Single().Aktiv.ShouldBeFalse();

        await PortalAsync(w, async p => { await p.AdresseAendernAsync("Neue Str. 5", "48268", "Greven", Ct); await p.TelefonSetzenAsync("0171 1234567", Ct); return 0; });
        var d2 = await PortalAsync(w, p => p.DatenAsync(Ct));
        d2.Ort.ShouldBe("Greven");
        d2.Telefon.ShouldBe("0171 1234567");
    }

    [Fact]
    public async Task Anmeldelink_legt_Teilnehmendenkonto_an_aber_nicht_fuer_Mitarbeitende()
    {
        await using var w = await AufbauenAsync();
        await MitAsync(w.H, Benutzer.Automatik, async d => { await d.GetRequiredService<TeilnehmendenportalDienst>().LinkAnfordernAsync("sven@example.org", Ct); return 0; });
        string token;
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var mail = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.SingleAsync(m => m.Empfaenger == "sven@example.org", Ct);
            token = Regex.Match(mail.Inhalt, @"teilnehmende/link/([A-Za-z0-9_\-]+)").Groups[1].Value;
        }

        var konto = await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<TeilnehmendenportalDienst>().LinkEinloesenAsync(token, Ct));
        konto!.Rollen.ShouldBe([Rolle.Teilnehmende]);
        konto.PersonId.ShouldBe(w.Fremd);
        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<TeilnehmendenportalDienst>().LinkEinloesenAsync(token, Ct))).ShouldBeNull();

        // Person mit Mitarbeitendenkonto: kein Teilnehmendenzugang über dieses Konto
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var p = Person.Anlegen("Mia", "Mitarbeit", new DateOnly(2024, 1, 1), w.H.Uhr.GetLocalNow(), null, "mia@example.org");
            db.Personen.Add(p);
            db.Benutzerkonten.Add(new Benutzerkonto("mia", "Mia Mitarbeit", null, [Rolle.Teilnehmerservice], [], p.Id));
            await db.SaveChangesAsync(Ct);
        }

        await MitAsync(w.H, Benutzer.Automatik, async d => { await d.GetRequiredService<TeilnehmendenportalDienst>().LinkAnfordernAsync("mia@example.org", Ct); return 0; });
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var mail = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.SingleAsync(m => m.Empfaenger == "mia@example.org", Ct);
            token = Regex.Match(mail.Inhalt, @"teilnehmende/link/([A-Za-z0-9_\-]+)").Groups[1].Value;
        }

        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<TeilnehmendenportalDienst>().LinkEinloesenAsync(token, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Anmeldelink_legt_fuer_jede_Person_ein_eigenes_Konto_an()
    {
        // Früher: Login aus dem Zeitstempel-Anfang der UUIDv7 – die zweite Person aus demselben Zeitfenster bekam keinen Zugang.
        await using var w = await AufbauenAsync();
        var neu = new List<Guid>();
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var jetzt = w.H.Uhr.GetLocalNow();
            foreach (var v in new[] { "Ute", "Uwe" })
            {
                var p = Person.Anlegen(v, "Import", new DateOnly(2024, 1, 1), jetzt.AddYears(-2), null, $"{v.ToLowerInvariant()}@example.org");
                p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-2)));
                db.Personen.Add(p);
                neu.Add(p.Id);
            }

            await db.SaveChangesAsync(Ct);
        }

        var logins = new List<string>();
        foreach (var (mail, personId) in new[] { ("ute@example.org", neu[0]), ("uwe@example.org", neu[1]) })
        {
            await MitAsync(w.H, Benutzer.Automatik, async d => { await d.GetRequiredService<TeilnehmendenportalDienst>().LinkAnfordernAsync(mail, Ct); return 0; });
            string token;
            await using (var bereich = w.H.App.Services.CreateAsyncScope())
            {
                var m = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.SingleAsync(x => x.Empfaenger == mail, Ct);
                token = Regex.Match(m.Inhalt, @"teilnehmende/link/([A-Za-z0-9_\-]+)").Groups[1].Value;
            }

            var konto = await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<TeilnehmendenportalDienst>().LinkEinloesenAsync(token, Ct));
            konto.ShouldNotBeNull();
            konto.PersonId.ShouldBe(personId);
            konto.Login.ShouldBe(Benutzerkonto.PortalLogin("tn", personId));
            logins.Add(konto.Login);
        }

        logins.Distinct().Count().ShouldBe(2);
    }
}
