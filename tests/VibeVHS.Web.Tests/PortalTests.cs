using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Portal;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Portal;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Sicherheit;
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>Dozierendenportal: nur eigene Kurse/Termine (fremd → 403), Unterlagen, Ausfall, Anliegen mit Vier-Augen, Kalender, Anmeldelink.</summary>
public sealed class PortalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Welt(ShopTestHost H, Guid DozentA, Guid DozentB, Kurs KursA, Kurs KursB, Guid Teilnehmerin, Guid Buchhalterin) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => H.DisposeAsync();
    }

    private static Benutzer Doz(string login) => new(login, login, Rolle.Dozierende, [Rolle.Dozierende]);

    private static readonly Benutzer Buchhaltung = new("lina.buch", "Lina Buch", Rolle.Buchhaltung, [Rolle.Buchhaltung, Rolle.Dozierende]);

    private static readonly Benutzer Fachbereich = new("fbl.acht", "Fach Bereich", Rolle.Fachbereichsleitung, [Rolle.Fachbereichsleitung], [8]);

    private static async Task<Welt> AufbauenAsync()
    {
        var h = await StartenAsync();
        var a = await h.KursAsync("26H-960", max: 10);
        var b = await h.KursAsync("26H-961", max: 10);
        await using var bereich = h.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var jetzt = h.Uhr.GetLocalNow();
        Person P(string v, string n, string mail, Personenrolle rolle)
        {
            var p = Person.Anlegen(v, n, new DateOnly(2024, 1, 1), jetzt.AddYears(-2), null, mail);
            p.RolleHinzufuegen(rolle, new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-2)));
            db.Personen.Add(p);
            return p;
        }

        var dozA = P("Dora", "Dozentin", "dora@example.org", Personenrolle.Dozierende);
        var dozB = P("Bernd", "Fremd", "bernd@example.org", Personenrolle.Dozierende);
        var tn = P("Tina", "Teil", "tina@example.org", Personenrolle.Teilnehmende);
        var buch = P("Lina", "Buch", "lina@example.org", Personenrolle.Dozierende);
        (await db.Kurse.SingleAsync(k => k.Id == a.Id, Ct)).MitTeam([new KursDozent(dozA.Id, "Dora Dozentin", true)]);
        (await db.Kurse.SingleAsync(k => k.Id == b.Id, Ct)).MitTeam([new KursDozent(dozB.Id, "Bernd Fremd", true)]);
        db.Benutzerkonten.Add(new Benutzerkonto("doz.a", "Dora Dozentin", null, [Rolle.Dozierende], [8], dozA.Id));
        db.Benutzerkonten.Add(new Benutzerkonto("doz.b", "Bernd Fremd", null, [Rolle.Dozierende], [8], dozB.Id));
        db.Benutzerkonten.Add(new Benutzerkonto("lina.buch", "Lina Buch", null, [Rolle.Buchhaltung, Rolle.Dozierende], [], buch.Id));
        db.Benutzerkonten.Add(new Benutzerkonto("fbl.acht", "Fach Bereich", null, [Rolle.Fachbereichsleitung], [8], null));
        await db.SaveChangesAsync(Ct);
        await MitAsync(h, Benutzer.Automatik, async d =>
            (await d.GetRequiredService<AnmeldungDienst>().AnmeldenAsync(new AnmeldeAuftrag(tn.Id, a.Id, Zahlart.Ueberweisung), Ct)).Belegung);
        return new Welt(h, dozA.Id, dozB.Id, a, b, tn.Id, buch.Id);
    }

    private static async Task<T> MitAsync<T>(ShopTestHost h, Benutzer wer, Func<IServiceProvider, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider);
    }

    private static Task<T> PortalAsync<T>(Welt w, Func<DozentenportalDienst, Task<T>> arbeit, string login = "doz.a")
        => MitAsync(w.H, Doz(login), d => arbeit(d.GetRequiredService<DozentenportalDienst>()));

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% Übungsblatt\n");

    [Fact]
    public async Task Nur_eigene_Kurse_und_Termine_fremde_ergeben_403()
    {
        await using var w = await AufbauenAsync();
        var termine = await PortalAsync(w, p => p.TermineAsync(null, DateOnly.FromDateTime(DateTime.Today.AddDays(60)), Ct));
        termine.ShouldNotBeEmpty();
        termine.ShouldAllBe(t => t.Kursnummer == "26H-960");
        termine[0].Teilnehmende.ShouldBe(1);
        termine[0].Wegbeschreibung!.ShouldContain("Bildungsweg 1");
        (await PortalAsync(w, p => p.KurseAsync(Ct))).Select(k => k.Kursnummer).ShouldBe(["26H-960"]);

        var fremd = w.KursB.Veranstaltungen.First().Id;
        foreach (var versuch in new Func<DozentenportalDienst, Task>[]
                 {
                     p => p.TerminAsync(fremd, Ct), p => p.TerminIcsAsync(fremd, Ct), p => p.KursAsync(w.KursB.Id, Ct), p => p.UnterlagenAsync(w.KursB.Id, Ct),
                     p => p.NachrichtAsync(fremd, "x", "y", Ct), p => p.AusfallMeldenAsync(fremd, "krank", null, Ct),
                     p => p.UnterlageHochladenAsync(w.KursB.Id, "x", "x.pdf", Pdf, false, Ct),
                 })
        {
            (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, async p => { await versuch(p); return 0; }))).Status.ShouldBe(403);
        }

        // Andere Rolle (z. B. Teilnehmerservice) kommt nicht ins Portal.
        (await Should.ThrowAsync<PortalFehler>(() => MitAsync(w.H, new Benutzer("katja", "Katja", Rolle.Teilnehmerservice),
            d => d.GetRequiredService<DozentenportalDienst>().TermineAsync(null, null, Ct)))).Status.ShouldBe(403);
    }

    [Fact]
    public async Task Unterlagen_mit_Pruefung_Verteilung_Abruf_und_Zurueckziehen()
    {
        await using var w = await AufbauenAsync();
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, p => p.UnterlageHochladenAsync(w.KursA.Id, "Skript", "skript.exe", Pdf, true, Ct))))
            .Message.ShouldContain("nicht erlaubt");
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, p => p.UnterlageHochladenAsync(w.KursA.Id, "Skript", "skript.pdf", Encoding.ASCII.GetBytes("MZ…"), true, Ct))))
            .Message.ShouldContain("passt nicht");
        var virus = Encoding.ASCII.GetBytes("%PDF-1.4 X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, p => p.UnterlageHochladenAsync(w.KursA.Id, "Skript", "skript.pdf", virus, true, Ct))))
            .Message.ShouldContain("EICAR");

        (await PortalAsync(w, p => p.UnterlageHochladenAsync(w.KursA.Id, "Übungsblatt 1", "blatt1.pdf", Pdf, true, Ct))).ShouldBe(1);
        string token;
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var mail = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.SingleAsync(m => m.Betreff.Contains("Unterlage"), Ct);
            mail.PersonId.ShouldBe(w.Teilnehmerin);
            mail.Inhalt.ShouldContain("/uebersetzen/");
            token = Regex.Match(mail.Inhalt, @"/u/([A-Za-z0-9_\-]+)").Groups[1].Value;
        }

        var d = await MitAsync(w.H, Benutzer.Automatik, s => s.GetRequiredService<DozentenportalDienst>().UnterlageAbrufenAsync(token, Ct));
        d!.Name.ShouldBe("blatt1.pdf");
        d.Inhalt.ShouldBe(Pdf);
        var liste = await PortalAsync(w, p => p.UnterlagenAsync(w.KursA.Id, Ct));
        liste.Single().Abrufe.ShouldBe(1);
        await PortalAsync(w, async p => { await p.UnterlageZurueckziehenAsync(liste.Single().Id, Ct); return 0; });
        (await MitAsync(w.H, Benutzer.Automatik, s => s.GetRequiredService<DozentenportalDienst>().UnterlageAbrufenAsync(token, Ct))).ShouldBeNull();
        (await MitAsync(w.H, Benutzer.Automatik, s => s.GetRequiredService<DozentenportalDienst>().UnterlageAbrufenAsync("erfunden", Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Ausfall_wird_Aufgabe_der_Fachbereichsleitung_und_informiert_auf_Wunsch()
    {
        await using var w = await AufbauenAsync();
        var termin = w.KursA.Veranstaltungen.OrderBy(v => v.Beginn).First().Id;
        (await PortalAsync(w, p => p.AusfallMeldenAsync(termin, "Krankheit", "Der Termin fällt leider aus.", Ct))).ShouldBe(1);
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, p => p.AusfallMeldenAsync(termin, "Krankheit", null, Ct)))).Message.ShouldContain("bereits");

        await using var bereich = w.H.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var aufgabe = await db.Aufgaben.SingleAsync(a => a.Typ == Aufgabentypen.PortalAusfall, Ct);
        aufgabe.Fachbereich.ShouldBe(8);
        aufgabe.Dringlichkeit.ShouldBe(Dringlichkeit.Hoch);
        aufgabe.Ziel.ShouldStartWith("portal-anliegen/");
        (await db.Postausgang.CountAsync(m => m.PersonId == w.Teilnehmerin && m.Inhalt.Contains("fällt leider aus"), Ct)).ShouldBe(1);

        // Fachbereichsleitung nimmt zur Kenntnis → Aufgabe schließt sich.
        var antrag = await db.Set<Portalantrag>().SingleAsync(Ct);
        await MitAsync(w.H, Fachbereich, async d => { await d.GetRequiredService<PortalVerwaltung>().EntscheidenAsync(antrag.Id, true, "Vertretung: Frau X", Ct); return 0; });
        await using var neu = w.H.App.Services.CreateAsyncScope();
        (await neu.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Aufgaben.SingleAsync(a => a.Typ == Aufgabentypen.PortalAusfall, Ct)).Status.ShouldBe(Aufgabenstatus.Geschlossen);
    }

    [Fact]
    public async Task Bankverbindung_erst_nach_Freigabe_durch_die_Buchhaltung_und_nie_durch_sich_selbst()
    {
        await using var w = await AufbauenAsync();
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, async p => { await p.BankverbindungBeantragenAsync("Dora", "DE00 1234", null, Ct); return 0; })))
            .Status.ShouldBe(422);
        await PortalAsync(w, async p => { await p.BankverbindungBeantragenAsync("Dora Dozentin", "DE89 3704 0044 0532 0130 00", null, Ct); return 0; });
        // Die Buchhalterin beantragt für sich selbst und darf das nicht selbst freigeben.
        await PortalAsync(w, async p => { await p.BankverbindungBeantragenAsync("Lina Buch", "DE02 1203 0000 0000 2020 51", null, Ct); return 0; }, "lina.buch");

        Guid eigener, fremder;
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            (await db.Set<VibeVHS.Domain.Finanzen.Bankverbindung>().CountAsync(Ct)).ShouldBe(0); // noch nicht wirksam
            fremder = (await db.Set<Portalantrag>().SingleAsync(a => a.PersonId == w.DozentA, Ct)).Id;
            eigener = (await db.Set<Portalantrag>().SingleAsync(a => a.PersonId == w.Buchhalterin, Ct)).Id;
            (await db.Aufgaben.CountAsync(a => a.Typ == Aufgabentypen.PortalBuchhaltung && a.Status == Aufgabenstatus.Offen, Ct)).ShouldBe(2);
        }

        (await Should.ThrowAsync<PortalFehler>(() => MitAsync(w.H, Buchhaltung, async d => { await d.GetRequiredService<PortalVerwaltung>().EntscheidenAsync(eigener, true, null, Ct); return 0; })))
            .Message.ShouldContain("Vier-Augen");
        (await Should.ThrowAsync<PortalFehler>(() => MitAsync(w.H, Fachbereich, async d => { await d.GetRequiredService<PortalVerwaltung>().EntscheidenAsync(fremder, true, null, Ct); return 0; })))
            .Status.ShouldBe(403);
        await MitAsync(w.H, Buchhaltung, async d => { await d.GetRequiredService<PortalVerwaltung>().EntscheidenAsync(fremder, true, null, Ct); return 0; });

        await using var nach = w.H.App.Services.CreateAsyncScope();
        var bank = await nach.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Set<VibeVHS.Domain.Finanzen.Bankverbindung>().SingleAsync(Ct);
        bank.PersonId.ShouldBe(w.DozentA);
        (await PortalAsync(w, p => p.DatenAsync(Ct))).Bankverbindung!.ShouldContain("Dora Dozentin");
    }

    [Fact]
    public async Task Nachweis_wird_nach_Pruefung_als_Qualifikation_eingetragen()
    {
        await using var w = await AufbauenAsync();
        await MitAsync(w.H, Benutzer.Automatik, async d => { await d.GetRequiredService<VibeVHS.Application.Programm.QualifikationsDienst>().GrundeinstellungenAsync(Ct); return 0; });
        await PortalAsync(w, async p => { await p.NachweisHochladenAsync("erste-hilfe", null, new DateOnly(2028, 1, 31), "erste-hilfe.pdf", Pdf, Ct); return 0; });
        Guid id;
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            id = (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Set<Portalantrag>().SingleAsync(Ct)).Id;
        }

        await MitAsync(w.H, Fachbereich, async d => { await d.GetRequiredService<PortalVerwaltung>().EntscheidenAsync(id, true, null, Ct); return 0; });
        var q = (await PortalAsync(w, p => p.DatenAsync(Ct))).Qualifikationen.Single();
        q.Name.ShouldBe("Erste-Hilfe-Nachweis");
        q.Bis.ShouldBe(new DateOnly(2028, 1, 31));
    }

    [Fact]
    public async Task Kalender_Abo_mit_widerrufbarer_Adresse()
    {
        await using var w = await AufbauenAsync();
        var link = await PortalAsync(w, p => p.KalenderLinkErzeugenAsync(Ct));
        var token = Regex.Match(link.ToString(), @"kalender/([^/]+)\.ics$").Groups[1].Value;
        var ics = await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<DozentenportalDienst>().KalenderAsync(token, Ct));
        ics.ShouldNotBeNull();
        ics.ShouldContain("BEGIN:VCALENDAR");
        ics.ShouldContain("26H-960");
        ics.ShouldNotContain("26H-961");
        Regex.Matches(ics, "BEGIN:VEVENT").Count.ShouldBe(6);

        var neu = await PortalAsync(w, p => p.KalenderLinkErzeugenAsync(Ct));
        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<DozentenportalDienst>().KalenderAsync(token, Ct))).ShouldBeNull(); // alte Adresse gesperrt
        await PortalAsync(w, async p => { await p.KalenderSperrenAsync(Ct); return 0; });
        var neuToken = Regex.Match(neu.ToString(), @"kalender/([^/]+)\.ics$").Groups[1].Value;
        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<DozentenportalDienst>().KalenderAsync(neuToken, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Anmeldelink_einmalig_und_15_Minuten_gueltig()
    {
        await using var w = await AufbauenAsync();
        await MitAsync(w.H, Benutzer.Automatik, async d => { await d.GetRequiredService<DozentenportalDienst>().LinkAnfordernAsync("DORA@example.org", Ct); return 0; });
        await MitAsync(w.H, Benutzer.Automatik, async d => { await d.GetRequiredService<DozentenportalDienst>().LinkAnfordernAsync("tina@example.org", Ct); return 0; }); // keine Kursleitung
        string token;
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var mails = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.Where(m => m.Betreff.Contains("Anmeldelink")).ToListAsync(Ct);
            mails.Count.ShouldBe(1);
            token = Regex.Match(mails[0].Inhalt, @"dozierende/link/([A-Za-z0-9_\-]+)").Groups[1].Value;
        }

        var konto = await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<DozentenportalDienst>().LinkEinloesenAsync(token, Ct));
        konto!.Login.ShouldBe("doz.a");
        konto.PersonId.ShouldBe(w.DozentA);
        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<DozentenportalDienst>().LinkEinloesenAsync(token, Ct))).ShouldBeNull(); // nur einmal

        await MitAsync(w.H, Benutzer.Automatik, async d => { await d.GetRequiredService<DozentenportalDienst>().LinkAnfordernAsync("bernd@example.org", Ct); return 0; });
        await using (var bereich = w.H.App.Services.CreateAsyncScope())
        {
            var mail = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Postausgang.SingleAsync(m => m.Betreff.Contains("Anmeldelink") && m.PersonId == w.DozentB, Ct);
            token = Regex.Match(mail.Inhalt, @"dozierende/link/([A-Za-z0-9_\-]+)").Groups[1].Value;
        }

        w.H.Uhr.Advance(TimeSpan.FromMinutes(16));
        (await MitAsync(w.H, Benutzer.Automatik, d => d.GetRequiredService<DozentenportalDienst>().LinkEinloesenAsync(token, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Verfuegbarkeit_und_Kursvorschlag_landen_beim_Fachbereich()
    {
        await using var w = await AufbauenAsync();
        await PortalAsync(w, async p => { await p.VerfuegbarkeitAsync("Frühjahr 2027", ["Mo", "Mi"], "ab 17 Uhr", "nicht in den Osterferien", null, Ct); return 0; });
        await PortalAsync(w, async p => { await p.KursvorschlagAsync("Yoga für Einsteiger", "Sanfter Einstieg", 8, "8 × 90 Min.", "ab 50", Ct); return 0; });
        (await Should.ThrowAsync<PortalFehler>(() => PortalAsync(w, async p => { await p.KursvorschlagAsync("", "", 8, null, null, Ct); return 0; }))).Status.ShouldBe(422);
        var liste = await MitAsync(w.H, Fachbereich, d => d.GetRequiredService<PortalVerwaltung>().AntraegeAsync(true, Ct));
        liste.Select(a => a.Art).Order().ShouldBe([Antragsart.Verfuegbarkeit, Antragsart.Kursvorschlag]);
        await using var bereich = w.H.App.Services.CreateAsyncScope();
        (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Aufgaben.CountAsync(a => a.Typ == Aufgabentypen.PortalFachbereich && a.Fachbereich == 8, Ct)).ShouldBe(2);
    }
}
