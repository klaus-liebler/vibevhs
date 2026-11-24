using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Tests.Gemeinsam;

public class UnscharfTests
{
    [Theory]
    [InlineData("Müller", "Hans Müller", 100)]
    [InlineData("mueller", "Hans Müller", 100)]
    [InlineData("muller", "Hans Müller", 100)]
    [InlineData("Bren", "Karl Brenner", 88)]
    [InlineData("Brnner", "Karl Brenner", 40)]
    [InlineData("Brenenr", "Karl Brenner", 40)]
    [InlineData("Schmitt", "Peter Schmidt", 40)]
    [InlineData("Kowalsky", "Jan Kowalski", 40)]
    [InlineData("ab", "Abel Kain", 87)]
    public void Bewertung(string eingabe, string name, int erwartet) => Unscharf.Bewertung(eingabe, name).ShouldBe(erwartet);

    [Theory]
    [InlineData("xy", "Hans Müller")]
    [InlineData("Meier Karl", "Hans Meier")]  // jedes Wort muss passen
    [InlineData("Bxxnner", "Karl Brenner")]  // zwei Fehler bei 7 Buchstaben sind zu viel
    [InlineData("", "Hans Müller")]
    public void Kein_Treffer(string eingabe, string name) => Unscharf.Passt(eingabe, name).ShouldBeFalse();

    [Fact]
    public void Ordnen_bester_Treffer_zuerst()
        => Unscharf.Ordnen("schmitt", ["Peter Schmidt", "Petra Schmitt", "Paul Meier"], n => n).ShouldBe(["Petra Schmitt", "Peter Schmidt"]);

    [Fact]
    public void Abstand_zaehlt_Vertauschung_als_einen_Fehler() => Unscharf.Abstand("brenenr", "brenner").ShouldBe(1);
}
