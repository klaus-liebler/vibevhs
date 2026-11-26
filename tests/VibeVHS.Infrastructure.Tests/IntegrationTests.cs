using System.Text;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Adapters.Mocks;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Finanzen.Honorare;
using VibeVHS.Application.Integration;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Integration;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Anmeldung;
using VibeVHS.Infrastructure.Aufgaben;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Durchfuehrung;
using VibeVHS.Infrastructure.Finanzen;
using VibeVHS.Infrastructure.Integration;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Integrationskurse (I7): Kursbeginn, Identitätsprüfung mit Sichtvergleich, UE je Abschnitt, BAMF-Abrechnung, Fahrtkosten, Prüfungen.</summary>
public class IntegrationTests
{
    private static readonly Benutzer Ik = new("ines.mittendorf", "Ines Mittendorf", Rolle.IntegrationskursTeam);
    private static readonly Benutzer Tresen = new("katja.friedsen", "Katja Friedsen", Rolle.Teilnehmerservice);
    private static readonly Benutzer Buchhaltung = new("linda.luetke", "Linda Lütke", Rolle.Buchhaltung);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private sealed class LeereKonten : IBenutzerkonten
    {
        public Task<IReadOnlyList<Benutzerkonto>> AlleAsync(CancellationToken abbruch = default) => Task.FromResult<IReadOnlyList<Benutzerkonto>>([]);

        public Task<Benutzerkonto?> FindenAsync(string login, CancellationToken abbruch = default) => Task.FromResult<Benutzerkonto?>(null);
    }

    private sealed record Welt(IntegrationDienst Integration, IntegrationRoutinen Routinen, AnwesenheitDienst Anwesenheit, AnmeldungDienst Anmeldung, CheckinZugang Zugang,
        FinanzDienst Finanzen, ZahlungsDienst Zahlungen, HonorarDienst Honorare);

    private static Welt Erzeugen(TestDatenbank t, VibeVhsDbContext db, Benutzer wer)
    {
        var benutzer = new TestBenutzer(wer);
        var protokoll = new AuditProtokoll(db, benutzer, t.Uhr);
        var links = new Uebersetzungslinks(new Uri("https://vhs.example/"));
        var post = new PostausgangDienst(new NachrichtenSpeicher(db), protokoll, benutzer, links, t.Uhr);
        var anm = new AnmeldungSpeicher(db);
        var zugang = new CheckinZugang(new ZugangstokenSpeicher(db), new OeffentlicheAdresse(new Uri("https://vhs.example/")), protokoll, benutzer, t.Uhr);
        var anmeldung = new AnmeldungDienst(anm, new ProgrammSpeicher(db), new PersonenSpeicher(db), anm, new StammdatenSpeicher(db), post, protokoll, benutzer, t.Uhr, zugang,
            new QrCodeErzeuger());
        var anwesenheit = new AnwesenheitDienst(new DurchfuehrungSpeicher(db), anm, new ProgrammSpeicher(db), new PersonenSpeicher(db), new LeereKonten(),
            new Termincodes(new ZugangstokenSpeicher(db), t.Uhr), zugang, protokoll, benutzer, t.Uhr);
        var dokumente = new DokumentDienst(new DokumentSpeicher(db), anm, new ProgrammSpeicher(db), new PersonenSpeicher(db), new DurchfuehrungSpeicher(db),
            new StammdatenSpeicher(db), new BriefPdf(new QrCodeErzeuger()), links, zugang, protokoll, benutzer, t.Uhr);
        var fs = new FinanzSpeicher(db);
        var fp = new Finanzprotokoll(db, benutzer, t.Uhr);
        var finanzen = new FinanzDienst(fs, fp, new PersonenSpeicher(db), new ProgrammSpeicher(db), new StammdatenSpeicher(db), dokumente, post, protokoll, benutzer, t.Uhr);
        var entfernungen = new Entfernungen(new ProgrammSpeicher(db), new MockGeokodierer(), t.Uhr);
        var bamf = new MockBamf(post, new NachrichtenSpeicher(db), t.Uhr);
        var integration = new IntegrationDienst(new IntegrationSpeicher(db), anm, new ProgrammSpeicher(db), new PersonenSpeicher(db), anwesenheit, zugang, finanzen, fs, entfernungen,
            bamf, new IntegrationOptionen(14), protokoll, benutzer, t.Uhr);
        var aufgaben = new AufgabenDienst(new AufgabenSpeicher(db), new StammdatenSpeicher(db), protokoll, benutzer, t.Uhr);
        var routinen = new IntegrationRoutinen(integration, new IntegrationSpeicher(db), new ProgrammSpeicher(db), new PersonenSpeicher(db), aufgaben, t.Uhr);
        var zahlungen = new ZahlungsDienst(fs, fp, finanzen, new PersonenSpeicher(db), new StammdatenSpeicher(db), post, protokoll, benutzer, t.Uhr);
        var honorare = new HonorarDienst(fs, fp, finanzen, new HonorarModelle([new HonorarProUe(), new HonorarBasisUeTeilnehmende()]), new PersonenSpeicher(db), entfernungen,
            protokoll, t.Uhr);
        return new(integration, routinen, anwesenheit, anmeldung, zugang, finanzen, zahlungen, honorare);
    }

