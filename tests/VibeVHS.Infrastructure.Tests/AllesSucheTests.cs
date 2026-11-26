using VibeVHS.Application.Programm;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Alles-Suche (QA-61): fehlertolerante Personen, Funktionen nur mit Recht, Reihenfolge.</summary>
public sealed class AllesSucheTests
{
    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private static readonly Benutzer Tresen = new("katja.friedsen", "Katja Friedsen", Rolle.Teilnehmerservice);
    private static readonly Benutzer Fachbereich = new("varvara.schatte", "Varvara Schatte", Rolle.Fachbereichsleitung);
    private static readonly Benutzer Buchhaltung = new("bernd.buch", "Bernd Buch", Rolle.Buchhaltung);

    private static async Task<IReadOnlyList<Suchtreffer>> SuchenAsync(TestDatenbank t, Benutzer wer, string text)
    {
        await using var db = t.NeuerKontext();
        return await new GlobaleSuche(db, new PersonenAbfrage(db, new DublettenSuche(db), t.Uhr), new TestBenutzer(wer)).SuchenAsync(text, abbruch: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Findet_Personen_trotz_Tippfehler_und_Funktionen_nur_mit_Recht()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using (var db = t.NeuerKontext())
        {
            db.Personen.Add(Person.Anlegen("Karl", "Brenner", new DateOnly(2026, 1, 1), t.Uhr.GetLocalNow()));
            await db.SaveChangesAsync(ct);
        }

        (await SuchenAsync(t, Tresen, "Brnner")).ShouldContain(x => x.Typ == Treffertyp.Person && x.Titel == "Karl Brenner");

        // „Mahnwesen“: Buchhaltung darf, die Fachbereichsleitung nicht – dann erscheint es gar nicht.
        var buchhaltung = await SuchenAsync(t, Buchhaltung, "Mahnwesen");
        buchhaltung.First().ShouldBe(new Suchtreffer(Treffertyp.Funktion, "Mahnwesen", "Funktion öffnen", "finanzen?tab=mahnwesen"));
        (await SuchenAsync(t, Fachbereich, "Mahnwesen")).ShouldNotContain(x => x.Typ == Treffertyp.Funktion && x.Titel == "Mahnwesen");

        // Stichwort statt Titel, mit Tippfehler
        (await SuchenAsync(t, Buchhaltung, "lastschrft")).ShouldContain(x => x.Titel == "Zahlungsläufe");

        // Belege nur mit Leserecht auf Finanzen: ohne Recht keine Belegtreffer (auch keine Gruppe)
        (await SuchenAsync(t, Fachbereich, "R-2026")).ShouldNotContain(x => x.Typ == Treffertyp.Beleg);
    }

    [Fact]
    public async Task Findet_Personen_auch_ueber_frueheren_Namen_Geburtsdatum_und_EMail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using (var db = t.NeuerKontext())
        {
            var anna = Person.Anlegen("Anna", "Schmidt", new DateOnly(2020, 1, 1), t.Uhr.GetLocalNow().AddYears(-6), new DateOnly(1986, 8, 19), "anna.becker@example.org");
            anna.NameAendernAb("Anna", "Becker", new DateOnly(2025, 6, 1), "Heirat", t.Uhr.GetLocalNow());
            db.Personen.Add(anna);
            db.Personen.Add(Person.Anlegen("Bernd", "Brot", new DateOnly(2020, 1, 1), t.Uhr.GetLocalNow(), new DateOnly(1970, 1, 1)));
            await db.SaveChangesAsync(ct);
        }

        // Die Datenbank findet diese Personen über Versionen, Geburtsdatum und E-Mail – der Anzeigename ähnelt der Eingabe dabei nicht.
        foreach (var eingabe in new[] { "schmidt anna", "19.08.1986", "anna.becker@example.org" })
        {
            var treffer = await SuchenAsync(t, Tresen, eingabe);
            treffer.ShouldContain(x => x.Typ == Treffertyp.Person && x.Titel == "Anna Becker", eingabe);
            treffer.ShouldNotContain(x => x.Typ == Treffertyp.Person && x.Titel == "Bernd Brot", eingabe);
        }
    }
}
