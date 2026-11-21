using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

/// <summary>Namensvorsatz und -zusatz: Anzeige „Anna von Berg jun.“, Anrede „Frau von Berg“, Sortierung nach „Berg“ (DIN 5007-2), bitemporal.</summary>
public class NamensvorsatzTests
{
    [Theory]
    [InlineData("Anna", "von Berg", "Anna", "von", "Berg", "")]
    [InlineData("Anna von", "Berg", "Anna", "von", "Berg", "")]
    [InlineData("Jan", "van der Meer", "Jan", "van der", "Meer", "")]
    [InlineData("Karl", "von und zu Guttenberg", "Karl", "von und zu", "Guttenberg", "")]
    [InlineData("Hans", "Müller jun.", "Hans", "", "Müller", "jun.")]
    [InlineData("Lena", "Vonhoff", "Lena", "", "Vonhoff", "")] // kein Vorsatz: „von“ nur mit Leerzeichen
    [InlineData("Max", "von", "Max", "", "von", "")]           // allein ist „von“ der Nachname
    public void Zerlegen(string vorname, string nachname, string v, string vs, string n, string z)
        => Namensvorsatz.Zerlegen(vorname, nachname).ShouldBe((v, vs, n, z));

    [Fact]
    public void Anzeige_Anrede_und_Sortierung()
    {
        var jetzt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(1));
        var p = Person.Anlegen("Anna", "Berg", new DateOnly(2020, 1, 1), jetzt.AddYears(-5), vorsatz: "von", zusatz: "jun.");
        p.TitelSetzen("Dr.");
        p.AngabeAendern(MerkmalArt.Anrede, "Frau", new DateOnly(2020, 1, 1), Erfassung.Erst(jetzt), korrektur: true);
        (p.Anzeigename, p.Sortiername).ShouldBe(("Dr. Anna von Berg jun.", "berg anna von"));
        p.Briefanrede(jetzt).ShouldBe("Sehr geehrte Frau Dr. von Berg");

        // Vorsatz ist Teil des versionierten Namens: Änderung ab Datum (z. B. Heirat ohne „von“)
        p.NameAendernAb("Anna", "Schulz", new DateOnly(2026, 6, 1), "Heirat", jetzt);
        p.NameAm(new DateOnly(2026, 5, 31), jetzt)!.Anzeige.ShouldBe("Anna von Berg jun.");
        p.NameAm(new DateOnly(2026, 6, 1), jetzt)!.Anzeige.ShouldBe("Anna Schulz");
    }
}
