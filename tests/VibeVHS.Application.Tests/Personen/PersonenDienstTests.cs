using Microsoft.Extensions.Time.Testing;
using VibeVHS.Application.Personen;

namespace VibeVHS.Application.Tests.Personen;

public class PersonenDienstTests
{
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2025, 7, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly SpeicherPersonen _speicher = new();
    private readonly SpeicherProtokoll _protokoll;
    private readonly PersonenDienst _dienst;

    public PersonenDienstTests()
    {
        _protokoll = new SpeicherProtokoll(FesterBenutzer.Krueger, _uhr);
        _dienst = new PersonenDienst(_speicher, _protokoll, _uhr);
    }

    [Fact]
    public async Task Namensaenderung_wird_gespeichert_und_mit_Grund_protokolliert()
    {
        var ct = TestContext.Current.CancellationToken;
        var anna = await _dienst.AnlegenAsync("Anna", "Schmidt", null, "anna@example.org", "de", ct);
        _uhr.Advance(TimeSpan.FromMinutes(5));

        await _dienst.NameAendernAbAsync(anna.Id, "Anna", "Becker", new DateOnly(2025, 6, 14), "Heirat", ct);

        _speicher.Speichervorgaenge.ShouldBe(2);
        _protokoll.Eintraege.Select(e => e.Aktion).ShouldBe(["Person.Angelegt", "Person.NameGeaendert"]);
        _protokoll.Eintraege[1].Beschreibung.ShouldBe("Name von Anna Schmidt ab 14.06.2025 geändert in Anna Becker. Grund: Heirat");
        _protokoll.Eintraege[1].ObjektId.ShouldBe(anna.Id);
    }

    [Fact]
    public async Task Korrektur_wird_als_rueckwirkend_protokolliert()
    {
        var ct = TestContext.Current.CancellationToken;
        var p = await _dienst.AnlegenAsync("Lena", "Wolff", null, null, "de", ct);
        _uhr.Advance(TimeSpan.FromDays(1));

        await _dienst.NameKorrigierenAsync(p.Id, "Lena", "Wolf", new DateOnly(2025, 7, 2), "Tippfehler", ct);

        _protokoll.Eintraege.Last().Beschreibung.ShouldContain("gilt rückwirkend ab 02.07.2025");
    }

    [Fact]
    public async Task Unbekannte_Person_fuehrt_zu_verstaendlichem_Fehler()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            _dienst.NameAendernAbAsync(Guid.NewGuid(), "A", "B", new DateOnly(2025, 1, 1), "x", TestContext.Current.CancellationToken));

        ex.Message.ShouldBe("Person nicht gefunden.");
    }
}
