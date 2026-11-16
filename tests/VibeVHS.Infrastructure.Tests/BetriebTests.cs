using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VibeVHS.Application.Betrieb;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Betrieb;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Betrieb und Daten (I8): Auswertungen/Statistik, Export, Sicherung mit Wiederherstellungstest, Import, Lastmessung.</summary>
public class BetriebTests
{
    private static readonly Benutzer Leitung = new("eva.lang", "Eva Lang", Rolle.Leitung);
    private static readonly Benutzer FblFb8 = new("varvara.schatz", "Varvara Schatz", Rolle.Fachbereichsleitung, [Rolle.Fachbereichsleitung], [8]);
    private static readonly Benutzer System = new("amy.behrla", "Amy Behrla", Rolle.Systemverwaltung);
    private static readonly Benutzer Tresen = new("katja.friesen", "Katja Friesen", Rolle.Teilnehmerservice);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private static AuswertungsDienst Auswertung(TestDatenbank t, VibeVhsDbContext db, Benutzer wer)
    {
        var b = new TestBenutzer(wer);
        var protokoll = new AuditProtokoll(db, b, t.Uhr);
        return new AuswertungsDienst(new Auswertungsquelle(db, t.Uhr),
            new PostausgangDienst(new NachrichtenSpeicher(db), protokoll, b, new Uebersetzungslinks(new Uri("https://vhs.example/")), t.Uhr), new NachrichtenSpeicher(db), protokoll, b, t.Uhr);
    }

