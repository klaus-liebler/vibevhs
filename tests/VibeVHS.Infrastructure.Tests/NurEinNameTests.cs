using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Personen mit nur einem Namen laut Ausweis: werden gespeichert, gefunden und bei der Dublettenprüfung erkannt.</summary>
public sealed class NurEinNameTests
{
    [Fact]
    public async Task Speichern_und_Dublettenpruefung_ohne_Vorname()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var jetzt = t.Uhr.GetLocalNow();
        var geburt = new DateOnly(1990, 4, 12);
        await using (var db = t.NeuerKontext())
        {
            db.Personen.Add(Person.Anlegen("", "Rahimi", new DateOnly(2025, 1, 1), jetzt, geburt));
            await db.SaveChangesAsync(ct);
        }

        await using var db2 = t.NeuerKontext();
        var kandidaten = await new DublettenSuche(db2).KandidatenAsync("", "Rahimi", geburt, abbruch: ct);
        kandidaten.ShouldHaveSingleItem().Anzeigename.ShouldBe("Rahimi");
    }
}
