using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Datenschutz;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Finanzen.Honorare;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Anmeldung;
using VibeVHS.Infrastructure.Aufgaben;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Datenschutz;
using VibeVHS.Infrastructure.Durchfuehrung;
using VibeVHS.Infrastructure.Finanzen;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Finanzen (I6): Belege nach GoBD, Teilnehmerkonto, SEPA, camt.053, Mahnwesen, Honorare, Prüfexport.</summary>
public class FinanzTests
{
    private static readonly Benutzer Tresen = new("katja.friesen", "Katja Friesen", Rolle.Teilnehmerservice);
    private static readonly Benutzer Buchhaltung = new("linda.lueke", "Linda Lüke", Rolle.Buchhaltung);
    private static readonly Benutzer Pruefer = new("pruefung", "Betriebsprüfung", Rolle.Pruefung);
    private static readonly Benutzer Vl = new("sonja.eickelkamp", "Sonja Eickelkamp", Rolle.Verwaltungsleitung);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private sealed record Welt(FinanzDienst Finanzen, ZahlungsDienst Zahlungen, MahnDienst Mahnungen, HonorarDienst Honorare, GobdDienst Gobd, FinanzRoutinen Routinen,
        AnmeldungDienst Anmeldung, DatenschutzDienst Datenschutz);

    private static Welt Erzeugen(TestDatenbank t, VibeVhsDbContext db, Benutzer wer)
    {
        var benutzer = new TestBenutzer(wer);
        var protokoll = new AuditProtokoll(db, benutzer, t.Uhr);
        var links = new Uebersetzungslinks(new Uri("https://vhs.example/"));
        var post = new PostausgangDienst(new NachrichtenSpeicher(db), protokoll, benutzer, links, t.Uhr);
        var anmeldungSpeicher = new AnmeldungSpeicher(db);
        var zugang = new VibeVHS.Application.Durchfuehrung.CheckinZugang(new ZugangstokenSpeicher(db), new VibeVHS.Application.Durchfuehrung.OeffentlicheAdresse(new Uri("https://vhs.example/")),
            protokoll, benutzer, t.Uhr);
        var anmeldung = new AnmeldungDienst(anmeldungSpeicher, new ProgrammSpeicher(db), new PersonenSpeicher(db), anmeldungSpeicher, new StammdatenSpeicher(db), post, protokoll,
            benutzer, t.Uhr, zugang, new QrCodeErzeuger());
        var dokumente = new DokumentDienst(new DokumentSpeicher(db), anmeldungSpeicher, new ProgrammSpeicher(db), new PersonenSpeicher(db), new DurchfuehrungSpeicher(db),
            new StammdatenSpeicher(db), new BriefPdf(new QrCodeErzeuger()), links, zugang, protokoll, benutzer, t.Uhr);
        var speicher = new FinanzSpeicher(db);
        var fp = new Finanzprotokoll(db, benutzer, t.Uhr);
        var finanzen = new FinanzDienst(speicher, fp, new PersonenSpeicher(db), new ProgrammSpeicher(db), new StammdatenSpeicher(db), dokumente, post, protokoll, benutzer, t.Uhr);
        var zahlungen = new ZahlungsDienst(speicher, fp, finanzen, new PersonenSpeicher(db), new StammdatenSpeicher(db), post, protokoll, benutzer, t.Uhr);
        var mahnungen = new MahnDienst(speicher, fp, finanzen, protokoll, benutzer, t.Uhr);
        var honorare = new HonorarDienst(speicher, fp, finanzen, new HonorarModelle([new HonorarProUe(), new HonorarBasisUeTeilnehmende()]), new PersonenSpeicher(db), protokoll, t.Uhr);
        var gobd = new GobdDienst(speicher, fp, finanzen, new DatenschutzSpeicher(db), new StammdatenSpeicher(db), post, protokoll, t.Uhr);
        var aufgaben = new AufgabenDienst(new AufgabenSpeicher(db), new StammdatenSpeicher(db), protokoll, benutzer, t.Uhr);
        var routinen = new FinanzRoutinen(finanzen, zahlungen, mahnungen, honorare, speicher, aufgaben, t.Uhr);
        var datenschutz = new DatenschutzDienst(new DatenschutzSpeicher(db), new PersonenSpeicher(db), post, protokoll, benutzer, new StammdatenSpeicher(db), t.Uhr);
        return new(finanzen, zahlungen, mahnungen, honorare, gobd, routinen, anmeldung, datenschutz);
    }

