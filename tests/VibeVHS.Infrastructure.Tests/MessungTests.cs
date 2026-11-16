using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Laufzeitmessungen für docs/messungen.md (Median aus 7 Läufen, Datenbankdatei wie im Betrieb). Nur auf Anforderung:
/// VIBEVHS_MESSUNG=3000 bzw. =30000 (Anzahl Personen). Ohne Variable ist der Test sofort grün.
/// </summary>
public sealed class MessungTests
{
    private sealed class Automatik : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(Benutzer.Automatik);
    }

    [Fact]
    public async Task Laufzeiten_messen()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("VIBEVHS_MESSUNG"), out var anzahl) || anzahl <= 0)
        {
            return;
        }

        var ct = TestContext.Current.CancellationToken;
        var datei = Path.Combine(Path.GetTempPath(), $"vibevhs-messung-{anzahl}-{Guid.NewGuid():N}.db");
        await using var t = await TestDatenbank.ErzeugenAsync(datei);
        var jetzt = t.Uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var aufbau = Stopwatch.StartNew();
        var daten = new VibeVHS.Adapters.Mocks.Testdaten.Massendaten().Erzeugen(anzahl, jetzt);
        foreach (var paket in daten.Personen.Chunk(2000))
        {
            await using var db = t.NeuerKontext();
            db.Personen.AddRange(paket);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            db.Organisationen.AddRange(daten.Organisationen);
            await db.SaveChangesAsync(ct);
        }

        var ids = daten.Personen.Select(p => p.Id).ToList();
        var semester = new Semester("Herbst 2026", "26H", heute.AddDays(-80), heute.AddDays(80));
        var art = new Kursart("Allgemein", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var orte = Enumerable.Range(0, 20).Select(i => new Ort(Ortsart.Raum, $"Raum {i}", null, $"R {i}").MitKapazitaet(Nutzungsart.Seminar, 20)).ToList();
        var kurse = anzahl >= 30000 ? 2000 : 500;
        await using (var db = t.NeuerKontext())
        {
            db.Semester.Add(semester);
            db.Kursarten.Add(art);
            db.Orte.AddRange(orte);
            for (var i = 0; i < kurse; i++)
            {
                var dozent = daten.Personen[(i * 7) % daten.Personen.Count];
                var kurs = new Kurs($"26H-{i:0000}", $"Kurs {i}", semester, 1 + (i % 8), art, 6, 15).MitTeam([new KursDozent(dozent.Id, true)]);
                kurs.EntgeltSetzenAb(Geld.Euro(60 + (i % 40)), null, semester.Beginn, Erfassung.Erst(jetzt.AddMonths(-3)));
                var start = heute.AddDays(-40 + (i % 60));
                kurs.SerieFestlegen(new Serienregel { Start = start, Anzahl = 10, Wochentage = [start.DayOfWeek], Beginn = new TimeOnly(8 + (i % 12), 0), DauerMinuten = 90,
                    OrtIds = [orte[i % orte.Count].Id], DozentId = dozent.Id }, Kalender.OhneFerien, heute.AddDays(120));
                kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
                db.Kurse.Add(kurs);
                for (var j = 0; j < 12; j++)
                {
                    db.Belegungen.Add(Belegung.Anlegen(kurs, ids[(i * 12 + j) % ids.Count], false, Buchungsweg.Tresen, new Buchender("Last", null, null), null, Zahlart.Ueberweisung, jetzt));
                }
            }

            await db.SaveChangesAsync(ct);
        }

        aufbau.Stop();
        var person = ids[ids.Count / 2];
        var ergebnisse = new List<(string Name, double Median)>();

        async Task Messen(string name, Func<VibeVhsDbContext, Task> arbeit)
        {
            var zeiten = new List<double>();
            for (var lauf = 0; lauf < 8; lauf++)
            {
                await using var db = t.NeuerKontext();
                var sw = Stopwatch.StartNew();
                await arbeit(db);
                if (lauf > 0)
                {
                    zeiten.Add(sw.Elapsed.TotalMilliseconds); // erster Lauf = Aufwärmen
                }
            }

            zeiten.Sort();
            ergebnisse.Add((name, zeiten[zeiten.Count / 2]));
        }

        Seitenschluessel? weiter = null;
        await Messen("Personenliste, erste Seite (50, nach Name)", async db =>
        {
            var seite = await new PersonenAbfrage(db, new DublettenSuche(db)).SeiteAsync(new PersonenFilter(), null, 50, ct);
            weiter = Seitenschluessel.Von(seite[^1]);
        });
        await Messen("davon: nur Sortierabfrage (50 Ids)", async db =>
            _ = await db.MitSortierschluessel(db.Personen.Where(p => p.Status != Personenstatus.Zusammengefuehrt), heute).Sortiert().Take(50).ToListAsync(ct));
        await Messen("davon: 50 Personen vollständig laden", async db =>
        {
            var auswahl = ids.Take(50).ToList();
            _ = await db.Personen.AsNoTracking().AsSplitQuery().Where(p => auswahl.Contains(p.Id)).ToListAsync(ct);
        });
        await Messen("Personenliste, Folgeseite (Keyset)", async db => (await new PersonenAbfrage(db, new DublettenSuche(db)).SeiteAsync(new PersonenFilter(), weiter, 50, ct)).Count.ShouldBe(50));
        await Messen("Filter Rolle Dozierende", async db => _ = await new PersonenAbfrage(db, new DublettenSuche(db)).SeiteAsync(new PersonenFilter(Rolle: Personenrolle.Dozierende), null, 50, ct));
        await Messen("Volltextsuche „müller“", async db => _ = await new PersonenAbfrage(db, new DublettenSuche(db)).SeiteAsync(new PersonenFilter("müller"), null, 50, ct));
        await Messen("Personendetail (laden + Stand heute)", async db =>
        {
            var p = await new PersonenSpeicher(db).LadenAsync(person, ct);
            _ = p!.StandAm(heute, jetzt);
        });
        await Messen($"Kursliste ({kurse} Kurse mit Terminen)", async db => _ = await db.Kurse.AsNoTracking().Include(k => k.Veranstaltungen).OrderBy(k => k.Kursnummer).ThenBy(k => k.Id).ToListAsync(ct));
        await Messen("davon: Kurse ohne Termine", async db => _ = await db.Kurse.AsNoTracking().OrderBy(k => k.Kursnummer).ThenBy(k => k.Id).ToListAsync(ct));
        await Messen("davon: alle Termine (mit Orten)", async db => _ = await db.Veranstaltungen.AsNoTracking().ToListAsync(ct));
        await Messen("Probe: alle Termine mit Orten (eine Abfrage)", async db => _ = await db.Veranstaltungen.AsNoTracking().AsSingleQuery().ToListAsync(ct));
        await Messen("Probe: Orte als Objekte (SelectMany)", async db => _ = await db.Veranstaltungen.AsNoTracking().SelectMany(v => v.Orte).ToListAsync(ct));
        await Messen("Probe: alle Termine mit Orten, verfolgt", async db => _ = await db.Veranstaltungen.ToListAsync(ct));
        await Messen("Probe: Orte allein (Projektion)", async db => _ = await db.Veranstaltungen.AsNoTracking().SelectMany(v => v.Orte).Select(o => o.OrtId).ToListAsync(ct));
        await Messen("davon: alle Termine ohne Orte (Projektion)", async db => _ = await db.Veranstaltungen.AsNoTracking().Select(v => new { v.Id, v.KursId, v.Beginn, v.Ende, v.DozentId }).ToListAsync(ct));
        await Messen("Kursliste der Oberfläche (alle Kurse des Semesters, Kennzahlen)", async db =>
            _ = await new ProgrammSpeicher(db).KurseAsync(new VibeVHS.Application.Programm.KursFilter(), ct));
        await Messen("Raumplan einer Woche", async db => _ = await new ProgrammSpeicher(db).TermineAsync(jetzt.Date, jetzt.Date.AddDays(7), ct));
        await Messen("Rundschreiben-Empfänger (alle Teilnehmenden)", async db =>
        {
            var b = new Automatik();
            var protokoll = new AuditProtokoll(db, b, t.Uhr);
            var post = new PostausgangDienst(new NachrichtenSpeicher(db), protokoll, b, new Uebersetzungslinks(new Uri("https://vhs.example/")), t.Uhr);
            _ = await new Rundschreiben(new PersonenAbfrage(db, new DublettenSuche(db), t.Uhr), post, new NachrichtenSpeicher(db), protokoll, t.Uhr)
                .VorschauAsync(new PersonenFilter(Rolle: Personenrolle.Teilnehmende), Kontaktzweck.Kursorganisation, ct);
        });
        // Startabgleich: Der Fortschreib-Job der Personen-Kopien entfällt mit der Normalisierung (keine Kopien mehr).

        // Abfrageplan der Personenliste (erste Seite und Folgeseite): nutzt SQLite den Index IX_PersonNamen_Sortierung (Collation DIN5007_2)?
        var plaene = new List<string>();
        await using (var db = t.NeuerKontext())
        {
            await db.Database.OpenConnectionAsync(ct);
            var basis = db.Personen.Where(p => p.Status != Personenstatus.Zusammengefuehrt);
            foreach (var (name, q) in new[]
                     {
                         ("erste Seite", db.MitSortierschluessel(basis, heute).Sortiert().Take(50)),
                         ("Folgeseite", db.MitSortierschluessel(basis, heute).Nach(weiter!.Nachname, weiter.Vorname, weiter.IdHex).Sortiert().Take(50)),
                     })
            {
                await using var cmd = q.CreateDbCommand();
                cmd.CommandText = "EXPLAIN QUERY PLAN " + cmd.CommandText;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var zeilenPlan = new List<string>();
                while (await r.ReadAsync(ct))
                {
                    zeilenPlan.Add(r.GetString(3));
                }

                plaene.Add($"{name}: {string.Join(" / ", zeilenPlan)}");
            }
        }

        var zeilen = string.Join("\n", ergebnisse.Select(e => $"| {e.Name} | {e.Median:0} ms |"));
        var bericht = $"MESSUNG {anzahl} Personen, {kurse} Kurse (Aufbau {aufbau.Elapsed.TotalSeconds:0} s)\n{zeilen}\nPLAN\n{string.Join("\n", plaene)}";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), $"vibevhs-messung-{anzahl}.txt"), bericht);
        TestContext.Current.SendDiagnosticMessage(bericht);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
