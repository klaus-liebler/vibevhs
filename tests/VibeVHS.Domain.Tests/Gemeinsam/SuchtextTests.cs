using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Tests.Gemeinsam;

public class SuchtextTests
{
    [Theory]
    [InlineData("Yılmaz", "Yilmaz")]
    [InlineData("Şahin", "Sahin")]
    [InlineData("Çelik", "Celik")]
    [InlineData("Müller", "Mueller")]
    [InlineData("Müller", "Muller")]
    [InlineData("Großmann", "Grossmann")]
    [InlineData("Öztürk", "Oeztuerk")]
    [InlineData("Wiśniewska", "Wisniewska")]
    [InlineData("José", "JOSE")]
    [InlineData("Al-Hassan", "al hassan")]
    public void Schreibweisen_werden_gleich_normalisiert(string a, string b)
    {
        Suchtext.Normalisieren(a).ShouldBe(Suchtext.Normalisieren(b));
    }

    [Fact]
    public void Leerzeichen_und_Satzzeichen_werden_vereinheitlicht()
    {
        Suchtext.Normalisieren("  Anna-Lena   Becker ").ShouldBe("anna lena becker");
        Suchtext.Woerter("Becker, Anna  Becker").ShouldBe(["becker", "anna"]);
    }

    [Fact]
    public void Datum_und_EMail_bleiben_suchbar()
    {
        Suchtext.Normalisieren("12.08.1986").ShouldBe("12.08.1986");
        Suchtext.Normalisieren("Anna.Becker@Example.org").ShouldBe("anna.becker@example.org");
    }

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("yilmaz", "yilmaz", 0)]
    [InlineData("schmidt", "schmitt", 1)]
    public void Abstand_zaehlt_Einfuegen_Loeschen_Ersetzen(string a, string b, int erwartet)
    {
        Suchtext.Abstand(a, b).ShouldBe(erwartet);
    }
}
