using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Architekturtests der Normalisierung (11/2026): Stammdatentabellen bekommen keine neuen, unklassifizierten Spalten und keine Lese-Caches (A),
/// berechnete Werte und Namen entsprechen zu jedem Datum ihrer bitemporalen Quelle (B), und Statistik- bzw. Namenskopien gibt es nur dort,
/// wo ein Beleg den Stand zum Erfassungszeitpunkt bewusst festhält (C).
/// </summary>
public sealed class ArchitekturTests
{
    private static VibeVhsDbContext ModellKontext()
    {
        // Nur das Modell wird gebraucht, keine Verbindung.
        return new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseNpgsql("Host=modell;Database=modell").Options);
    }

    private static Dictionary<string, List<string>> SpaltenJeTabelle(VibeVhsDbContext db)
        => db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is { } t && Spalteneinstufung.Tabellen.ContainsKey(t) && !e.IsOwned())
            .GroupBy(e => e.GetTableName()!)
            .ToDictionary(g => g.Key, g => g.SelectMany(e => e.GetProperties()).Where(p => !p.IsShadowProperty() || p.IsForeignKey())
                .Select(p => p.GetColumnName()).Distinct().Order().ToList());

    // ---------- A: jede Spalte ist eingestuft ----------

    [Fact]
    public void A_Jede_Spalte_der_Stammdatentabellen_ist_eingestuft()
    {
        using var db = ModellKontext();
        var ist = SpaltenJeTabelle(db);
        var fehler = new List<string>();
        foreach (var (tabelle, spalten) in Spalteneinstufung.Tabellen)
        {
            var vorhanden = ist.GetValueOrDefault(tabelle) ?? [];
            fehler.AddRange(vorhanden.Except(spalten.Keys).Select(s => $"{tabelle}.{s}: neue Spalte ohne Einstufung – Personenangaben bitte als versioniertes Merkmal, "
                                                                       + "sonst in Spalteneinstufung.cs als Fuehrend/Cache/Anonymisierungsrest eintragen"));
            fehler.AddRange(spalten.Keys.Except(vorhanden).Select(s => $"{tabelle}.{s}: in der Einstufung, aber nicht (mehr) im Modell"));
        }

        foreach (var (tabelle, spalten) in Spalteneinstufung.Tabellen)
        {
            fehler.AddRange(spalten.Where(s => s.Value.Art == Spaltenart.Cache)
                .Select(s => $"{tabelle}.{s.Key}: Lese-Cache (Kopie) – stattdessen zur Laufzeit aus der Quelle lesen (Index statt Kopie)"));
        }

        fehler.ShouldBeEmpty();
    }

    // ---------- C: keine Statistik- oder Namenskopien außerhalb der dokumentierten Ausnahmen ----------

    [Fact]
    public void C_Keine_Statistikspalten_und_Namenskopien_ausserhalb_der_Ausnahmen()
    {
        using var db = ModellKontext();
        // Benutzerkonten.Name ist nur bei Funktionskonten ohne Person gefüllt (führend), sonst leer.
        var erlaubteNamenskopien = new HashSet<string>(StringComparer.Ordinal) { "Benutzerkonten.Name" };
        var fehler = new List<string>();
        foreach (var e in db.Model.GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            var tabelle = e.GetTableName()!;
            foreach (var p in e.GetProperties())
            {
                var spalte = p.GetColumnName();
                if (spalte.StartsWith("Statistik", StringComparison.Ordinal))
                {
                    fehler.Add($"{tabelle}.{spalte}: Statistikangaben werden zur Laufzeit aus Merkmalen berechnet (nach Anonymisierung: Anonymisiert…)");
                }

                // „…Name“ neben einem Personen-Verweis (…PersonId/DozentId) ist eine Namenskopie
                if (spalte.EndsWith("Name", StringComparison.Ordinal) && tabelle != "PersonNamen"
                    && (spalte.Length > 4
                        ? e.GetProperties().Any(x => x.GetColumnName() == spalte[..^4] + "Id")
                        : e.GetProperties().Any(x => x.GetColumnName() == "PersonId"))
                    && !erlaubteNamenskopien.Contains($"{tabelle}.{spalte}") && !IstEingefroren(tabelle))
                {
                    fehler.Add($"{tabelle}.{spalte}: Namenskopie einer Person/Organisation – zur Laufzeit lesen oder als dokumentierten Cache eintragen");
                }
            }
        }

        fehler.ShouldBeEmpty();

        // Belege, Mahn- und Zahlungsläufe (Posten = Abbild des freigegebenen Laufs bzw. der SEPA-Datei) u. Ä. halten den Namen zum
        // Erfassungszeitpunkt bewusst fest.
        static bool IstEingefroren(string tabelle) => tabelle is "Belege" or "Mahnposten" or "Zahlungsposten" or "PersonVollmachten" or "PersonOrganisationen" or "Belegungen" or "Belegpositionen"
            or "Ermaessigungen" or "Anwesenheiten" or "Listenabschluesse" or "Schnellanmeldungen" or "Honorarvertraege";
    }

    // ---------- B: berechnete Werte und Namen == bitemporale Quelle zu jedem Datum (ohne Job, ohne Kopien) ----------

    private sealed class Automatik : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(Benutzer.Automatik);
    }

    [Fact]
    public async Task B_Berechnete_Werte_und_Namen_entsprechen_der_bitemporalen_Quelle_zum_jeweiligen_Datum()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var zufall = new Random(11_2026);
        var jetzt = t.Uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var personen = new List<Person>();
        for (var i = 0; i < 25; i++)
        {
            var p = Person.Anlegen($"Vor{i}", $"Nach{i}", heute.AddYears(-3), jetzt.AddYears(-3), heute.AddYears(-30 - i), $"p{i}@example.org");
            p.RolleHinzufuegen(Personenrolle.Teilnehmende | Personenrolle.Dozierende, heute.AddYears(-3), Erfassung.Erst(jetzt.AddYears(-3)));
            var a = p.AdresseHinzufuegen(null, "Weg 1", "48282", "Emsdetten", heute.AddYears(-3), Erfassung.Erst(jetzt.AddYears(-3)));
            p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, a, heute.AddYears(-3), Erfassung.Erst(jetzt.AddYears(-3)));
            // zufällige Änderungen in Vergangenheit und Zukunft
            var tag = heute.AddDays(zufall.Next(-200, 40));
            switch (zufall.Next(5))
            {
                case 0: p.NameAendernAb($"Vor{i}", $"Neu{i}", tag, "Heirat", jetzt); break;
                case 1: p.RolleBeendenAb(Personenrolle.Dozierende, tag, Erfassung.Aenderung(jetzt, "Ende")); break;
                case 2: p.UmzugAb(a, null, "Neuweg 2", "48268", "Greven", tag, Erfassung.Aenderung(jetzt, "Umzug")); break;
                case 3: p.AngabeAendern(MerkmalArt.Titel, "Dr.", tag, Erfassung.Aenderung(jetzt, "Promotion"), korrektur: false); break;
                default: p.SpracheAendernAb("tr", tag, Erfassung.Aenderung(jetzt, "Wunsch")); break;
            }

            personen.Add(p);
        }

        var semester = new Semester("Herbst", "26H", heute.AddDays(-120), heute.AddDays(120));
        var art = new Kursart("Sprachkurs", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var kurse = personen.Take(10).Select((p, i) =>
        {
            var k = new Kurs($"26H-{600 + i}", $"Kurs {i}", semester, 5, art, 1, 10).MitTeam([new KursDozent(p.Id, true)]);
            var start = heute.AddDays(-70 + (i * 7));
            k.SerieFestlegen(new Serienregel { Start = start, Anzahl = 12, Wochentage = [start.DayOfWeek], Beginn = new TimeOnly(18, 0), DauerMinuten = 90, OrtIds = [Guid.NewGuid()], DozentId = p.Id },
                Kalender.OhneFerien, heute.AddDays(120));
            return k;
        }).ToList();
        await using (var db = t.NeuerKontext())
        {
            db.Personen.AddRange(personen);
            db.Semester.Add(semester);
            db.Kursarten.Add(art);
            db.Kurse.AddRange(kurse);
            await db.SaveChangesAsync(ct);
        }

        // Zeit läuft – ohne Job und ohne Kopien: beim Laden wird jeder Wert aus den Versionen zum jeweiligen Tag berechnet.
        foreach (var schritt in new[] { 0, 3, 10, 30, 45 })
        {
            t.Uhr.SetUtcNow(jetzt.ToUniversalTime().AddDays(schritt));
            var n = t.Uhr.GetLocalNow();
            var h = DateOnly.FromDateTime(n.Date);
            await using var pruef = t.NeuerKontext();
            foreach (var p in await pruef.Personen.AsNoTracking().ToListAsync(ct))
            {
                var s = p.StandAm(h, n);
                (p.Anzeigename, p.Rollen, p.Wohnort, p.Sprache, p.Titel, p.EMail, p.Geburtsdatum)
                    .ShouldBe((s.NameMitTitel, s.Rollen, s.Aufenthaltsort?.Ort, s.Sprache ?? "de", s.Titel, s.EMails.FirstOrDefault()?.Adresse, s.Geburtsdatum),
                        $"Wert von {p.Anzeigename} am {h:dd.MM.yyyy}");
            }

            var alle = await pruef.Personen.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
            var kurseMitTerminen = await pruef.Kurse.AsNoTracking().Include(x => x.Veranstaltungen).ToListAsync(ct);
            var namen = await new Namensquelle(pruef).FuerKurseAsync(kurseMitTerminen, ct);
            foreach (var k in kurseMitTerminen)
            {
                foreach (var v in k.Veranstaltungen.Where(v => v.DozentId is not null))
                {
                    namen.Dozent(v).ShouldBe(alle[v.DozentId!.Value].StandAm(DateOnly.FromDateTime(v.Beginn), n).NameMitTitel, $"Termin {k.Kursnummer} am {v.Beginn:dd.MM.yyyy}");
                }
            }
        }
    }
}