    /// <summary>
    /// Integrationskurs montags 08:00–12:00 (5,33 UE) ab 02.11.2026 im Bildungshaus Emsdetten (mit Koordinaten).
    /// Abschnitte für den Test: Modul 1 = 16 UE (drei Termine), Modul 2 = 16 UE. Teilnehmende: Greven (befreit), Emsdetten (Kostenbeitrag), Rheine (befreit).
    /// </summary>
    private static async Task<(Kurs Kurs, List<Person> P, Person Dozentin)> KursAsync(TestDatenbank t)
    {
        var jetzt = t.Uhr.GetLocalNow();
        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 6, 30));
        var art = new Kursart("Integrationskurs", Nutzungsart.Integrationskurs, [], Buchungsrichtlinie.Integrationskurs, Sichtbarkeit.Intern);
        var haus = new Ort(Ortsart.Liegenschaft, "Bildungshaus Emsdetten").MitAdresse("Bildungsweg 1", "48282", "Emsdetten", 52.1730, 7.5268);
        var raum = new Ort(Ortsart.Raum, "Kursraum", haus.Id, "A 1.10").MitKapazitaet(Nutzungsart.Integrationskurs, 25);
        var dozentin = Person.Anlegen("Maria", "Rossi", new DateOnly(2020, 1, 1), jetzt.AddYears(-5), null, "maria@example.org");
        dozentin.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(2020, 1, 1), Erfassung.Erst(jetzt.AddYears(-5)));
        dozentin.BeschaeftigungAendernAb(Beschaeftigungsart.Freiberuflich, new DateOnly(2020, 1, 1), Erfassung.Erst(jetzt.AddYears(-5)));
        dozentin.AdresseHinzufuegen(null, "Marktstr. 1", "48268", "Greven", new DateOnly(2020, 1, 1), Erfassung.Erst(jetzt.AddYears(-5)));
        var kurs = new Kurs("26H-401", "Integrationskurs Modul 1–2", semester, 4, art, 1, 20).MitTeam([new KursDozent(dozentin.Id, true)]);
        kurs.SerieFestlegen(new Serienregel
        {
            Start = new DateOnly(2026, 11, 2), Anzahl = 6, Wochentage = [DayOfWeek.Monday], Beginn = new TimeOnly(8, 0), DauerMinuten = 240, OrtIds = [raum.Id],
        }, Kalender.OhneFerien, new DateOnly(2027, 6, 30));
        kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
        var orte = new[] { ("48268", "Greven"), ("48282", "Emsdetten"), ("48429", "Rheine") };
        var liste = orte.Select((o, i) =>
        {
            var p = Person.Anlegen($"Vorname{i + 1}", $"Nachname{i + 1}", new DateOnly(2025, 1, 1), jetzt.AddYears(-1), new DateOnly(1990, 1, i + 1), $"p{i + 1}@example.org");
            p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2025, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
            p.AdresseHinzufuegen(null, $"Weg {i + 1}", o.Item1, o.Item2, new DateOnly(2025, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
            return p;
        }).ToList();

        await using var db = t.NeuerKontext();
        db.Semester.Add(semester);
        db.Kursarten.Add(art);
        db.Orte.AddRange(haus, raum);
        db.Kurse.Add(kurs);
        db.Personen.Add(dozentin);
        db.Personen.AddRange(liste);
        db.Add(new Integrationskursdaten(kurs.Id, "IK-2026-0815", Integrationskursart.Allgemein, [new Kursabschnitt(1, "Modul 1", 16), new Kursabschnitt(2, "Modul 2", 16)], 471, 229));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        foreach (var p in liste)
        {
            await using var db2 = t.NeuerKontext();
            await Erzeugen(t, db2, Ik).Anmeldung.AnmeldenAsync(new AnmeldeAuftrag(p.Id, kurs.Id, Zahlart.Kostentraeger), TestContext.Current.CancellationToken);
        }

        return (kurs, liste, dozentin);
    }

    private static async Task BerechtigungenUndAusweiseAsync(TestDatenbank t, List<Person> p)
    {
        await using var db = t.NeuerKontext();
        var w = Erzeugen(t, db, Ik);
        var ct = TestContext.Current.CancellationToken;
        await w.Integration.BerechtigungErfassenAsync(p[0].Id, Berechtigungsart.Berechtigungsschein, "TN-1001", "BAMF", new DateOnly(2026, 9, 1), new DateOnly(2027, 9, 1), 700, true,
            "Bürgergeld", true, ct);
        await w.Integration.BerechtigungErfassenAsync(p[1].Id, Berechtigungsart.Verpflichtung, "TN-1002", "Ausländerbehörde Kreis Steinfurt", new DateOnly(2026, 9, 1), null, 700, false,
            null, false, ct);
        await w.Integration.BerechtigungErfassenAsync(p[2].Id, Berechtigungsart.Verpflichtung, "TN-1003", "Jobcenter Kreis Steinfurt", new DateOnly(2026, 9, 1), null, 700, true,
            "Bürgergeld", true, ct);
        foreach (var x in p)
        {
            await w.Integration.IdentitaetPruefenAsync(x.Id, Ausweisart.Aufenthaltstitel, true, null, ct);
        }
    }

    [Fact]
    public async Task Kursbeginn_ist_ohne_Berechtigung_und_Ausweispruefung_gesperrt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t);
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Ik);
            var u = (await w.Integration.UebersichtAsync(kurs.Id, ct))!;
            u.Kursbeginnhindernisse.Count.ShouldBe(6);
            var f = IntegrationDienst.KursbeginnFreigabe(u);
            f.Erlaubt.ShouldBeFalse();
            f.Begruendung!.ShouldContain("Kursstart gesperrt");
            await Should.ThrowAsync<InvalidOperationException>(() => w.Integration.KursbeginnMeldenAsync(kurs.Id, ct));
            await Should.ThrowAsync<InvalidOperationException>(() => Erzeugen(t, db, Tresen).Integration.IdentitaetPruefenAsync(p[0].Id, Ausweisart.Reisepass, true, null, ct));
            await Should.ThrowAsync<InvalidOperationException>(() => w.Integration.IdentitaetPruefenAsync(p[0].Id, Ausweisart.Reisepass, false, null, ct)); // Abweichung braucht Notiz
        }

        await BerechtigungenUndAusweiseAsync(t, p);
        await using (var db = t.NeuerKontext())
        {
            var vorgang = await Erzeugen(t, db, Ik).Integration.KursbeginnMeldenAsync(kurs.Id, ct);
            vorgang.ShouldStartWith("BAMF-MOCK-KB-");
            var meldung = await db.Postausgang.SingleAsync(n => n.Betreff == "Kursbeginnmeldung IK-2026-0815", ct);
            meldung.Inhalt.ShouldContain("TN-1001, TN-1002, TN-1003");
            meldung.Inhalt.ShouldNotContain("Vorname");
            (await db.Set<Identitaetspruefung>().CountAsync(ct)).ShouldBe(3); // nur Datum, Person, Art, Ergebnis – keine Kopie
            (await db.Protokoll.AnyAsync(e => e.Beschreibung.Contains("Vorname"), ct)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Sichtvergleich_mit_Zweifel_macht_die_Anwesenheit_vorlaeufig_bis_zur_Ausweiskontrolle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t);
        await BerechtigungenUndAusweiseAsync(t, p);
        string ausweis;
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Ik);
            await Should.ThrowAsync<InvalidOperationException>(() => w.Integration.LichtbildSpeichernAsync(p[0].Id, kurs.Id, [1, 2, 3], "image/jpeg", false, ct)); // ohne Art. 13
            await w.Integration.LichtbildSpeichernAsync(p[0].Id, kurs.Id, [1, 2, 3], "image/jpeg", true, ct);
            (await db.Set<Lichtbild>().SingleAsync(ct)).LoeschenAb.ShouldBe(new DateOnly(2026, 12, 7).AddDays(14)); // letzter Termin 07.12. + 14 Tage
            ausweis = w.Zugang.AusweisErzeugen(p[0].Id);
            await db.SaveChangesAsync(ct);
        }

        t.Uhr.Advance(TimeSpan.FromHours(24 + 0.1)); // Mo 16.11., 08:06
        Guid vid;
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Ik);
            vid = (await db.Veranstaltungen.SingleAsync(v => v.KursId == kurs.Id && v.Beginn == new DateTime(2026, 11, 16, 8, 0, 0), ct)).Id;
            var sv = (await w.Integration.AusweisScannenAsync(vid, ausweis, ct))!;
            sv.PersonId.ShouldBe(p[0].Id);
            sv.LichtbildDatenUri.ShouldBe("data:image/jpeg;base64,AQID");
            sv.AusweisGeprueft.ShouldBeTrue();
            (await db.Set<Anwesenheit>().AnyAsync(ct)).ShouldBeFalse(); // erst nach dem Sichtvergleich
            var antwort = await w.Integration.SichtvergleichAsync(vid, p[0].Id, false, ct);
            antwort.Ergebnis.Text.ShouldContain("Vorläufig");
        }

        await using (var db = t.NeuerKontext())
        {
            var a = await db.Set<Anwesenheit>().SingleAsync(ct);
            a.Vorlaeufig.ShouldBeTrue();
            a.VorlaeufigGrund.ShouldBe(IntegrationDienst.GrundZweifel);
            (await Erzeugen(t, db, Benutzer.Automatik).Routinen.AufgabenAbgleichenAsync(ct)).Neu.ShouldBeGreaterThanOrEqualTo(1);
            (await db.Aufgaben.SingleAsync(x => x.Typ == Aufgabentypen.Ausweiskontrolle, ct)).Gruppe.ShouldBe(nameof(Rolle.IntegrationskursTeam));
            var u = (await Erzeugen(t, db, Ik).Integration.UebersichtAsync(kurs.Id, ct))!;
            var tn = u.Teilnehmende.Single(x => x.Belegung.PersonId == p[0].Id);
            tn.Abschnitte[0].Vorlaeufig.ShouldBe(5.33m);
            tn.Abschnitte[0].Anwesend.ShouldBe(0);
        }

        await using (var db = t.NeuerKontext())
        {
            await Erzeugen(t, db, Ik).Integration.AusweiskontrolleAbschliessenAsync(p[0].Id, Ausweisart.Aufenthaltstitel, true, null, ct);
            (await db.Set<Anwesenheit>().SingleAsync(ct)).Vorlaeufig.ShouldBeFalse();
            (await Erzeugen(t, db, Benutzer.Automatik).Routinen.AufgabenAbgleichenAsync(ct)).Geschlossen.ShouldBe(1);
        }

        // Kursende + 14 Tage: Lichtbild wird gelöscht.
        t.Uhr.Advance(TimeSpan.FromDays(36));
        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db, Benutzer.Automatik).Integration.LichtbilderLoeschenAsync(ct)).ShouldBe(1);
            var bild = await db.Set<Lichtbild>().SingleAsync(ct);
            bild.Inhalt.ShouldBeEmpty();
            bild.GeloeschtAm.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Abschnitt_wird_UE_genau_abgerechnet_Kostenbeitrag_und_BAMF()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t);
        await BerechtigungenUndAusweiseAsync(t, p);

        // Kostenbeitrag vorab: nur die nicht befreite Person (16 UE × 2,29 €)
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Ik);
            (await w.Integration.KostenbeitragAbrechnenAsync(kurs.Id, 1, ct)).ShouldBe(1);
            (await w.Integration.KostenbeitragAbrechnenAsync(kurs.Id, 1, ct)).ShouldBe(0);
            (await db.Set<Beleg>().SingleAsync(ct)).BetragCent.ShouldBe(16 * 229);
        }

        // Anwesenheit der drei Termine von Modul 1: P1 immer voll, P2 einmal 2 Stunden zu spät, P3 einmal entschuldigt
        t.Uhr.Advance(TimeSpan.FromDays(1) + TimeSpan.FromHours(5)); // 16.11., 13:00 – drei Termine vorbei
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Ik);
            var termine = await db.Veranstaltungen.Where(v => v.KursId == kurs.Id).OrderBy(v => v.Beginn).Take(3).ToListAsync(ct);
            var bel = await db.Belegungen.Where(b => b.KursId == kurs.Id).ToListAsync(ct);
            Guid B(int i) => bel.Single(b => b.PersonId == p[i].Id).Id;
            foreach (var v in termine)
            {
                const string Papier = "Liste vom Papier übertragen";
                await w.Anwesenheit.ErfassenAsync(v.Id, B(0), Anwesenheitsstatus.Anwesend, null, Papier, ct);
                await w.Anwesenheit.ErfassenAsync(v.Id, B(1), Anwesenheitsstatus.Anwesend, v == termine[1] ? 120 : null, Papier, ct);
                await w.Anwesenheit.ErfassenAsync(v.Id, B(2), v == termine[2] ? Anwesenheitsstatus.Entschuldigt : Anwesenheitsstatus.Anwesend, null, Papier, ct);
            }

            var u = (await w.Integration.UebersichtAsync(kurs.Id, ct))!;
            u.Abschnitte[0].Beendet.ShouldBeTrue();
            u.Abschnitte[0].Gehalten.ShouldBe(16m);
            u.Teilnehmende.Single(x => x.Belegung.PersonId == p[1].Id).Abschnitte[0].Anwesend.ShouldBe(5.33m + 2.67m + 5.33m);
            u.Teilnehmende.Single(x => x.Belegung.PersonId == p[2].Id).Abschnitte[0].Entschuldigt.ShouldBe(5.33m);
            IntegrationDienst.AbrechnungFreigabe(u.Abschnitte[1]).Begruendung!.ShouldContain("läuft noch");

            var r = await w.Integration.BamfAbrechnenAsync(kurs.Id, 1, ct);
            r.Nummer.ShouldStartWith("RE-2026-");
            r.EmpfaengerName.ShouldBe(IntegrationDienst.BamfName);
            r.Positionen.Count.ShouldBe(3);
            r.Positionen[0].BetragCent.ShouldBe((long)Math.Round(15.99m * 471)); // befreit: voller Satz
            r.Positionen[1].BetragCent.ShouldBe((long)Math.Round(13.33m * (471 - 229))); // Kostenbeitrag zieht ab
            r.Xml.ShouldNotBeNull();
            await Should.ThrowAsync<InvalidOperationException>(() => w.Integration.BamfAbrechnenAsync(kurs.Id, 1, ct));
            var meldung = await db.Postausgang.SingleAsync(n => n.Betreff == "Abrechnung IK-2026-0815 Modul 1", ct);
            meldung.Inhalt.ShouldContain("TN-1002;nein;13,33");
            meldung.Inhalt.ShouldNotContain("Vorname");
        }
    }

    [Fact]
    public async Task Fahrtkosten_nur_fuer_Befreite_ab_drei_Kilometern_und_Auszahlung()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t);
        await BerechtigungenUndAusweiseAsync(t, p);
        await using var db = t.NeuerKontext();
        var w = Erzeugen(t, db, Ik);
        var antrag = await w.Integration.FahrtkostenBeantragenAsync(kurs.Id, p[0].Id, 2026, 11, 6300, abbruch: ct); // Greven → Emsdetten
        antrag.EntfernungKm.ShouldBeInRange(13m, 18m);
        (await Should.ThrowAsync<InvalidOperationException>(() => w.Integration.FahrtkostenBeantragenAsync(kurs.Id, p[1].Id, 2026, 11, 6300, abbruch: ct)))
            .Message.ShouldContain("kostenbefreiten");
        (await Should.ThrowAsync<InvalidOperationException>(() => w.Integration.FahrtkostenBeantragenAsync(kurs.Id, p[0].Id, 2026, 11, 6300, abbruch: ct)))
            .Message.ShouldContain("bereits ein Antrag");
        await w.Integration.FahrtkostenEntscheidenAsync(antrag.Id, true, null, ct);
        var g = await db.Set<Beleg>().SingleAsync(b => b.Art == Belegart.Gutschrift, ct);
        g.BetragCent.ShouldBe(6300);
        (await w.Finanzen.KontoAsync(p[0].Id, ct)).SaldoCent.ShouldBe(-6300);

        var b = Erzeugen(t, db, Buchhaltung);
        await b.Finanzen.BankverbindungSetzenAsync(p[0].Id, "Vorname1 Nachname1", "DE89 3704 0044 0532 0130 00", null, ct);
        var lauf = (await b.Zahlungen.UeberweisungVorschlagAsync(ct)).Lauf!;
        lauf.Posten.ShouldHaveSingleItem().Verwendungszweck.ShouldStartWith("Erstattung GS-2026-");
    }

    [Fact]
    public async Task Honorar_Fahrtkosten_nach_Entfernung_Wohnort_Kursort()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, _, dozentin) = await KursAsync(t);
        await using var db = t.NeuerKontext();
        var w = Erzeugen(t, db, Buchhaltung);
        await w.Honorare.VertragSpeichernAsync(dozentin.Id, null, "Rahmenvertrag", "pro-ue",
            new Dictionary<string, string?> { ["satzJeUe"] = "30", ["fahrtkosten"] = "km", ["kmSatz"] = "0,30" }, new DateOnly(2026, 1, 1), null, ct);
        var vorschau = await w.Honorare.VorschauAsync(dozentin.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), ct);
        var fahrt = vorschau.Berechnungen.Single().Ergebnis.Positionen.Where(x => x.Art == "Fahrtkosten").ToList();
        fahrt.Count.ShouldBe(2); // 02.11. und 09.11.
        fahrt[0].Menge!.Value.ShouldBeInRange(26m, 36m); // Greven ↔ Emsdetten, hin und zurück (Luftlinie × 1,3)
        fahrt[0].Text.ShouldContain("km × 0,30 €");
    }

    [Fact]
    public async Task Pruefungen_DTZ_und_LiD_mit_Anmeldung_und_Ergebnis()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t);
        await using (var db = t.NeuerKontext())
        {
            await Should.ThrowAsync<InvalidOperationException>(async () =>
            {
                var w0 = Erzeugen(t, db, Ik);
                var pr0 = await w0.Integration.PruefungAnlegenAsync(Pruefungsart.Dtz, new DateTime(2027, 1, 16, 9, 0, 0), "Emsdetten", kurs.Id, ct);
                await w0.Integration.PruefungAnmeldenAsync(pr0.Id, [p[0].Id], ct); // ohne Berechtigung
            });
        }

        await BerechtigungenUndAusweiseAsync(t, p);
        await using var db2 = t.NeuerKontext();
        var w = Erzeugen(t, db2, Ik);
        var lid = await w.Integration.PruefungAnlegenAsync(Pruefungsart.Lid, new DateTime(2027, 1, 23, 9, 0, 0), "Emsdetten", kurs.Id, ct);
        (await w.Integration.PruefungAnmeldenAsync(lid.Id, [p[0].Id, p[1].Id], ct)).ShouldStartWith("BAMF-MOCK-PR-");
        var anmeldung = await db2.Set<Pruefungsanmeldung>().FirstAsync(a => a.PersonId == p[0].Id, ct);
        await Should.ThrowAsync<InvalidOperationException>(() => w.Integration.ErgebnisSetzenAsync(anmeldung.Id, Pruefungsergebnis.Bestanden, 14, ct));
        await Should.ThrowAsync<InvalidOperationException>(() => w.Integration.ErgebnisSetzenAsync(anmeldung.Id, Pruefungsergebnis.B1, null, ct));
        await w.Integration.ErgebnisSetzenAsync(anmeldung.Id, Pruefungsergebnis.Bestanden, 27, ct);
        (await db2.Set<Pruefungsanmeldung>().SingleAsync(a => a.Id == anmeldung.Id, ct)).Punkte.ShouldBe(27);
        (await db2.Postausgang.CountAsync(n => n.Betreff.StartsWith("Prüfungsanmeldung Lid"), ct)).ShouldBe(1);
    }
}
