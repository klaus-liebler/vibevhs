using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Täglicher Fortschreib-Job: Änderungen mit Datum in der Zukunft (Rolle endet, Umzug ab, Name ab) erreichen die Lese-Caches der
/// Personentabelle an ihrem Tag – auch nach einer Pause des Servers.
/// </summary>
public sealed class ZwischenstandTests
{
    private sealed class Automatik : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(Benutzer.Automatik);
    }

    private static Zwischenstaende Dienst(TestDatenbank t, VibeVhsDbContext db)
        => new(new ZwischenstandSpeicher(db, t.Uhr), new AuditProtokoll(db, new Automatik(), t.Uhr), t.Uhr);

    [Fact]
    public async Task Zukuenftige_Aenderungen_werden_an_ihrem_Tag_in_die_Caches_uebernommen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var jetzt = t.Uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var p = Person.Anlegen("Rita", "Rolle", heute.AddYears(-2), jetzt.AddYears(-2));
        p.RolleHinzufuegen(Personenrolle.Teilnehmende | Personenrolle.Dozierende, heute.AddYears(-2), Erfassung.Erst(jetzt.AddYears(-2)));
        var adresse = p.AdresseHinzufuegen(null, "Altweg 1", "48282", "Emsdetten", heute.AddYears(-2), Erfassung.Erst(jetzt.AddYears(-2)));
        p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, adresse, heute.AddYears(-2), Erfassung.Erst(jetzt.AddYears(-2)));
        // Heute erfasst, wirksam in drei Tagen:
        p.RolleBeendenAb(Personenrolle.Dozierende, heute.AddDays(3), Erfassung.Aenderung(jetzt, "Vertrag endet"));
        p.UmzugAb(adresse, null, "Neuweg 2", "48268", "Greven", heute.AddDays(3), Erfassung.Aenderung(jetzt, "Umzug"));
        p.NameAendernAb("Rita", "Neumann", heute.AddDays(3), "Heirat", jetzt);
        await using (var db = t.NeuerKontext())
        {
            db.Personen.Add(p);
            await db.SaveChangesAsync(ct);
        }

        async Task<(Personenrolle Rollen, string? Wohnort, string Anzeigename)> CacheAsync()
        {
            await using var db = t.NeuerKontext();
            var x = await db.Personen.AsNoTracking().IgnoreAutoIncludes().SingleAsync(y => y.Id == p.Id, ct);
            return (x.Rollen, x.Wohnort, x.Anzeigename);
        }

        (await CacheAsync()).ShouldBe((Personenrolle.Teilnehmende | Personenrolle.Dozierende, "Emsdetten", "Rita Rolle"));

        await using (var db = t.NeuerKontext())
        {
            (await Dienst(t, db).FortschreibenAsync(ct)).Geaendert.ShouldBe(0); // heute noch nichts wirksam
        }

        // Server war zwei Tage aus: am vierten Tag holt der Lauf die verpassten Grenzen nach.
        t.Uhr.Advance(TimeSpan.FromDays(4));
        await using (var db = t.NeuerKontext())
        {
            var lauf = await Dienst(t, db).FortschreibenAsync(ct);
            (lauf.Geprueft, lauf.Geaendert).ShouldBe((1, 1));
        }

        (await CacheAsync()).ShouldBe((Personenrolle.Teilnehmende, "Greven", "Rita Neumann"));

        await using (var db = t.NeuerKontext())
        {
            (await Dienst(t, db).FortschreibenAsync(ct)).Geprueft.ShouldBe(0); // am selben Tag nur einmal
            (await db.Protokoll.CountAsync(e => e.Aktion == Zwischenstaende.Aktion, ct)).ShouldBe(2);
        }
    }

    [Fact]
    public async Task Namenskopien_in_Kursteam_kuenftigen_Terminen_und_Konto_werden_nachgezogen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var jetzt = t.Uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var anna = Person.Anlegen("Anna", "Schmidt", heute.AddYears(-3), jetzt.AddYears(-3));
        var semester = new VibeVHS.Domain.Programm.Semester("Herbst 2026", "26H", heute.AddDays(-60), heute.AddDays(120));
        var art = new VibeVHS.Domain.Programm.Kursart("Sprachkurs", VibeVHS.Domain.Programm.Nutzungsart.Seminar, [], VibeVHS.Domain.Programm.Buchungsrichtlinie.Oeffentlich);
        var kurs = new VibeVHS.Domain.Programm.Kurs("26H-501", "Englisch", semester, 5, art, 6, 14).MitTeam([new VibeVHS.Domain.Programm.KursDozent(anna.Id, "Anna Schmidt", true)]);
        kurs.SerieFestlegen(new VibeVHS.Domain.Programm.Serienregel
        {
            Start = heute.AddDays(-14), Anzahl = 4, Wochentage = [heute.AddDays(-14).DayOfWeek], Beginn = new TimeOnly(18, 0), DauerMinuten = 90, OrtIds = [Guid.NewGuid()],
            DozentId = anna.Id,
        }, VibeVHS.Domain.Programm.Kalender.OhneFerien, _ => "Raum", heute.AddDays(120));
        await using (var db = t.NeuerKontext())
        {
            db.Personen.Add(anna);
            db.Semester.Add(semester);
            db.Kursarten.Add(art);
            db.Kurse.Add(kurs);
            db.Benutzerkonten.Add(new Benutzerkonto("anna.schmidt", "Anna Schmidt", null, [Rolle.Dozierende], [5], anna.Id));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var p = await db.Personen.SingleAsync(x => x.Id == anna.Id, ct);
            p.NameAendernAb("Anna", "Becker", heute, "Heirat", t.Uhr.GetLocalNow());
            await db.SaveChangesAsync(ct);
            (await Dienst(t, db).FortschreibenAsync(ct)).Namenskopien.ShouldBe(2); // Kurs und Konto
        }

        await using (var db = t.NeuerKontext())
        {
            var k = await db.Kurse.Include(x => x.Veranstaltungen).SingleAsync(ct);
            k.Leitung!.Name.ShouldBe("Anna Becker");
            k.Veranstaltungen.Where(v => v.Beginn < jetzt.LocalDateTime).ShouldAllBe(v => v.DozentName == "Anna Schmidt"); // damaliger Name bleibt
            k.Veranstaltungen.Where(v => v.Beginn >= jetzt.LocalDateTime).ShouldAllBe(v => v.DozentName == "Anna Becker");
            (await db.Benutzerkonten.SingleAsync(ct)).Name.ShouldBe("Anna Becker");
        }

        // Korrektur für die Vergangenheit („hieß schon immer Schmitt“): wirkt auch auf vergangene Termine.
        t.Uhr.Advance(TimeSpan.FromDays(1));
        await using (var db = t.NeuerKontext())
        {
            var p = await db.Personen.SingleAsync(x => x.Id == anna.Id, ct);
            p.NameKorrigieren("Anna", "Schmitt", heute.AddDays(-1), "Tippfehler", t.Uhr.GetLocalNow());
            await db.SaveChangesAsync(ct);
            await Dienst(t, db).FortschreibenAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var k = await db.Kurse.Include(x => x.Veranstaltungen).SingleAsync(ct);
            k.Veranstaltungen.Where(v => v.Beginn < jetzt.LocalDateTime).ShouldAllBe(v => v.DozentName == "Anna Schmitt");
            k.Veranstaltungen.Where(v => v.Beginn >= jetzt.LocalDateTime).ShouldAllBe(v => v.DozentName == "Anna Becker");
        }
    }
}
