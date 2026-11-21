using VibeVHS.Domain.Stammdaten;

namespace VibeVHS.Domain.Tests.Gemeinsam;

public class EinrichtungImpressumTests
{
    private static Einrichtung Neu() => new("Volkshochschule Test", "vhs Test", ["Emsdetten"], "NRW");

    [Fact]
    public void Leeres_Impressum_bedeutet_Platzhalter()
    {
        var e = Neu();
        e.ImpressumSetzen("  \n ");
        e.Impressum.ShouldBeNull();
    }

    [Fact]
    public void Impressum_wird_getrimmt_und_mit_einheitlichen_Zeilenenden_gespeichert()
    {
        var e = Neu();
        e.ImpressumSetzen("  **vhs**\r\nStraße 1\r\n ");
        e.Impressum.ShouldBe("**vhs**\nStraße 1");
    }

    [Fact]
    public void Zu_langes_Impressum_wird_abgelehnt()
    {
        var e = Neu();
        Should.Throw<ArgumentException>(() => e.ImpressumSetzen(new string('x', Einrichtung.ImpressumMaxZeichen + 1)));
        e.Impressum.ShouldBeNull();
    }
}