    /// <summary>Kurs 26H-811 ab 24.11.2026, 90 € je Person; Personen mit Adresse und Mail; eine freiberufliche Kursleitung.</summary>
    private static async Task<(Kurs Kurs, List<Person> Personen, Person Dozentin)> KursAsync(TestDatenbank t, int personen, Guid? auftraggeber = null)
    {
        var jetzt = t.Uhr.GetLocalNow();
        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 6, 30));
        var art = new Kursart("Sprachen", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var raum = new Ort(Ortsart.Raum, "Seminarraum", null, "A 1.12").MitKapazitaet(Nutzungsart.Seminar, 16);
        var dozentin = Person.Anlegen("Maria", "Rossi", new DateOnly(2020, 1, 1), jetzt.AddYears(-5), null, "maria@example.org");
        dozentin.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(2020, 1, 1), Erfassung.Erst(jetzt.AddYears(-5)));
        dozentin.BeschaeftigungAendernAb(Beschaeftigungsart.Freiberuflich, new DateOnly(2020, 1, 1), Erfassung.Erst(jetzt.AddYears(-5)));
        var kurs = new Kurs("26H-811", "Spanisch A1", semester, 8, art, 1, 10).MitTeam([new KursDozent(dozentin.Id, "Maria Rossi", true)]);
        kurs.EntgeltSetzenAb(Geld.Euro(90), null, semester.Beginn, Erfassung.Erst(jetzt.AddMonths(-3)));
        kurs.SerieFestlegen(new Serienregel
        {
            Start = new DateOnly(2026, 11, 2), Anzahl = 8, Wochentage = [DayOfWeek.Monday], Beginn = new TimeOnly(18, 0), DauerMinuten = 90, OrtIds = [raum.Id],
        }, Kalender.OhneFerien, _ => "A 1.12 Seminarraum", new DateOnly(2027, 6, 30));
        if (auftraggeber is { } a)
        {
            kurs.MitSichtbarkeit(Sichtbarkeit.Intern, a, "Stadtwerke Emsdetten");
        }

        kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
        var liste = Enumerable.Range(1, personen).Select(i =>
        {
            var p = Person.Anlegen($"Vorname{i}", $"Nachname{i}", new DateOnly(2025, 1, 1), jetzt.AddYears(-1), new DateOnly(1980, 1, i), $"p{i}@example.org");
            p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2025, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
            p.AdresseHinzufuegen(null, $"Musterweg {i}", "48282", "Emsdetten", new DateOnly(2025, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
            return p;
        }).ToList();

        await using var db = t.NeuerKontext();
        db.Semester.Add(semester);
        db.Kursarten.Add(art);
        db.Orte.Add(raum);
        db.Kurse.Add(kurs);
        db.Personen.Add(dozentin);
        db.Personen.AddRange(liste);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (kurs, liste, dozentin);
    }

    private static async Task<Belegung> AnmeldenAsync(TestDatenbank t, Kurs kurs, Person p, Zahlart zahlart = Zahlart.Ueberweisung)
    {
        await using var db = t.NeuerKontext();
        return (await Erzeugen(t, db, Tresen).Anmeldung.AnmeldenAsync(new AnmeldeAuftrag(p.Id, kurs.Id, zahlart), TestContext.Current.CancellationToken)).Belegung;
    }

    private static async Task<Rechnungslaufergebnis> RechnungslaufAsync(TestDatenbank t)
    {
        await using var db = t.NeuerKontext();
        return await Erzeugen(t, db, Benutzer.Automatik).Finanzen.RechnungslaufAsync(abbruch: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Rechnungslauf_schreibt_lueckenlos_fest_und_Storno_statt_Aenderung()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t, 3);
        var b1 = await AnmeldenAsync(t, kurs, p[0]);
        await AnmeldenAsync(t, kurs, p[1]);
        await AnmeldenAsync(t, kurs, p[2]);

        (await RechnungslaufAsync(t)).Rechnungen.ShouldBe(3);
        (await RechnungslaufAsync(t)).Rechnungen.ShouldBe(0); // nicht doppelt

        await using (var db = t.NeuerKontext())
        {
            var belege = await db.Set<Beleg>().OrderBy(b => b.Laufnummer).ToListAsync(ct);
            belege.Select(b => b.Nummer).ShouldBe(["RE-2026-00001", "RE-2026-00002", "RE-2026-00003"]);
            belege.ShouldAllBe(b => b.Sha256 == b.Pruefsumme());
            belege.ShouldAllBe(b => b.DokumentId != null);
            belege.ShouldAllBe(b => b.Faellig == new DateOnly(2026, 11, 29));
            var mail = await db.Postausgang.FirstAsync(n => n.Betreff == "Rechnung RE-2026-00001", ct);
            mail.WeitereAnhaenge.ShouldContain(a => a.Name == "RE-2026-00001.pdf" && a.Typ == "application/pdf;base64");
            mail.Uebersetzungslink.ShouldNotBeNull();
            var konto = await Erzeugen(t, db, Buchhaltung).Finanzen.KontoAsync(p[0].Id, ct);
            konto.SaldoCent.ShouldBe(9000);
            konto.OffeneForderungenCent.ShouldBe(9000);
        }

        // Festgeschrieben heißt: nicht ändern, nicht löschen – nur stornieren (mit Begründung).
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Buchhaltung);
            var r1 = await db.Set<Beleg>().SingleAsync(b => b.Nummer == "RE-2026-00001", ct);
            Should.Throw<InvalidOperationException>(() => r1.Festschreiben(new Nummernkreis(Belegart.Rechnung, 2026), t.Uhr.GetLocalNow(), "x"));
            await Should.ThrowAsync<InvalidOperationException>(() => w.Finanzen.EntwurfVerwerfenAsync(r1.Id, ct));
            await Should.ThrowAsync<InvalidOperationException>(() => w.Finanzen.StornierenAsync(r1.Id, " ", abbruch: ct));
            await Should.ThrowAsync<InvalidOperationException>(() => Erzeugen(t, db, Pruefer).Finanzen.StornierenAsync(r1.Id, "Test", abbruch: ct));
        }

        // Abmeldung: Rechnungslauf storniert die Rechnung (eigener Nummernkreis ST).
        await using (var db = t.NeuerKontext())
        {
            await Erzeugen(t, db, Tresen).Anmeldung.StornierenAsync(b1.Id, "krank", ct);
        }

        // Kurs läuft schon (seit 02.11.): Abmeldung nach Frist kostet laut AGB das volle Entgelt → Storno plus Gebührenrechnung.
        var lauf = await RechnungslaufAsync(t);
        (lauf.Stornos, lauf.Gebuehren).ShouldBe((1, 1));
        await using (var db = t.NeuerKontext())
        {
            var storno = await db.Set<Beleg>().SingleAsync(b => b.Art == Belegart.Storno, ct);
            storno.Nummer.ShouldBe("ST-2026-00001");
            storno.Text!.ShouldContain("Abmeldung");
            (await db.Set<Beleg>().SingleAsync(b => b.Nummer == "RE-2026-00001", ct)).StorniertDurchId.ShouldBe(storno.Id);
            var konto = await Erzeugen(t, db, Buchhaltung).Finanzen.KontoAsync(p[0].Id, ct);
            konto.Zeilen.Select(z => z.Beleg.Nummer).ShouldBe(["RE-2026-00001", "ST-2026-00001", "RE-2026-00004"]);
            konto.Zeilen[2].Beleg.Positionen[0].Art.ShouldBe(Positionsart.Gebuehr);
            konto.SaldoCent.ShouldBe(9000);

            var kette = await Erzeugen(t, db, Pruefer).Gobd.KettePruefenAsync(ct);
            kette.Unversehrt.ShouldBeTrue();
            kette.Eintraege.ShouldBeGreaterThanOrEqualTo(5);
            (await db.Set<Finanzprotokolleintrag>().AnyAsync(e => e.Nachher!.Contains("Vorname"), ct)).ShouldBeFalse(); // nur Pseudonyme

            // Manipulation am Protokoll fällt auf.
            await db.Database.ExecuteSqlAsync($"UPDATE Finanzprotokoll SET Nachher = 'geändert' WHERE Nr = 2", ct);
            var gebrochen = await Erzeugen(t, db, Pruefer).Gobd.KettePruefenAsync(ct);
            gebrochen.Unversehrt.ShouldBeFalse();
            gebrochen.FehlerNr.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Lastschrift_mit_Vorabankuendigung_pain008_und_camt_mit_Ruecklastschrift()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t, 2);
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Tresen);
            await Should.ThrowAsync<InvalidOperationException>(() => w.Finanzen.MandatAnlegenAsync(p[0].Id, "Vorname1 Nachname1", "DE89 3704 0044 0532 0130 01", null, new DateOnly(2026, 11, 1), ct));
            await w.Finanzen.MandatAnlegenAsync(p[0].Id, "Vorname1 Nachname1", "DE89 3704 0044 0532 0130 00", "COBADEFFXXX", new DateOnly(2026, 11, 1), ct);
            await w.Finanzen.MandatAnlegenAsync(p[1].Id, "Vorname2 Nachname2", Iban.AusBlzUndKonto("40050150", "123456"), null, new DateOnly(2026, 11, 1), ct);
        }

        await AnmeldenAsync(t, kurs, p[0], Zahlart.Lastschrift);
        await AnmeldenAsync(t, kurs, p[1], Zahlart.Lastschrift);
        await RechnungslaufAsync(t);

        Guid laufId;
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Buchhaltung);
            var lauf = await w.Zahlungen.LastschriftVorschlagAsync(ct);
            lauf.ShouldNotBeNull();
            lauf.Aktiv.Count().ShouldBe(2);
            lauf.Posten.ShouldAllBe(x => x.Sequenz == "FRST");
            laufId = lauf.Id;
            (await w.Routinen.AufgabenAbgleichenAsync(ct)).Neu.ShouldBe(1); // Aufgabe „Lastschriftlauf freigeben“
            await Should.ThrowAsync<InvalidOperationException>(() => w.Zahlungen.ExportierenAsync(laufId, ct)); // erst freigeben
            await w.Zahlungen.FreigebenAsync(laufId, ct);
        }

        string xml;
        await using (var db = t.NeuerKontext())
        {
            (await db.Postausgang.CountAsync(n => n.Betreff.StartsWith("Ankündigung SEPA-Lastschrift"), ct)).ShouldBe(2);
            var vorab = await db.Postausgang.FirstAsync(n => n.Betreff.StartsWith("Ankündigung SEPA-Lastschrift"), ct);
            vorab.Inhalt.ShouldContain("DE98ZZZ09999999999");
            vorab.Inhalt.ShouldContain("21.11.2026"); // heute + 6 Tage
            (_, xml) = await Erzeugen(t, db, Buchhaltung).Zahlungen.ExportierenAsync(laufId, ct);
        }

        xml.ShouldContain(SepaXml.Pain008);
        xml.ShouldContain("<NbOfTxs>2</NbOfTxs>");
        xml.ShouldContain("<CtrlSum>180.00</CtrlSum>");
        xml.ShouldContain("<SeqTp>FRST</SeqTp>");
        xml.ShouldContain("<MndtId>VHS-2026-000001</MndtId>");
        xml.ShouldContain("<BICFI>COBADEFFXXX</BICFI>");
        xml.ShouldContain("<Id>NOTPROVIDED</Id>");
        System.Xml.Linq.XDocument.Parse(xml).Root!.Name.NamespaceName.ShouldBe(SepaXml.Pain008);

        // Kontoauszug: beide Einzüge gebucht, einer kommt zurück (AM04).
        await using (var db = t.NeuerKontext())
        {
            var lauf = await db.Set<Zahlungslauf>().SingleAsync(ct);
            var einst = await db.Set<Finanzeinstellungen>().SingleAsync(ct);
            var p0 = lauf.Posten[0];
            var camt = Camt053.Erzeugen("AUSZUG-1", einst.Iban, new DateOnly(2026, 11, 21),
            [
                new(new DateOnly(2026, 11, 21), null, p0.BetragCent, p0.Name, p0.Iban, p0.Verwendungszweck, p0.EndToEndId, null),
                new(new DateOnly(2026, 11, 21), null, lauf.Posten[1].BetragCent, lauf.Posten[1].Name, lauf.Posten[1].Iban, lauf.Posten[1].Verwendungszweck, lauf.Posten[1].EndToEndId, null),
                new(new DateOnly(2026, 11, 21), null, -1250, "Sparkasse", null, "Kontofuehrung", null, null),
            ]);
            var w = Erzeugen(t, db, Buchhaltung);
            var ergebnis = await w.Zahlungen.EinlesenAsync("auszug1.xml", Encoding.UTF8.GetBytes(camt), ct);
            ergebnis.Umsaetze.ShouldBe(3);
            ergebnis.Sicher.ShouldBe(2);
            await Should.ThrowAsync<InvalidOperationException>(() => w.Zahlungen.EinlesenAsync("nochmal.xml", Encoding.UTF8.GetBytes(camt), ct));
            (await w.Zahlungen.SichereZuordnenAsync(abbruch: ct)).ShouldBe(2);
            (await w.Finanzen.KontoAsync(p[0].Id, ct)).SaldoCent.ShouldBe(0);
        }

        await using (var db = t.NeuerKontext())
        {
            var lauf = await db.Set<Zahlungslauf>().SingleAsync(ct);
            var einst = await db.Set<Finanzeinstellungen>().SingleAsync(ct);
            var p0 = lauf.Posten[0];
            var rueck = Camt053.Erzeugen("AUSZUG-2", einst.Iban, new DateOnly(2026, 11, 24),
                [new(new DateOnly(2026, 11, 24), null, -p0.BetragCent, p0.Name, p0.Iban, "RUECKLASTSCHRIFT", p0.EndToEndId, "AM04")]);
            var w = Erzeugen(t, db, Buchhaltung);
            var ergebnis = await w.Zahlungen.EinlesenAsync("auszug2.xml", Encoding.UTF8.GetBytes(rueck), ct);
            ergebnis.Sicher.ShouldBe(1);
            var umsatz = await db.Set<Umsatz>().SingleAsync(u => u.Rueckgabegrund == "AM04", ct);
            umsatz.VorschlagText!.ShouldContain("Deckung unzureichend");
            await w.Zahlungen.ZuordnenAsync(umsatz.Id, abbruch: ct);
            var konto = await w.Finanzen.KontoAsync(p0.PersonId, ct);
            konto.SaldoCent.ShouldBe(9000 + 300); // Rechnung wieder offen + Bankgebühr
            konto.Zeilen.ShouldContain(z => z.Beleg.Art == Belegart.Storno && z.Beleg.Text!.StartsWith("Rücklastschrift AM04"));
            (await w.Routinen.AufgabenAbgleichenAsync(ct)).Neu.ShouldBeGreaterThanOrEqualTo(1);
            (await db.Aufgaben.AnyAsync(a => a.Typ == Aufgabentypen.Ruecklastschrift && a.Status == Domain.Aufgaben.Aufgabenstatus.Offen, ct)).ShouldBeTrue();
            (await db.Set<SepaMandat>().SingleAsync(m => m.PersonId == p0.PersonId, ct)).Sequenz.ShouldBe("RCUR");
        }
    }

    [Fact]
    public async Task Einzug_und_Ruecklastschrift_im_selben_Auszug()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t, 1);
        await using (var db = t.NeuerKontext())
        {
            await Erzeugen(t, db, Tresen).Finanzen.MandatAnlegenAsync(p[0].Id, "Vorname1 Nachname1", "DE89 3704 0044 0532 0130 00", null, new DateOnly(2026, 11, 1), ct);
        }

        await AnmeldenAsync(t, kurs, p[0], Zahlart.Lastschrift);
        await RechnungslaufAsync(t);
        await using var db2 = t.NeuerKontext();
        var w = Erzeugen(t, db2, Buchhaltung);
        var lauf = (await w.Zahlungen.LastschriftVorschlagAsync(ct))!;
        await w.Zahlungen.FreigebenAsync(lauf.Id, ct);
        await w.Zahlungen.ExportierenAsync(lauf.Id, ct);
        var x = lauf.Posten[0];
        var einst = await w.Finanzen.EinstellungenAsync(ct);
        var camt = Camt053.Erzeugen("A-R", einst.Iban, new DateOnly(2026, 11, 24),
        [
            new(new DateOnly(2026, 11, 21), null, x.BetragCent, x.Name, x.Iban, x.Verwendungszweck, x.EndToEndId, null),
            new(new DateOnly(2026, 11, 24), null, -x.BetragCent, x.Name, x.Iban, "RUECKLASTSCHRIFT", x.EndToEndId, "MD06"),
        ]);
        (await w.Zahlungen.EinlesenAsync("a.xml", Encoding.UTF8.GetBytes(camt), ct)).Sicher.ShouldBe(2);
        (await w.Zahlungen.SichereZuordnenAsync(abbruch: ct)).ShouldBe(2);
        var konto = await w.Finanzen.KontoAsync(p[0].Id, ct);
        konto.Zeilen.ShouldContain(z => z.Beleg.Art == Belegart.Storno && z.Beleg.Text!.StartsWith("Rücklastschrift MD06"));
        konto.SaldoCent.ShouldBe(9000 + 300);
    }

    [Fact]
    public async Task Zuordnungsvorschlag_nach_Belegnummer_Betrag_und_Name()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t, 3);
        foreach (var x in p)
        {
            await AnmeldenAsync(t, kurs, x);
        }

        await RechnungslaufAsync(t);
        await using var db = t.NeuerKontext();
        var w = Erzeugen(t, db, Buchhaltung);
        var einst = await w.Finanzen.EinstellungenAsync(ct);
        var camt = Camt053.Erzeugen("A-3", einst.Iban, new DateOnly(2026, 11, 16),
        [
            new(new DateOnly(2026, 11, 16), null, 9000, "Vorname1 Nachname1", null, "RE-2026-00001 Spanisch", null, null),
            new(new DateOnly(2026, 11, 16), null, 4000, "Vorname2 Nachname2", null, "Rechnung RE-2026-00002 Teilzahlung", null, null),
            new(new DateOnly(2026, 11, 16), null, 9000, "NACHNAME3, VORNAME3", null, "Kursgebuehr", null, null),
            new(new DateOnly(2026, 11, 16), null, 1234, "Unbekannt", null, "Spende", null, null),
        ]);
        await w.Zahlungen.EinlesenAsync("a3.xml", Encoding.UTF8.GetBytes(camt), ct);
        var u = await db.Set<Umsatz>().OrderBy(x => x.Nr).ToListAsync(ct);
        u.Select(x => x.Sicherheit).ShouldBe([Zuordnungssicherheit.Sicher, Zuordnungssicherheit.Mittel, Zuordnungssicherheit.Mittel, Zuordnungssicherheit.Keine]);
        u[1].VorschlagText!.ShouldContain("Betrag weicht ab");
        await w.Zahlungen.ZuordnenAsync(u[1].Id, abbruch: ct); // Teilzahlung
        (await w.Finanzen.KontoAsync(p[1].Id, ct)).OffeneForderungenCent.ShouldBe(5000);
        await Should.ThrowAsync<InvalidOperationException>(() => w.Zahlungen.IgnorierenAsync(u[3].Id, "", ct));
        await w.Zahlungen.IgnorierenAsync(u[3].Id, "Spende, separat verbucht", ct);
    }

    [Fact]
    public async Task Mahnwesen_mit_Stufen_Gebuehr_und_Freigabe()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t, 1);
        await AnmeldenAsync(t, kurs, p[0]);
        await RechnungslaufAsync(t); // fällig 29.11.

        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db, Buchhaltung).Mahnungen.VorschlagAsync(abbruch: ct)).ShouldBeNull();
        }

        t.Uhr.Advance(TimeSpan.FromDays(21)); // 06.12.: 7 Tage nach Fälligkeit → Zahlungserinnerung
        Guid laufId;
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Benutzer.Automatik);
            var lauf = await w.Mahnungen.VorschlagAsync(abbruch: ct);
            lauf!.Posten.ShouldHaveSingleItem().Stufenname.ShouldBe("Zahlungserinnerung");
            laufId = lauf.Id;
            (await db.Set<Beleg>().CountAsync(b => b.Art == Belegart.Mahnung, ct)).ShouldBe(0); // nur Vorschlag
            await Should.ThrowAsync<InvalidOperationException>(() => Erzeugen(t, db, Tresen).Mahnungen.FreigebenAsync(laufId, abbruch: ct));
            (await Erzeugen(t, db, Buchhaltung).Mahnungen.FreigebenAsync(laufId, abbruch: ct)).ShouldBe(1);
        }

        t.Uhr.Advance(TimeSpan.FromDays(14)); // 20.12.: 14 Tage nach Frist der Erinnerung → 1. Mahnung mit 5 €
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Buchhaltung);
            var lauf = await w.Mahnungen.VorschlagAsync(abbruch: ct);
            lauf!.Posten.Single().GebuehrCent.ShouldBe(500);
            await w.Mahnungen.FreigebenAsync(lauf.Id, abbruch: ct);
            var mahnungen = await db.Set<Beleg>().Where(b => b.Art == Belegart.Mahnung).OrderBy(b => b.Laufnummer).ToListAsync(ct);
            mahnungen.Select(m => m.Nummer).ShouldBe(["MA-2026-00001", "MA-2026-00002"]);
            mahnungen[1].Text!.ShouldContain("1. Mahnung");
            (await w.Finanzen.KontoAsync(p[0].Id, ct)).SaldoCent.ShouldBe(9500);
            (await db.Postausgang.CountAsync(n => n.Betreff.StartsWith("Mahnung MA-"), ct)).ShouldBe(2);
        }

        // Neuer Vorschlag (2. Mahnung) – dann zahlt die Person: Der offene Vorschlag passt sich an und entfällt.
        t.Uhr.Advance(TimeSpan.FromDays(14));
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Buchhaltung);
            (await w.Mahnungen.VorschlagAsync(abbruch: ct))!.Posten.Single().Stufenname.ShouldBe("2. Mahnung");
            var einst = await w.Finanzen.EinstellungenAsync(ct);
            await w.Zahlungen.EinlesenAsync("z.xml", Encoding.UTF8.GetBytes(Camt053.Erzeugen("Z-1", einst.Iban, t.Uhr.GetLocalNow().Date is var d ? DateOnly.FromDateTime(d) : default,
                [new(DateOnly.FromDateTime(t.Uhr.GetLocalNow().Date), null, 9500, "Vorname1 Nachname1", null, "RE-2026-00001 und Mahngebuehr", null, null)])), ct);
            (await w.Zahlungen.SichereZuordnenAsync(abbruch: ct)).ShouldBe(0); // Betrag weicht ab (9.000 offen) → nur „wahrscheinlich“
            var u = await db.Set<Umsatz>().SingleAsync(ct);
            await w.Zahlungen.ZuordnenAsync(u.Id, abbruch: ct);
            (await w.Mahnungen.VorschlagAsync(nurAktualisieren: true, abbruch: ct)).ShouldBeNull();
            (await db.Set<Mahnlauf>().CountAsync(l => l.Status == Mahnlaufstatus.Verworfen, ct)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Honorarvertrag_Abrechnung_mit_Snapshot_Doppelschutz_und_Ueberweisung()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (_, _, dozentin) = await KursAsync(t, 0); // Termine 02.11. und 09.11. sind vorbei (heute 15.11.)
        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Buchhaltung);
            await Should.ThrowAsync<InvalidOperationException>(() => w.Honorare.VertragSpeichernAsync(dozentin.Id, null, "Rahmenvertrag", "pro-ue",
                new Dictionary<string, string?>(), new DateOnly(2026, 1, 1), null, ct)); // Pflichtparameter fehlt
            await w.Honorare.VertragSpeichernAsync(dozentin.Id, null, "Rahmenvertrag", "pro-ue",
                new Dictionary<string, string?> { ["satzJeUe"] = "30,00", ["fahrtkosten"] = "pauschal", ["fahrtPauschale"] = "5" }, new DateOnly(2026, 1, 1), null, ct);
            var vorschau = await w.Honorare.VorschauAsync(dozentin.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), ct);
            vorschau.Summe.ShouldBe(2 * (60m + 5m));
        }

        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Buchhaltung);
            (await w.Honorare.AbrechnungenErstellenAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), abbruch: ct)).Entwuerfe.ShouldBe(1);
            (await w.Honorare.AbrechnungenErstellenAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), abbruch: ct)).Entwuerfe.ShouldBe(0); // Doppelschutz
            var entwurf = await db.Set<Beleg>().SingleAsync(b => b.Art == Belegart.Honorarabrechnung, ct);
            entwurf.Festgeschrieben.ShouldBeFalse();
            entwurf.BetragCent.ShouldBe(13000);
            entwurf.Grundlage!.ShouldContain("\"Modell\":\"pro-ue\"");
            entwurf.Grundlage!.ShouldContain("\"Version\":\"1.0\"");
            entwurf.Grundlage!.ShouldContain("\"satzJeUe\":\"30.00\"");

            // Neue Vertragsversion ab 01.12. (Satz 35): der alte endet am 30.11.
            await w.Honorare.VertragSpeichernAsync(dozentin.Id, null, "Rahmenvertrag 2027", "pro-ue", new Dictionary<string, string?> { ["satzJeUe"] = "35" },
                new DateOnly(2026, 12, 1), null, ct);
            (await w.Honorare.VertraegeAsync(dozentin.Id, ct)).Select(v => v.GueltigBis).ShouldBe([new DateOnly(2026, 11, 30), null]);

            await w.Honorare.FreigebenAsync(entwurf.Id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var w = Erzeugen(t, db, Buchhaltung);
            var h = await db.Set<Beleg>().SingleAsync(b => b.Art == Belegart.Honorarabrechnung, ct);
            h.Nummer.ShouldBe("HO-2026-00001");
            h.Sha256.ShouldBe(h.Pruefsumme());
            (await w.Finanzen.KontoAsync(dozentin.Id, ct)).SaldoCent.ShouldBe(-13000); // Verbindlichkeit der VHS
            var (ohneKonto, anzahl) = await w.Zahlungen.UeberweisungVorschlagAsync(ct) is var r ? (r.OhneKonto, r.Lauf?.Aktiv.Count() ?? 0) : default;
            (ohneKonto, anzahl).ShouldBe((1, 0));
            await w.Finanzen.BankverbindungSetzenAsync(dozentin.Id, "Maria Rossi", "DE89 3704 0044 0532 0130 00", null, ct);
            var lauf = (await w.Zahlungen.UeberweisungVorschlagAsync(ct)).Lauf!;
            await w.Zahlungen.FreigebenAsync(lauf.Id, ct);
            var (_, xml) = await w.Zahlungen.ExportierenAsync(lauf.Id, ct);
            xml.ShouldContain(SepaXml.Pain001);
            xml.ShouldContain("<CtrlSum>130.00</CtrlSum>");
            xml.ShouldContain("Honorar HO-2026-00001");
        }
    }

    [Fact]
    public async Task Kurzfristig_abgesagter_Termin_wird_nach_Modell_vergutet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, _, dozentin) = await KursAsync(t, 0);
        await using (var db = t.NeuerKontext())
        {
            var k = await db.Kurse.Include(x => x.Veranstaltungen).SingleAsync(ct);
            var termin = k.Veranstaltungen.OrderBy(v => v.Beginn).First();
            termin.Ausfallen("Kursleitung krank", new DateTimeOffset(termin.Beginn.AddHours(-3), TimeSpan.FromHours(1)));
            await db.SaveChangesAsync(ct);
            var w = Erzeugen(t, db, Buchhaltung);
            await w.Honorare.VertragSpeichernAsync(dozentin.Id, kurs.Id, "Kursvertrag", "pro-ue",
                new Dictionary<string, string?> { ["satzJeUe"] = "30", ["ausfallProzent"] = "50" }, new DateOnly(2026, 1, 1), null, ct);
            var vorschau = await w.Honorare.VorschauAsync(dozentin.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), ct);
            vorschau.Summe.ShouldBe(30m + 60m); // 50 % von 2 UE + voller Termin
        }
    }

    [Fact]
    public async Task Firmenrechnung_mit_XRechnung_und_Pruefexport_mit_index_xml()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var org = new Organisation("Stadtwerke Emsdetten", Organisationsart.Firma, "Emsdetten");
        org.RechnungsdatenSetzen("Moorbrückenstraße 30\n48282 Emsdetten", "05566-12345-67", "rechnung@stadtwerke.example");
        await using (var db = t.NeuerKontext())
        {
            db.Add(org);
            await db.SaveChangesAsync(ct);
        }

        var (kurs, p, _) = await KursAsync(t, 2, org.Id);
        await AnmeldenAsync(t, kurs, p[0]);
        await AnmeldenAsync(t, kurs, p[1]);
        var ergebnis = await RechnungslaufAsync(t);
        ergebnis.Rechnungen.ShouldBe(0);
        ergebnis.Firmenrechnungen.ShouldBe(1);

        await using var db2 = t.NeuerKontext();
        var r = await db2.Set<Beleg>().SingleAsync(ct);
        r.OrganisationId.ShouldBe(org.Id);
        r.BetragCent.ShouldBe(18000);
        r.Xml!.ShouldContain(XRechnung.Spezifikation);
        r.Xml!.ShouldContain("<cbc:BuyerReference>05566-12345-67</cbc:BuyerReference>");
        r.Xml!.ShouldContain("<cbc:PayableAmount currencyID=\"EUR\">180.00</cbc:PayableAmount>");
        r.Xml!.ShouldContain("<cbc:ID>E</cbc:ID>");
        var mail = await db2.Postausgang.SingleAsync(n => n.Betreff == $"Rechnung {r.Nummer}", ct);
        mail.Empfaenger.ShouldBe("rechnung@stadtwerke.example");
        mail.WeitereAnhaenge.ShouldContain(a => a.Name.EndsWith("-xrechnung.xml"));

        var w = Erzeugen(t, db2, Pruefer);
        await Should.ThrowAsync<InvalidOperationException>(() => w.Gobd.UebergebenAsync(ct)); // Prüfung ändert nichts
        var (datei, zip) = await w.Gobd.PruefexportAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), ct);
        datei.ShouldBe("pruefexport-20260101-20261231.zip");
        using var archiv = new ZipArchive(new MemoryStream(zip));
        archiv.Entries.Select(e => e.Name).ShouldBe(["index.xml", "belege.csv", "positionen.csv", "finanzprotokoll.csv", "verfahrensdokumentation.md"], ignoreOrder: true);
        using var index = new StreamReader(archiv.GetEntry("index.xml")!.Open());
        var indexText = await index.ReadToEndAsync(ct);
        indexText.ShouldContain("gdpdu-01-09-2004.dtd");
        indexText.ShouldContain("<RecordDelimiter>&#13;&#10;</RecordDelimiter>");
        using var belege = new StreamReader(archiv.GetEntry("belege.csv")!.Open());
        (await belege.ReadToEndAsync(ct)).ShouldContain("RE-2026-00001;Rechnung;15.11.2026");

        var uebergabe = await Erzeugen(t, db2, Buchhaltung).Gobd.UebergebenAsync(ct);
        uebergabe!.Anzahl.ShouldBe(1);
        uebergabe.Inhalt.ShouldContain("RE-2026-00001;15.11.2026;Rechnung;O-");
        (await Erzeugen(t, db2, Buchhaltung).Gobd.UebergebenAsync(ct)).ShouldBeNull(); // nichts doppelt
        var doku = await Erzeugen(t, db2, Pruefer).Gobd.VerfahrensdokuAsync(ct);
        doku.ShouldContain("| Rechnung | 2026 | 1 |");
    }

    [Fact]
    public async Task Loeschverlangen_bei_Belegen_in_laufender_Frist_sperrt_bis_zum_Fristende()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _) = await KursAsync(t, 1);
        await AnmeldenAsync(t, kurs, p[0]);
        await RechnungslaufAsync(t);
        await using var db = t.NeuerKontext();
        var w = Erzeugen(t, db, Vl);
        await w.Datenschutz.GrundeinstellungenAsync(ct);
        var pruefung = await w.Datenschutz.PruefenAsync(p[0].Id, ct);
        pruefung.Entscheidung.Aktion.ShouldBe(Loeschaktion.Sperren);
        pruefung.Entscheidung.GesperrtBis.ShouldBe(new DateOnly(2034, 12, 31)); // Belegjahr 2026 + 8 Jahre ab Jahresende
        pruefung.Entscheidung.Gruende.ShouldContain(g => g.Contains("RE-2026-00001") || g.Contains("Buchungsbelege"));
    }
}
