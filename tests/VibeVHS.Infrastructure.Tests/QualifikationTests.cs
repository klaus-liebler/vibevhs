using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

public class QualifikationTests
{
    private static readonly Benutzer Fbl = new("varvara.schatz", "Varvara Schatz", Rolle.Fachbereichsleitung);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private static QualifikationsDienst Dienst(TestDatenbank t, VibeVhsDbContext db, Benutzer? wer = null)
    {
        var benutzer = new TestBenutzer(wer ?? Fbl);
        var protokoll = new AuditProtokoll(db, benutzer, t.Uhr);
        return new QualifikationsDienst(new QualifikationsSpeicher(db), new ProgrammSpeicher(db), new PersonenSpeicher(db),
            new PersonenAbfrage(db, new DublettenSuche(db)), protokoll, benutzer, t.Uhr,
            new Application.Aufgaben.AufgabenDienst(new Aufgaben.AufgabenSpeicher(db), new StammdatenSpeicher(db), protokoll, benutzer, t.Uhr));
    }

    /// <summary>Integrationskurs mit 12 Terminen ab 17.11.2026; Lehrkraft mit BAMF-Zulassung bis 31.12.2026; zweite Lehrkraft ohne.</summary>
    private static async Task<(Kurs Kurs, Person Mit, Person Ohne)> IntegrationskursAsync(TestDatenbank t)
    {
        var jetzt = t.Uhr.GetLocalNow();
        await using (var db = t.NeuerKontext())
        {
            await Dienst(t, db).GrundeinstellungenAsync(TestContext.Current.CancellationToken);
        }

        await using var db2 = t.NeuerKontext();
        var katalog = await db2.Merkmale.ToDictionaryAsync(m => m.Schluessel, TestContext.Current.CancellationToken);
        var mit = Person.Anlegen("Maria", "Rossi", new DateOnly(2020, 1, 1), jetzt.AddYears(-5));
        mit.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(2020, 1, 1), Erfassung.Erst(jetzt.AddYears(-5)));
        mit.QualifikationSetzenAb(katalog["bamf-lk"], "direkt", "Az. 81-4711", "BAMF", new DateOnly(2023, 1, 1), new DateOnly(2026, 12, 31), Erfassung.Erst(jetzt.AddYears(-3)));
        var ohne = Person.Anlegen("Peter", "Krause", new DateOnly(2020, 1, 1), jetzt.AddYears(-5));
        ohne.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(2020, 1, 1), Erfassung.Erst(jetzt.AddYears(-5)));

        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 1, 31));
        var art = new Kursart("Integrationskurs", Nutzungsart.Integrationskurs, [], Buchungsrichtlinie.Integrationskurs, Sichtbarkeit.Intern)
            .MitDozierendenmerkmalen(["bamf-lk"]);
        var kurs = new Kurs("26H-401", "Integrationskurs Modul 3", semester, 4, art, 8, 20).MitTeam([new KursDozent(mit.Id, true)]);
        kurs.SerieFestlegen(new Serienregel
        {
            Start = new DateOnly(2026, 11, 17), Anzahl = 12, Wochentage = [DayOfWeek.Tuesday], Beginn = new TimeOnly(8, 30), DauerMinuten = 240, OrtIds = [Guid.NewGuid()],
        }, Kalender.OhneFerien, new DateOnly(2027, 6, 30));
        kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
        db2.Personen.AddRange(mit, ohne);
        db2.Semester.Add(semester);
        db2.Kursarten.Add(art);
        db2.Kurse.Add(kurs);
        await db2.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (kurs, mit, ohne);
    }

    [Fact]
    public async Task Ablauf_mitten_in_der_Serie_markiert_Veranstaltungen_und_legt_Aufgaben_an()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, mit, _) = await IntegrationskursAsync(t);

        await using var db = t.NeuerKontext();
        var dienst = Dienst(t, db);
        var geladen = await db.Kurse.Include(k => k.Veranstaltungen).SingleAsync(k => k.Id == kurs.Id, ct);
        var betroffen = await dienst.BetroffeneVeranstaltungenAsync(geladen, ct);
        betroffen.Count.ShouldBe(5); // Termine ab 05.01.2027
        betroffen.Values.ShouldAllBe(e => e.Code == Eignungscode.Abgelaufen);

        (await dienst.AufgabenErzeugenAsync(abbruch: ct)).Neu.ShouldBe(2); // Ablauf in 46 Tagen + betroffener Einsatz
        var aufgaben = await db.Aufgaben.ToListAsync(ct);
        aufgaben.ShouldContain(a => a.Schluessel.StartsWith("qualifikation-ablauf:") && a.Titel.Contains("31.12.2026"));
        aufgaben.ShouldContain(a => a.Schluessel.StartsWith("qualifikation-einsatz:") && a.Titel.Contains("5 Veranstaltung(en) ab 05.01.2027"));
        aufgaben.ShouldAllBe(a => a.Gruppe == nameof(Rolle.Fachbereichsleitung));
        (await dienst.AufgabenErzeugenAsync(abbruch: ct)).ShouldBe(Application.Aufgaben.Abgleichergebnis.Leer); // nicht doppelt
        (await db.Protokoll.AnyAsync(e => e.Beschreibung.Contains("Maria"), ct)).ShouldBeFalse();
        _ = mit;
    }

    [Fact]
    public async Task Ungeeignete_Lehrkraft_wird_abgelehnt_und_bei_gesetzlichem_Merkmal_gibt_es_keine_Ausnahme()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, _, ohne) = await IntegrationskursAsync(t);

        await using var db = t.NeuerKontext();
        var dienst = Dienst(t, db);
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => dienst.TeamHinzufuegenAsync(kurs.Id, ohne.Id, false, null, ct));
        ex.Message.ShouldContain("BAMF-Lehrkraftzulassung");
        ex.Message.ShouldContain("fehlt");

        var mitBegruendung = await Should.ThrowAsync<InvalidOperationException>(() => dienst.TeamHinzufuegenAsync(kurs.Id, ohne.Id, false, "Vertretung", ct));
        mitBegruendung.Message.ShouldContain("nicht zulässig");
        (await db.Set<Qualifikationsausnahme>().CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Ausnahme_fuer_nicht_gesetzliches_Merkmal_wird_mit_Begruendung_protokolliert()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, _, ohne) = await IntegrationskursAsync(t);
        await using (var db = t.NeuerKontext())
        {
            var k = await db.Kurse.SingleAsync(x => x.Id == kurs.Id, ct);
            k.MitDozierendenmerkmalen(["telc-pruefer"]); // z. B. Prüfungsvorbereitung: Lizenz wünschenswert, nicht gesetzlich
            await db.SaveChangesAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var teilnehmerservice = new Benutzer("katja.friesen", "Katja Friesen", Rolle.Teilnehmerservice);
            await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, teilnehmerservice).TeamHinzufuegenAsync(kurs.Id, ohne.Id, false, "Vertretung", ct));
            await Dienst(t, db).TeamHinzufuegenAsync(kurs.Id, ohne.Id, false, "Vertretung im Dezember, Lizenz beantragt", ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var k = await db.Kurse.SingleAsync(x => x.Id == kurs.Id, ct);
            k.Team.ShouldContain(d => d.PersonId == ohne.Id);
            var a = await db.Set<Qualifikationsausnahme>().SingleAsync(ct);
            a.Begruendung.ShouldBe("Vertretung im Dezember, Lizenz beantragt");
            a.ErteiltVon.ShouldContain("Fachbereichsleitung");
            (await db.Protokoll.AnyAsync(e => e.Aktion == "Qualifikation.Ausnahme" && e.Beschreibung.Contains("Lizenz beantragt"), ct)).ShouldBeTrue();
        }
    }
}
