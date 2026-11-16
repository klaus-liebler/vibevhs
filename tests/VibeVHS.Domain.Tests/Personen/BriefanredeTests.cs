using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

/// <summary>Einheitliche Briefanrede: förmlich (Standard) oder informell, Titel vor dem Nachnamen, ohne Anrede mit vollem Namen.</summary>
public class BriefanredeTests
{
    [Theory]
    [InlineData("Frau", "Dr.", "Eva", "Muster", Anredestil.Foermlich, "Sehr geehrte Frau Dr. Muster")]
    [InlineData("Herr", "Prof. Dr.", "Kai", "Muster", Anredestil.Foermlich, "Sehr geehrter Herr Prof. Dr. Muster")]
    [InlineData("Herr", null, "Kai", "von Berg", Anredestil.Foermlich, "Sehr geehrter Herr von Berg")]
    [InlineData("Frau", "Dr.", "Eva", "Muster", Anredestil.Informell, "Guten Tag Frau Dr. Muster")]
    [InlineData(null, "Dr.", "Kim", "Muster", Anredestil.Foermlich, "Guten Tag Dr. Kim Muster")]
    [InlineData("Divers", null, "Kim", "Muster", Anredestil.Informell, "Guten Tag Kim Muster")]
    [InlineData("Keine Anrede", null, "Kim", "Muster", Anredestil.Foermlich, "Guten Tag Kim Muster")]
    public void Bilden(string? anrede, string? titel, string vorname, string nachname, Anredestil stil, string erwartet)
        => Briefanrede.Bilden(anrede, titel, vorname, nachname, stil).ShouldBe(erwartet);

    [Fact]
    public void Person_nutzt_Anrede_Titel_und_aktuellen_Namen()
    {
        var jetzt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(1));
        var p = Person.Anlegen("Eva", "Muster", new DateOnly(2020, 1, 1), jetzt.AddYears(-5));
        p.Briefanrede(jetzt).ShouldBe("Guten Tag Eva Muster");
        p.TitelSetzen("Dr.");
        p.AngabeAendern(MerkmalArt.Anrede, "Frau", new DateOnly(2020, 1, 1), VibeVHS.Domain.Gemeinsam.Erfassung.Erst(jetzt), korrektur: true);
        p.Briefanrede(jetzt).ShouldBe("Sehr geehrte Frau Dr. Muster");
        p.Briefanrede(jetzt, Anredestil.Informell).ShouldBe("Guten Tag Frau Dr. Muster");
    }
}