    /// <summary>Zwei Kurse (FB 5 und FB 8) mit Belegungen von Personen mit Geburtsdatum; einer davon unterbelegt.</summary>
    private static async Task DatenAsync(TestDatenbank t, int kurseJeFb = 1, int personen = 12)
    {
        var jetzt = t.Uhr.GetLocalNow();
        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 1, 31));
        var art = new Kursart("Allgemein", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        await using var db = t.NeuerKontext();
        db.Semester.Add(semester);
        db.Kursarten.Add(art);
        db.Fachbereiche.AddRange(new VibeVHS.Domain.Stammdaten.Fachbereich(5, "Fremdsprachen"), new VibeVHS.Domain.Stammdaten.Fachbereich(8, "Bewegung und Fitness"));
        var leute = Enumerable.Range(1, personen).Select(i =>
            Person.Anlegen($"V{i}", $"N{i}", new DateOnly(2025, 1, 1), jetzt.AddYears(-1), new DateOnly(1950 + (i * 5 % 60), 3, 1), null)).ToList();
        db.Personen.AddRange(leute);
        var kursNr = 0;
        foreach (var fb in new[] { 5, 8 })
        {
            for (var k = 0; k < kurseJeFb; k++)
            {
                var kurs = new Kurs($"26H-{fb}{kursNr++:00}", $"Kurs {fb}/{k}", semester, fb, art, fb == 8 ? 20 : 3, fb == 8 ? 25 : 10);
                kurs.EntgeltSetzenAb(Geld.Euro(80), null, semester.Beginn, Erfassung.Erst(jetzt.AddMonths(-3)));
                kurs.SerieFestlegen(new Serienregel { Start = new DateOnly(2026, 11, 2), Anzahl = 10, Wochentage = [DayOfWeek.Monday], Beginn = new TimeOnly(18, 0), DauerMinuten = 90, OrtIds = [Guid.NewGuid()] },
                    Kalender.OhneFerien, new DateOnly(2027, 6, 30));
                kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
                db.Kurse.Add(kurs);
                foreach (var p in leute.Take(fb == 8 ? 4 : 6))
                {
                    db.Belegungen.Add(Belegung.Anlegen(kurs, p.Id, false, Buchungsweg.Tresen, new Buchender("Test", null, null), null, Zahlart.Ueberweisung, jetzt));
                }
            }
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Statistik_nimmt_Geschlecht_zum_Kursdatum_und_Alter_zum_Jahresende()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await DatenAsync(t, personen: 6);
        var jetzt = t.Uhr.GetLocalNow();
        await using (var db = t.NeuerKontext())
        {
            // V1 (geb. 01.03.1955): Geschlecht „männlich“ zum Kursbeginn (02.11.2026), ab 01.12.2026 „weiblich“ – die Statistik 2026 zählt „männlich“.
            var p = (await db.Personen.ToListAsync(ct)).Single(x => x.Anzeigename == "V1 N1");
            p.AngabeAendern(MerkmalArt.Geschlecht, "männlich", new DateOnly(2025, 1, 1), Erfassung.Erst(jetzt), korrektur: false);
            p.AngabeAendern(MerkmalArt.Geschlecht, "weiblich", new DateOnly(2026, 12, 1), Erfassung.Aenderung(jetzt, "Personenstand geändert"), korrektur: false);
            await db.SaveChangesAsync(ct);
        }

        await using var db2 = t.NeuerKontext();
        var faelle = await new Auswertungsquelle(db2, t.Uhr).StatistikfaelleAsync(2026, ct);
        faelle.Count(f => f.Geschlecht == "männlich").ShouldBe(2); // in beiden Kursen (FB 5 und 8)
        faelle.ShouldNotContain(f => f.Geschlecht == "weiblich");
        // Altersgruppe zum 31.12.2026 (nicht „heute“): V1 ist dann 71.
        faelle.Select(f => f.Altersgruppe).ShouldContain(VibeVHS.Domain.Datenschutz.Statistik.Altersgruppe(new DateOnly(1955, 3, 1), new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public async Task Auswertungen_Fachbereichsfilter_Statistik_und_Export()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await DatenAsync(t);
        await using var db = t.NeuerKontext();
        var leitung = Auswertung(t, db, Leitung);
        var kurse = await leitung.TabelleAsync("kurse", new Auswertungsfilter(), ct);
        kurse.Zeilen.Count.ShouldBe(2);
        (await Auswertung(t, db, FblFb8).TabelleAsync("kurse", new Auswertungsfilter(), ct)).Zeilen.ShouldHaveSingleItem()[2].ShouldBe("8 Bewegung und Fitness");
        await Should.ThrowAsync<InvalidOperationException>(() => Auswertung(t, db, Tresen).TabelleAsync("kurse", new Auswertungsfilter(), ct));

        var unter = await leitung.TabelleAsync("unterbelegt", new Auswertungsfilter(), ct);
        unter.Zeilen.ShouldHaveSingleItem()[6].ShouldBe(16); // FB 8: 4 von mind. 20

        var dashboard = await leitung.DashboardAsync(null, ct);
        dashboard.Belegungen.ShouldBe(10);
        dashboard.Plaetze.ShouldBe(35);
        dashboard.Auslastung.ShouldBe(28.6m);

        var die = await leitung.TabelleAsync("die", new Auswertungsfilter(Jahr: 2026), ct);
        die.Zeilen.Select(z => z[0]).ShouldBe(["3 Gesundheit", "4 Sprachen"]);
        die.Zeilen.Sum(z => (int)z[1]!).ShouldBe(10);
        var csv = Encoding.UTF8.GetString(die.Csv());
        csv.ShouldStartWith("﻿Programmbereich;Belegungen");
        csv.ShouldNotContain("N1"); // nur aggregiert, keine Namen
        var xlsx = new TabellenExport().Xlsx(kurse);
        Encoding.ASCII.GetString(xlsx, 0, 2).ShouldBe("PK");

        await Should.ThrowAsync<InvalidOperationException>(() => Auswertung(t, db, FblFb8).UebermittelnAsync("die", 2026, ct));
        await leitung.UebermittelnAsync("wbg", 2026, ct);
        (await db.Postausgang.SingleAsync(ct)).Empfaenger.ShouldBe("WbG NRW (Mock)");
    }

    [Fact]
    public async Task Sicherung_mit_Pruefsumme_Wiederherstellungstest_und_Aufbewahrung()
    {
        var ct = TestContext.Current.CancellationToken;
        var ordner = Path.Combine(Path.GetTempPath(), $"vibevhs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ordner);
        var uhr = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 1, 30, 0, TimeSpan.FromHours(1)));
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(ordner, "vibevhs.db"), Pooling = false }.ToString();
            await using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(cs).Konfigurieren(streng: true).Options);
            await db.Database.MigrateAsync(ct);
            db.Personen.Add(Person.Anlegen("Anna", "Becker", new DateOnly(2025, 1, 1), uhr.GetLocalNow()));
            await db.SaveChangesAsync(ct);
            var konfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sicherung:Behalten"] = "2" }).Build();
            var b = new TestBenutzer(System);
            var sicherung = new SqliteSicherung(db, konfig, new AuditProtokoll(db, b, uhr), b, uhr);

            var e = await sicherung.SichernAsync(ct);
            e.Wiederherstellbar.ShouldBe(true);
            e.Pruefergebnis!.ShouldContain("Personen 1");
            e.Migration.ShouldNotBeNull();
            e.Sha256.Length.ShouldBe(64);
            await Should.ThrowAsync<InvalidOperationException>(() => new SqliteSicherung(db, konfig, new AuditProtokoll(db, new TestBenutzer(Tresen), uhr), new TestBenutzer(Tresen), uhr)
                .SichernAsync(ct));

            // Manipulierte Sicherung fällt beim Wiederherstellungstest auf.
            var pfad = Path.Combine(ordner, "sicherung", e.Datei);
            var bytes = await File.ReadAllBytesAsync(pfad, ct);
            bytes[^100] ^= 0xFF;
            await File.WriteAllBytesAsync(pfad, bytes, ct);
            var geprueft = await sicherung.PruefenAsync(e.Datei, ct);
            geprueft.Wiederherstellbar.ShouldBe(false);
            geprueft.Pruefergebnis!.ShouldContain("Prüfsumme");

            uhr.Advance(TimeSpan.FromSeconds(1));
            await sicherung.SichernAsync(ct);
            uhr.Advance(TimeSpan.FromSeconds(1));
            await sicherung.SichernAsync(ct);
            (await sicherung.ListeAsync(ct)).Count.ShouldBe(2); // die älteste ist aufgeräumt
            (await db.Protokoll.CountAsync(x => x.Aktion == "Betrieb.Sicherung", ct)).ShouldBe(3);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(ordner, true);
        }
    }

    [Fact]
    public async Task Import_aus_KuferSQL_CSV_mit_Probelauf_Dubletten_und_Altnummern()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using (var db0 = t.NeuerKontext())
        {
            db0.Personen.Add(Person.Anlegen("Lukas", "Becker", new DateOnly(2025, 1, 1), t.Uhr.GetLocalNow(), new DateOnly(2001, 8, 14)));
            await db0.SaveChangesAsync(ct);
        }

        var pfad = Path.Combine(AppContext.BaseDirectory, "kufer-export-beispiel.csv");
        var datei = ImportDienst.Lesen("kufer-export-beispiel.csv", await File.ReadAllBytesAsync(pfad, ct));
        datei.Kodierung.ShouldBe("UTF-8");
        datei.Trennzeichen.ShouldBe(';');
        datei.Zeilen.Count.ShouldBe(8);
        datei.Zeilen[2][4].ShouldBe("Kirchweg 5; Hinterhaus"); // Anführungszeichen mit Trennzeichen im Feld
        var zuordnung = ImportDienst.ZuordnungVorschlagen(datei.Spalten);
        zuordnung.Values.ShouldBe([Importfeld.Altnummer, Importfeld.Nachname, Importfeld.Vorname, Importfeld.Geburtsdatum, Importfeld.Strasse, Importfeld.Postleitzahl,
            Importfeld.Ort, Importfeld.EMail, Importfeld.Telefon]);

        // Windows-Zeichensatz wird erkannt.
        ImportDienst.Lesen("alt.csv", Encoding.Latin1.GetBytes("Name;Vorname\nMüller;Jörg\n")).Zeilen[0][0].ShouldBe("Müller");

        await using var db = t.NeuerKontext();
        var b = new TestBenutzer(System);
        var protokoll = new AuditProtokoll(db, b, t.Uhr);
        var dienst = new ImportDienst(new DublettenSuche(db), new PersonenSpeicher(db), new ImportSpeicher(db), protokoll, b, t.Uhr);
        var probe = await dienst.ProbelaufAsync(datei, zuordnung, ct);
        (probe.Neu, probe.Dubletten, probe.Fehler).ShouldBe((4, 2, 2));
        probe.Zeilen.Single(z => z.Nr == 7).Hinweis.ShouldContain("Nachname fehlt");
        probe.Zeilen.Single(z => z.Nr == 8).Hinweis.ShouldContain("Postleitzahl");
        probe.Zeilen.Single(z => z.Nr == 6).Status.ShouldBe(Importstatus.DubletteInDatei);
        probe.Zeilen.Single(z => z.Nr == 9).Status.ShouldBe(Importstatus.Dublette);
        (await db.Personen.CountAsync(ct)).ShouldBe(1); // Probelauf schreibt nichts

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new ImportDienst(new DublettenSuche(db), new PersonenSpeicher(db), new ImportSpeicher(db), protokoll, new TestBenutzer(Tresen), t.Uhr).ImportierenAsync(datei, zuordnung, ct));
        var ergebnis = await dienst.ImportierenAsync(datei, zuordnung, ct);
        ergebnis.Angelegt.ShouldBe(4);
        (await db.Personen.CountAsync(ct)).ShouldBe(5);
        var lauf = await db.Set<Importlauf>().SingleAsync(ct);
        lauf.Zuordnungen.Select(z => z.Altnummer).ShouldBe(["100231", "100232", "100233", "100234"]);
        var hildegard = await db.Personen.Include(p => p.Adressen).SingleAsync(p => p.Id == lauf.Zuordnungen[2].PersonId, ct);
        hildegard.Adressen.Single().Strasse.ShouldBe("Kirchweg 5; Hinterhaus");
        await Should.ThrowAsync<InvalidOperationException>(() => dienst.ImportierenAsync(datei, zuordnung, ct)); // gleiche Datei
    }

    [Fact]
    public async Task Import_nimmt_Personen_mit_nur_einem_Namen_an_und_weist_sie_aus()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using var db = t.NeuerKontext();
        var b = new TestBenutzer(System);
        var dienst = new ImportDienst(new DublettenSuche(db), new PersonenSpeicher(db), new ImportSpeicher(db), new AuditProtokoll(db, b, t.Uhr), b, t.Uhr);
        var datei = ImportDienst.Lesen("e.csv", Encoding.UTF8.GetBytes("Nr;Name;Vorname;NurEinName\n1;Rahimi;;\n2;Sukarno;;ja\n3;Widerspruch;Kai;ja\n4;Muster;Eva;\n"));
        var zuordnung = ImportDienst.ZuordnungVorschlagen(datei.Spalten);
        zuordnung.Values.ShouldContain(Importfeld.NurEinName);
        var probe = await dienst.ProbelaufAsync(datei, zuordnung, ct);
        (probe.Neu, probe.Fehler, probe.NurEinName).ShouldBe((3, 1, 2));
        probe.Zeilen[0].Hinweis.ShouldContain("nur ein Name laut Ausweis");
        probe.Zeilen[2].Hinweis.ShouldContain("Vorname angegeben");

        await dienst.ImportierenAsync(datei, zuordnung, ct);
        var jetzt = t.Uhr.GetLocalNow();
        var namen = (await db.Personen.Include(p => p.Namen).AsNoTracking().ToListAsync(ct)).Select(p => p.AktuellerName(jetzt)).ToList();
        namen.Where(n => n.NurEinName).Select(n => n.Nachname).Order().ShouldBe(["Rahimi", "Sukarno"]);
    }

    [Fact]
    public async Task Import_trennt_Titel_aus_dem_Namen_ab_und_uebernimmt_eine_Titelspalte()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using var db = t.NeuerKontext();
        var b = new TestBenutzer(System);
        var dienst = new ImportDienst(new DublettenSuche(db), new PersonenSpeicher(db), new ImportSpeicher(db), new AuditProtokoll(db, b, t.Uhr), b, t.Uhr);

        var ohneSpalte = ImportDienst.Lesen("a.csv", Encoding.UTF8.GetBytes("Nr;Name;Vorname\n1;Lüken;Dr. Kai\n2;Dr. Berg;Anna\n3;Ahlers;Jens\n"));
        var probe = await dienst.ProbelaufAsync(ohneSpalte, ImportDienst.ZuordnungVorschlagen(ohneSpalte.Spalten), ct);
        probe.Zeilen[0].Hinweis.ShouldContain("Titel „Dr.“");
        await dienst.ImportierenAsync(ohneSpalte, ImportDienst.ZuordnungVorschlagen(ohneSpalte.Spalten), ct);

        var mitSpalte = ImportDienst.Lesen("b.csv", Encoding.UTF8.GetBytes("Nr;Titel;Name;Vorname\n4;Prof.;Kranz;Dr. Eva\n"));
        var zuordnung = ImportDienst.ZuordnungVorschlagen(mitSpalte.Spalten);
        zuordnung.Values.ShouldContain(Importfeld.Titel);
        await dienst.ImportierenAsync(mitSpalte, zuordnung, ct);

        var personen = await db.Personen.Include(p => p.Namen).AsNoTracking().ToListAsync(ct);
        var jetzt = t.Uhr.GetLocalNow();
        string Name(Person p) => $"{p.Titel}|{p.AktuellerName(jetzt).Vorname}|{p.AktuellerName(jetzt).Nachname}";
        personen.Select(Name).Order().ShouldBe(["|Jens|Ahlers", "Dr.|Anna|Berg", "Dr.|Kai|Lüken", "Prof. Dr.|Eva|Kranz"]);
        personen.Single(p => p.Titel == "Dr." && p.Anzeigename.Contains("Kai")).Anzeigename.ShouldBe("Dr. Kai Lüken");
    }

    [Fact]
    public async Task Lastmessung_3000_Personen_500_Kurse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var jetzt = t.Uhr.GetLocalNow();
        var daten = new VibeVHS.Adapters.Mocks.Testdaten.Massendaten().Erzeugen(3000, jetzt);
        var sw = Stopwatch.StartNew();
        await using (var db = t.NeuerKontext())
        {
            db.Organisationen.AddRange(daten.Organisationen);
            db.Personen.AddRange(daten.Personen);
            await db.SaveChangesAsync(ct);
        }

        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 1, 31));
        var art = new Kursart("Allgemein", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var ids = daten.Personen.Select(p => p.Id).ToList();
        await using (var db = t.NeuerKontext())
        {
            db.Semester.Add(semester);
            db.Kursarten.Add(art);
            for (var i = 0; i < 500; i++)
            {
                var kurs = new Kurs($"26H-{i:000}", $"Kurs {i}", semester, 1 + (i % 8), art, 6, 15);
                kurs.EntgeltSetzenAb(Geld.Euro(60 + (i % 40)), null, semester.Beginn, Erfassung.Erst(jetzt.AddMonths(-3)));
                kurs.SerieFestlegen(new Serienregel { Start = new DateOnly(2026, 9, 1).AddDays(i % 60), Anzahl = 10, Wochentage = [(DayOfWeek)(1 + (i % 5))], Beginn = new TimeOnly(18, 0),
                    DauerMinuten = 90, OrtIds = [Guid.NewGuid()] }, Kalender.OhneFerien, new DateOnly(2027, 6, 30));
                kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
                db.Kurse.Add(kurs);
                for (var j = 0; j < 12; j++)
                {
                    db.Belegungen.Add(Belegung.Anlegen(kurs, ids[(i * 12 + j) % ids.Count], false, Buchungsweg.Tresen, new Buchender("Last", null, null), null, Zahlart.Ueberweisung, jetzt));
                }
            }

            await db.SaveChangesAsync(ct);
        }

        var aufbau = sw.Elapsed;
        var messung = new Dictionary<string, TimeSpan>();
        async Task Messen(string name, Func<VibeVhsDbContext, Task> arbeit)
        {
            await using var db = t.NeuerKontext();
            var s = Stopwatch.StartNew();
            await arbeit(db);
            messung[name] = s.Elapsed;
        }

        await Messen("Personensuche", async db => (await new PersonenAbfrage(db, new DublettenSuche(db)).SeiteAsync(new PersonenFilter("müller"), null, 50, ct)).Count.ShouldBeGreaterThan(0));
        await Messen("Kursliste (500)", async db => (await Auswertung(t, db, Leitung).TabelleAsync("kurse", new Auswertungsfilter(), ct)).Zeilen.Count.ShouldBe(500));
        await Messen("Dashboard", async db => (await Auswertung(t, db, Leitung).DashboardAsync(null, ct)).Belegungen.ShouldBe(6000));
        await Messen("VHS-Statistik", async db => (await Auswertung(t, db, Leitung).TabelleAsync("die", new Auswertungsfilter(Jahr: 2026), ct)).Zeilen.ShouldNotBeEmpty());
        await Messen("Excel-Export Kursliste", async db => new TabellenExport().Xlsx(await Auswertung(t, db, Leitung).TabelleAsync("kurse", new Auswertungsfilter(), ct)).Length.ShouldBeGreaterThan(0));

        var bericht = $"MESSUNG Aufbau {aufbau.TotalSeconds:0.0} s; " + string.Join("; ", messung.Select(m => $"{m.Key} {m.Value.TotalMilliseconds:0} ms"));
        TestContext.Current.SendDiagnosticMessage(bericht);
        Console.WriteLine(bericht);
        messung.Values.ShouldAllBe(d => d < TimeSpan.FromSeconds(5));
    }
}
