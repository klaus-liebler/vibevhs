using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Datenschutz;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Datenschutz;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Tests;

public class DatenschutzTests
{
    private static readonly Benutzer Verwaltungsleitung = new("sonja.eickelkamp", "Sonja Eickelkamp", Rolle.Verwaltungsleitung);
    private static readonly Benutzer Leitung = new("kai.lueken", "Dr. Kai Lüken", Rolle.Leitung);
    private static readonly Benutzer Buchhaltung = new("linda.lueke", "Linda Lüke", Rolle.Buchhaltung);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public Benutzer Aktuell { get; set; } = b;

        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(Aktuell);
    }

    private static async Task<(DatenschutzDienst Dienst, VibeVhsDbContext Db, TestBenutzer Wer)> DienstAsync(TestDatenbank t, Benutzer wer)
    {
        var db = t.NeuerKontext();
        var benutzer = new TestBenutzer(wer);
        var protokoll = new AuditProtokoll(db, benutzer, t.Uhr);
        var post = new PostausgangDienst(new NachrichtenSpeicher(db), protokoll, benutzer, new Uebersetzungslinks(new Uri("https://vhs.example/")), t.Uhr);
        var dienst = new DatenschutzDienst(new DatenschutzSpeicher(db), new PersonenSpeicher(db), post, protokoll, benutzer, new StammdatenSpeicher(db), t.Uhr);
        await dienst.GrundeinstellungenAsync(TestContext.Current.CancellationToken);
        return (dienst, db, benutzer);
    }

    /// <summary>Teilnehmer mit Kontaktdaten, Mail im Postausgang und als Bevollmächtigter einer anderen Person.</summary>
    private static async Task<(Person Mehmet, Person Ayse)> TeilnehmendeAsync(TestDatenbank t, DateTimeOffset erfasst)
    {
        var tag = DateOnly.FromDateTime(erfasst.Date);
        var e = Erfassung.Erst(erfasst);
        var mehmet = Person.Anlegen("Mehmet", "Yılmaz", tag, erfasst, new DateOnly(1988, 5, 3), "mehmet.yilmaz@example.org", "tr");
        mehmet.RolleHinzufuegen(Personenrolle.Teilnehmende, tag, e);
        var adr = mehmet.AdresseHinzufuegen(null, "Lindenstraße 14", "48282", "Emsdetten", tag, e);
        mehmet.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, adr, tag, e);
        mehmet.RufnummerHinzufuegen(RufnummerArt.Mobil, "+49 151 00000001", Messagingdienst.Sms, true, true, tag, e);
        var ayse = Person.Anlegen("Ayşe", "Yılmaz", tag, erfasst, new DateOnly(2012, 1, 1));
        ayse.RolleHinzufuegen(Personenrolle.Teilnehmende, tag, e);
        ayse.VollmachtErteilen(Bevollmaechtigtenart.Person, mehmet.Id, "Mehmet Yılmaz", "Sorgerecht", tag, null, e);

        await using var db = t.NeuerKontext();
        db.Personen.AddRange(mehmet, ayse);
        db.Postausgang.Add(new AusgehendeNachricht(Kanal.EMail, "mehmet.yilmaz@example.org", "Mehmet Yılmaz", "Erinnerung an Mehmet Yılmaz",
            "Guten Tag Herr Yılmaz", "tr", erfasst, "Test", mehmet.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (mehmet, ayse);
    }

    /// <summary>Dozentin mit einem Kurs; die Termine liegen im angegebenen Jahr.</summary>
    private static async Task<(Person Anna, Kurs Kurs)> DozentinAsync(TestDatenbank t, int kursjahr)
    {
        var erfasst = new DateTimeOffset(kursjahr, 1, 10, 9, 0, 0, TimeSpan.FromHours(1));
        var anna = Person.Anlegen("Anna", "Becker", new DateOnly(kursjahr, 1, 1), erfasst, new DateOnly(1986, 8, 12), "anna.becker@example.org");
        anna.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(kursjahr, 1, 1), Erfassung.Erst(erfasst));
        var semester = new Semester($"Frühjahr {kursjahr}", $"{kursjahr % 100}F", new DateOnly(kursjahr, 2, 1), new DateOnly(kursjahr, 7, 31));
        var art = new Kursart("Sprachkurs", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var kurs = new Kurs($"{kursjahr % 100}F-501", "Englisch A2", semester, 5, art, 6, 14).MitTeam([new KursDozent(anna.Id, "Anna Becker", true)]);
        kurs.SerieFestlegen(new Serienregel
        {
            Start = new DateOnly(kursjahr, 3, 2), Anzahl = 4, Wochentage = [DayOfWeek.Tuesday], Beginn = new TimeOnly(18, 0), DauerMinuten = 90,
            OrtIds = [Guid.NewGuid()],
        }, Kalender.OhneFerien, _ => "R 1", new DateOnly(kursjahr, 12, 31));

        await using var db = t.NeuerKontext();
        db.Personen.Add(anna);
        db.Semester.Add(semester);
        db.Kursarten.Add(art);
        db.Kurse.Add(kurs);
        db.Postausgang.Add(new AusgehendeNachricht(Kanal.EMail, "anna.becker@example.org", "Anna Becker", "Honorarvertrag", "Liebe Frau Becker",
            "de", erfasst, "Test", anna.Id));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (anna, kurs);
    }

    [Fact]
    public async Task Anonymisierung_haelt_referentielle_Integritaet_und_ersetzt_Namen_ueberall()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (anna, kurs) = await DozentinAsync(t, 2010); // Buchhaltungsfrist bis 31.12.2020 abgelaufen
        var (dienst, db, _) = await DienstAsync(t, Verwaltungsleitung);
        var versionen = await db.Set<PersonName>().CountAsync(ct);

        var entscheidung = await dienst.LoeschenAsync(anna.Id, "Test", ct);

        entscheidung.Aktion.ShouldBe(Loeschaktion.Anonymisieren);
        await using var neu = t.NeuerKontext();
        var person = await neu.Personen.SingleAsync(p => p.Id == anna.Id, ct);
        person.IstAnonymisiert.ShouldBeTrue();
        person.Anzeigename.ShouldStartWith($"Gelöschte Person {Pseudonym.Kurz(anna.Id)}");
        person.Namen.ShouldAllBe(n => n.Vorname == "Gelöschte" && n.Grund == "anonymisiert");
        person.EMailAdressen.ShouldAllBe(m => m.Adresse == "–");
        person.Merkmale.Where(m => m.Art == MerkmalArt.Geburtsdatum).ShouldAllBe(m => m.Inhalt == string.Empty);
        (await neu.Set<PersonName>().CountAsync(ct)).ShouldBe(versionen); // keine Zeile gelöscht

        var k = await neu.Kurse.Include(x => x.Veranstaltungen).SingleAsync(x => x.Id == kurs.Id, ct);
        k.Team.Single().PersonId.ShouldBe(anna.Id);
        k.Team.Single().Name.ShouldStartWith("Gelöschte Person");
        k.Veranstaltungen.ShouldAllBe(v => v.DozentId == anna.Id && v.DozentName.StartsWith("Gelöschte Person"));

        var mail = await neu.Postausgang.SingleAsync(n => n.PersonId == anna.Id, ct);
        mail.Inhalt.ShouldBe("[Inhalt anonymisiert]");
        mail.Empfaenger.ShouldBe("anonymisiert");
    }

    [Fact]
    public async Task Anonymisierte_Teilnehmende_zaehlen_in_Statistik_weiter_auch_fremde_Vollmacht_zeigt_Platzhalter()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (mehmet, ayse) = await TeilnehmendeAsync(t, new DateTimeOffset(2020, 3, 1, 9, 0, 0, TimeSpan.FromHours(1)));
        var (dienst, db, _) = await DienstAsync(t, Verwaltungsleitung);
        var teilnehmendeVorher = await db.Personen.CountAsync(p => (p.Rollen & Personenrolle.Teilnehmende) == Personenrolle.Teilnehmende, ct);

        await dienst.LoeschenAsync(mehmet.Id, "Test", ct);

        await using var neu = t.NeuerKontext();
        (await neu.Personen.CountAsync(p => (p.Rollen & Personenrolle.Teilnehmende) == Personenrolle.Teilnehmende, ct)).ShouldBe(teilnehmendeVorher);
        var m = await neu.Personen.SingleAsync(p => p.Id == mehmet.Id, ct);
        m.StatistikAltersgruppe.ShouldBe("35–49");
        m.StatistikGebiet.ShouldBe("Kreis Steinfurt");
        m.Adressen.ShouldAllBe(a => a.Strasse == "–" && a.Postleitzahl == string.Empty);
        m.Rufnummern.ShouldAllBe(r => r.Nummer == "–");

        var a = await neu.Personen.SingleAsync(p => p.Id == ayse.Id, ct);
        a.Vollmachten.Single().BevollmaechtigterId.ShouldBe(mehmet.Id);
        a.Vollmachten.Single().BevollmaechtigterName.ShouldStartWith("Gelöschte Person");
    }

    [Fact]
    public async Task Bei_laufender_Frist_wird_gesperrt_statt_anonymisiert_Kontaktdaten_werden_trotzdem_geloescht()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (anna, _) = await DozentinAsync(t, 2025);
        var (dienst, _, _) = await DienstAsync(t, Verwaltungsleitung);

        var pruefung = await dienst.PruefenAsync(anna.Id, ct);
        pruefung.Entscheidung.Aktion.ShouldBe(Loeschaktion.Sperren);
        pruefung.Entscheidung.GesperrtBis.ShouldBe(new DateOnly(2035, 12, 31));
        pruefung.Entscheidung.Gruende.Single().ShouldContain("§ 147");

        var ergebnis = await dienst.LoeschverlangenAsync(anna.Id, ct);

        ergebnis.AntwortEingereiht.ShouldBeTrue();
        await using var neu = t.NeuerKontext();
        var person = await neu.Personen.SingleAsync(p => p.Id == anna.Id, ct);
        person.Datenschutz.ShouldBe(Datenschutzstatus.Gesperrt);
        person.GesperrtBis.ShouldBe(new DateOnly(2035, 12, 31));
        person.Anzeigename.ShouldBe("Anna Becker");                 // für die Frist nötig
        person.EMail.ShouldBeNull();                                 // nicht nötig: gelöscht
        person.EMailAdressen.ShouldAllBe(m => m.Adresse == "–");
        (await neu.Postausgang.CountAsync(n => n.Betreff.StartsWith("Ihr Antrag auf Löschung") && n.PersonId == null, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Loeschlauf_braucht_zwei_Freigaben_von_verschiedenen_berechtigten_Personen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (mehmet, ayse) = await TeilnehmendeAsync(t, new DateTimeOffset(2020, 3, 1, 9, 0, 0, TimeSpan.FromHours(1)));
        var (dienst, db, wer) = await DienstAsync(t, Verwaltungsleitung);

        var lauf = await dienst.LoeschlaufVorschauAsync(36, ct);
        lauf.ZuAnonymisieren.ShouldBe(2);
        lauf.PersonIds.ShouldBe([mehmet.Id, ayse.Id], ignoreOrder: true);

        await dienst.FreigebenAsync(lauf.Id, ct);
        lauf.Status.ShouldBe(Loeschlaufstatus.TeilweiseFreigegeben);
        (await db.Personen.CountAsync(p => p.Datenschutz == Datenschutzstatus.Anonymisiert, ct)).ShouldBe(0);

        var gleich = await Should.ThrowAsync<InvalidOperationException>(() => dienst.FreigebenAsync(lauf.Id, ct));
        gleich.Message.ShouldContain("Vier-Augen");

        wer.Aktuell = Buchhaltung;
        (await Should.ThrowAsync<InvalidOperationException>(() => dienst.FreigebenAsync(lauf.Id, ct))).Message.ShouldContain("Verwaltungsleitung oder Leitung");

        wer.Aktuell = Leitung;
        await dienst.FreigebenAsync(lauf.Id, ct);

        lauf.Status.ShouldBe(Loeschlaufstatus.Ausgefuehrt);
        lauf.Anonymisiert.ShouldBe(2);
        lauf.PersonIds.ShouldBeEmpty(); // Löschprotokoll ohne Personendaten
        await using var neu = t.NeuerKontext();
        (await neu.Personen.CountAsync(p => p.Datenschutz == Datenschutzstatus.Anonymisiert, ct)).ShouldBe(2);
        var protokoll = await neu.Protokoll.Select(e => e.Beschreibung).ToListAsync(ct);
        protokoll.ShouldNotContain(b => b.Contains("Yılmaz") || b.Contains("Mehmet") || b.Contains("Ayşe"));
    }

    [Fact]
    public async Task Suchindex_enthaelt_nach_Anonymisierung_keine_Altdaten()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (mehmet, _) = await TeilnehmendeAsync(t, new DateTimeOffset(2020, 3, 1, 9, 0, 0, TimeSpan.FromHours(1)));
        var (dienst, db, _) = await DienstAsync(t, Verwaltungsleitung);
        var abfrage = new PersonenAbfrage(db, new DublettenSuche(db));
        (await abfrage.SeiteAsync(new PersonenFilter("mehmet"), null, 10, ct)).ShouldNotBeEmpty();

        await dienst.LoeschenAsync(mehmet.Id, "Test", ct);

        var text = (await db.Database.SqlQuery<string>($"SELECT Text AS Value FROM PersonSuche WHERE PersonId = {PersonenSuchindex.Schluessel(mehmet.Id)}")
            .ToListAsync(ct)).Single();
        text.ShouldNotContain("mehmet");
        text.ShouldNotContain("yilmaz");
        text.ShouldNotContain("1988");
        text.ShouldNotContain("example.org");
        (await abfrage.SeiteAsync(new PersonenFilter("mehmet", Datenschutz: Datenschutzstatus.Anonymisiert), null, 10, ct)).ShouldBeEmpty();
        (await abfrage.SeiteAsync(new PersonenFilter("geloschte", Datenschutz: Datenschutzstatus.Anonymisiert), null, 10, ct)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Auskunft_enthaelt_alle_Daten_Historie_Nachrichten_und_Fristen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (anna, kurs) = await DozentinAsync(t, 2025);
        var (dienst, db, _) = await DienstAsync(t, Verwaltungsleitung);
        await using (var vorher = t.NeuerKontext())
        {
            var p = await vorher.Personen.SingleAsync(x => x.Id == anna.Id, ct);
            p.NameAendernAb("Anna", "Becker-Lang", new DateOnly(2026, 6, 1), "Heirat", t.Uhr.GetLocalNow().AddDays(-10));
            await vorher.SaveChangesAsync(ct);
        }

        var auskunft = await dienst.AuskunftAsync(anna.Id, ct);

        auskunft.Name.ShouldBe("Anna Becker-Lang");
        var person = await db.Personen.AsNoTracking().SingleAsync(x => x.Id == anna.Id, ct);
        auskunft.Historie.Count.ShouldBe(Datenhistorie.Erstellen(person).Count);
        auskunft.Historie.ShouldContain(h => h.AlterWert == "Anna Becker" && h.NeuerWert == "Anna Becker-Lang");
        auskunft.Stammdaten.EMails.ShouldHaveSingleItem().ShouldContain("anna.becker@example.org");
        auskunft.KurseAlsDozent.ShouldHaveSingleItem().Kursnummer.ShouldBe(kurs.Kursnummer);
        auskunft.Nachrichten.ShouldHaveSingleItem().Betreff.ShouldBe("Honorarvertrag");
        auskunft.Aufbewahrung.ShouldHaveSingleItem().Fristende.ShouldBe("31.12.2035");
        auskunft.Empfaenger.ShouldContain("E-Mail-Versand (Auftragsverarbeitung)");

        var json = JsonSerializer.Serialize(auskunft);
        json.ShouldContain("Becker-Lang");
        var pdf = new AuskunftPdf().Erzeugen(auskunft);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");
        (await db.Protokoll.CountAsync(e => e.Aktion == "Datenschutz.Auskunft" && e.ObjektId == anna.Id, ct)).ShouldBe(1);
    }

    [Fact]
    public void Fristende_beginnt_bei_Abgabenordnung_mit_dem_Jahresende()
    {
        var buchhaltung = Aufbewahrungsfrist.Vorschlaege().Single(f => f.Schluessel == "buchhaltung");
        buchhaltung.Fristende(new DateOnly(2025, 3, 15)).ShouldBe(new DateOnly(2035, 12, 31));
        var post = Aufbewahrungsfrist.Vorschlaege().Single(f => f.Schluessel == "postausgang");
        post.Fristende(new DateOnly(2025, 3, 15)).ShouldBe(new DateOnly(2027, 3, 15));
    }
}
