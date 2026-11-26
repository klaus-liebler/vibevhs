using Microsoft.Extensions.Configuration;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Durchfuehrung;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Unterrichts-Apps (QA-69): Podium eingebaut und sofort nutzbar, angebundene Apps erst nach Freigabe, Einsatz je Kurs.</summary>
public sealed class UnterrichtsappTests
{
    private static readonly Benutzer Fachbereich = new("varvara.schatte", "Varvara Schatte", Rolle.Fachbereichsleitung);
    private static readonly Benutzer Leitung = new("kai.luedeking", "Dr. Kai Lüdeking", Rolle.Leitung);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private static UnterrichtsappDienst Dienst(TestDatenbank t, VibeVhsDbContext db, Benutzer wer)
    {
        var konfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Podium:Adresse"] = "https://vhs.example/podium/" }).Build();
        return new UnterrichtsappDienst(new UnterrichtsappSpeicher(db), new AuditProtokoll(db, new TestBenutzer(wer), t.Uhr), new TestBenutzer(wer), konfiguration, t.Uhr);
    }

    [Fact]
    public async Task Podium_eingebaut_angebundene_App_erst_nach_Freigabe_Einsatz_im_Kurs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 2, 28));
        var art = new Kursart("Sprachen", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var kurs = new Kurs("26H-400", "Englisch A2", semester, 5, art, 1, 12);
        await using var db = t.NeuerKontext();
        db.AddRange(semester, art, kurs);
        await db.SaveChangesAsync(ct);

        await Dienst(t, db, Benutzer.Automatik).GrundeinstellungenAsync(ct);
        await Dienst(t, db, Benutzer.Automatik).GrundeinstellungenAsync(ct); // nicht doppelt
        var podium = (await Dienst(t, db, Fachbereich).KatalogAsync(ct)).Single();
        podium.Eingebaut.ShouldBeTrue();
        podium.Nutzbar.ShouldBeTrue();

        var extern_ = await Dienst(t, db, Fachbereich).AnlegenAsync("Vokabeltrainer", Einbindung.Link, "https://trainer.example", "Vokabeln üben",
            "Anbieter in der EU, nur Vorname", ct);
        extern_.Eingebaut.ShouldBeFalse();
        var personId = Ids.Neu();
        (await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, Fachbereich).EinsetzenAsync(kurs.Id, personId, extern_.Id, "Lektion 3", null, ct)))
            .Message.ShouldContain("freigegeben");
        await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, Fachbereich).FreigebenAsync(extern_.Id, true, ct)); // nur Verwaltungsleitung/Leitung
        await Dienst(t, db, Leitung).FreigebenAsync(extern_.Id, true, ct);

        var e1 = await Dienst(t, db, Fachbereich).EinsetzenAsync(kurs.Id, personId, podium.Id, "Quiz zur Stunde", "PIN steht an der Leinwand", ct);
        await Dienst(t, db, Fachbereich).EinsetzenAsync(kurs.Id, personId, extern_.Id, "Lektion 3", null, ct);
        (await Dienst(t, db, Fachbereich).EinsaetzeAsync([kurs.Id], ct)).Count.ShouldBe(2);

        await Dienst(t, db, Fachbereich).BeendenAsync(e1.Id, kurs.Id, ct);
        (await Dienst(t, db, Fachbereich).EinsaetzeAsync([kurs.Id], ct)).Single().App.Name.ShouldBe("Vokabeltrainer");
        Should.Throw<ArgumentException>(() => new Unterrichtsapp("x", Einbindung.Link, "javascript:alert(1)", "", "x", t.Uhr.GetLocalNow()));
    }
}
