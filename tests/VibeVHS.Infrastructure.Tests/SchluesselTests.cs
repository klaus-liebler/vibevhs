using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Aufgaben;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Schlüsselverwaltung (QA-67): Ausgabe nur an Kursleitungen/Mitarbeitende, eine offene Ausgabe je Schlüssel, Aufgabe zum Zurückfordern.</summary>
public sealed class SchluesselTests
{
    private static readonly Benutzer Tresen = new("katja.friesen", "Katja Friesen", Rolle.Teilnehmerservice);
    private static readonly Benutzer Buchhaltung = new("linda.lueke", "Linda Lüke", Rolle.Buchhaltung);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private static SchluesselDienst Dienst(TestDatenbank t, VibeVhsDbContext db, Benutzer wer)
    {
        var b = new TestBenutzer(wer);
        var protokoll = new AuditProtokoll(db, b, t.Uhr);
        var aufgaben = new AufgabenDienst(new AufgabenSpeicher(db), new StammdatenSpeicher(db), protokoll, b, t.Uhr);
        return new SchluesselDienst(new SchluesselSpeicher(db), new ProgrammSpeicher(db), new PersonenSpeicher(db), aufgaben, protokoll, b, t.Uhr);
    }

    [Fact]
    public async Task Ausgabe_Rueckgabe_und_Aufgabe_zum_Zurueckfordern()
    {
        var ct = TestContext.Current.CancellationToken;
        Zeitzone.Festlegen("Europe/Berlin");
        await using var t = await TestDatenbank.ErzeugenAsync();
        var jetzt = t.Uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.LocalDateTime);
        var schule = new Ort(Ortsart.Gebaeude, "Grundschule Saerbeck");
        var raum = new Ort(Ortsart.Raum, "Klasse 3a", schule.Id);
        var dozentin = Person.Anlegen("Petra", "Lehrmann", heute.AddYears(-1), jetzt.AddYears(-1));
        dozentin.RolleHinzufuegen(Personenrolle.Dozierende, heute.AddYears(-1), Erfassung.Erst(jetzt.AddYears(-1)));
        var teilnehmer = Person.Anlegen("Tom", "Teil", heute.AddYears(-1), jetzt.AddYears(-1));
        teilnehmer.RolleHinzufuegen(Personenrolle.Teilnehmende, heute.AddYears(-1), Erfassung.Erst(jetzt.AddYears(-1)));
        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 2, 28));
        var art = new Kursart("Sprachen", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var kurs = new Kurs("26H-300", "Italienisch", semester, 5, art, 1, 12).MitTeam([new KursDozent(dozentin.Id, true)]);
        kurs.EinzelveranstaltungHinzufuegen(heute.AddDays(7).ToDateTime(new TimeOnly(18, 0)), heute.AddDays(7).ToDateTime(new TimeOnly(19, 30)),
            [new VeranstaltungsOrt(raum.Id)], kurs.Team[0]);
        Guid schluesselId;
        await using (var db = t.NeuerKontext())
        {
            db.AddRange(schule, raum, dozentin, teilnehmer, semester, art, kurs);
            await db.SaveChangesAsync(ct);
            await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, Buchhaltung).AnlegenAsync("S-1", "Haupteingang", schule.Id, ct));
            schluesselId = (await Dienst(t, db, Tresen).AnlegenAsync("S-1", "Haupteingang", schule.Id, ct)).Id;
            await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, Tresen).AnlegenAsync("S-1", "doppelt", schule.Id, ct));
        }

        await using (var db = t.NeuerKontext())
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, Tresen).AusgebenAsync(schluesselId, teilnehmer.Id, heute, null, ct)))
                .Message.ShouldContain("nur an Kursleitungen");
            await Dienst(t, db, Tresen).AusgebenAsync(schluesselId, dozentin.Id, heute, "für den Italienischkurs", ct);
            await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, Tresen).AusgebenAsync(schluesselId, dozentin.Id, heute, null, ct));
            var zeile = (await Dienst(t, db, Tresen).ListeAsync(ct)).Single();
            zeile.Inhaber.ShouldBe("Petra Lehrmann");

            // Noch ein Einsatz am Ort (Raum unter dem Gebäude) in einer Woche: keine Aufgabe
            (await Dienst(t, db, Benutzer.Automatik).RueckgabeAufgabenAsync(ct)).Neu.ShouldBe(0);
        }

        // Sieben Wochen später kein Einsatz mehr: Aufgabe entsteht; nach der Rückgabe schließt sie sich selbst
        t.Uhr.Advance(TimeSpan.FromDays(49));
        await using (var db = t.NeuerKontext())
        {
            (await Dienst(t, db, Benutzer.Automatik).RueckgabeAufgabenAsync(ct)).Neu.ShouldBe(1);
            (await db.Aufgaben.SingleAsync(a => a.Typ == Aufgabentypen.SchluesselRueckgabe, ct)).Titel.ShouldBe("Schlüssel S-1 zurückfordern");
            var ausgabe = (await Dienst(t, db, Tresen).ListeAsync(ct)).Single().Ausgabe!;
            await Dienst(t, db, Tresen).ZurueckAsync(ausgabe.Id, DateOnly.FromDateTime(t.Uhr.GetLocalNow().LocalDateTime), ct);
            (await Dienst(t, db, Benutzer.Automatik).RueckgabeAufgabenAsync(ct)).Geschlossen.ShouldBe(1);
        }
    }
}
