using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Info-Monitore im Foyer (QA-65): Inhalt je Standort, Ausfälle, geheime Adresse, Vorschau 30 Minuten, Sperre, Rechte.</summary>
public sealed class InfomonitorTests
{
    private static readonly Benutzer Verwaltungsleitung = new("vera.leit", "Vera Leit", Rolle.Verwaltungsleitung);
    private static readonly Benutzer Tresen = new("katja.friesen", "Katja Friesen", Rolle.Teilnehmerservice);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private static InfomonitorDienst Dienst(TestDatenbank t, VibeVhsDbContext db, Benutzer wer)
        => new(new InfomonitorSpeicher(db), new ProgrammSpeicher(db), new AuditProtokoll(db, new TestBenutzer(wer), t.Uhr), new TestBenutzer(wer), t.Uhr);

    [Fact]
    public async Task Zeigt_die_Kurse_des_Standorts_mit_Ausfall_und_schuetzt_die_Adresse()
    {
        var ct = TestContext.Current.CancellationToken;
        Zeitzone.Festlegen("Europe/Berlin");
        await using var t = await TestDatenbank.ErzeugenAsync();
        var heute = DateOnly.FromDateTime(t.Uhr.GetLocalNow().LocalDateTime); // 15.11.2026, 8:00
        var haus = new Ort(Ortsart.Gebaeude, "Haus Emsdetten");
        var raum = new Ort(Ortsart.Raum, "Seminarraum", haus.Id, "E 1.01");
        var fremd = new Ort(Ortsart.Gebaeude, "Haus Greven");
        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 2, 28));
        var art = new Kursart("Sprachen", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var kurs = new Kurs("26H-100", "Englisch B1", semester, 8, art, 1, 12);
        kurs.EinzelveranstaltungHinzufuegen(heute.ToDateTime(new TimeOnly(7, 30)), heute.ToDateTime(new TimeOnly(9, 0)), [new VeranstaltungsOrt(raum.Id)], null);
        kurs.EinzelveranstaltungHinzufuegen(heute.ToDateTime(new TimeOnly(10, 0)), heute.ToDateTime(new TimeOnly(11, 30)), [new VeranstaltungsOrt(raum.Id)], null);
        var abend = kurs.EinzelveranstaltungHinzufuegen(heute.ToDateTime(new TimeOnly(18, 0)), heute.ToDateTime(new TimeOnly(19, 30)), [new VeranstaltungsOrt(raum.Id)], null);
        abend.Ausfallen("Kursleitung erkrankt");
        var anderswo = new Kurs("26H-200", "Yoga", semester, 3, art, 1, 12);
        anderswo.EinzelveranstaltungHinzufuegen(heute.ToDateTime(new TimeOnly(10, 0)), heute.ToDateTime(new TimeOnly(11, 0)), [new VeranstaltungsOrt(fremd.Id)], null);
        string geheimnis;
        await using (var db = t.NeuerKontext())
        {
            db.AddRange(haus, raum, fremd, semester, art, kurs, anderswo);
            await db.SaveChangesAsync(ct);
            await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, Tresen).AnlegenAsync("Foyer", haus.Id, null, ct));
            (_, geheimnis) = await Dienst(t, db, Verwaltungsleitung).AnlegenAsync("Foyer Emsdetten", haus.Id, null, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var inhalt = (await Dienst(t, db, Tresen).InhaltAsync(geheimnis, ct))!;   // Bildschirm braucht keine Rolle
            inhalt.Ort.ShouldBe("Haus Emsdetten");
            inhalt.Jetzt.Select(z => z.Beginn.Hour).ShouldBe([7]);
            inhalt.Spaeter.Select(z => (z.Beginn.Hour, z.Ausgefallen)).ShouldBe([(10, false), (18, true)]);
            inhalt.Spaeter.ShouldAllBe(z => z.Titel == "Englisch B1");                 // Greven erscheint nicht
            (await Dienst(t, db, Tresen).InhaltAsync("falsch", ct)).ShouldBeNull();
        }

        await using (var db = t.NeuerKontext())
        {
            var id = (await Dienst(t, db, Verwaltungsleitung).ListeAsync(ct)).Single().Id;
            var vorschau = await Dienst(t, db, Verwaltungsleitung).VorschauAsync(id, ct);
            (await Dienst(t, db, Tresen).InhaltAsync(vorschau, ct))!.Vorschau.ShouldBeTrue();
            t.Uhr.Advance(TimeSpan.FromMinutes(31));
            (await Dienst(t, db, Tresen).InhaltAsync(vorschau, ct)).ShouldBeNull();       // Vorschau abgelaufen
            (await Dienst(t, db, Tresen).InhaltAsync(geheimnis, ct)).ShouldNotBeNull();   // Gerät unverändert

            var neu = await Dienst(t, db, Verwaltungsleitung).NeueAdresseAsync(id, ct);
            (await Dienst(t, db, Tresen).InhaltAsync(geheimnis, ct)).ShouldBeNull();
            (await Dienst(t, db, Tresen).InhaltAsync(neu, ct)).ShouldNotBeNull();

            await Dienst(t, db, Verwaltungsleitung).SperrenAsync(id, true, ct);
            (await Dienst(t, db, Tresen).InhaltAsync(neu, ct)).ShouldBeNull();
            await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, Verwaltungsleitung).VorschauAsync(id, ct));
        }
    }
}
