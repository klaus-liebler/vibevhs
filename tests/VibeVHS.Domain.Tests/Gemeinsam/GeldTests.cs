using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Tests.Gemeinsam;

public class GeldTests
{
    [Fact]
    public void Euro_wird_in_Cent_gespeichert()
    {
        Geld.Euro(98).Cent.ShouldBe(9800);
        Geld.Euro(12, 5).Cent.ShouldBe(1205);
        Geld.Euro(-3, 50).Cent.ShouldBe(-350);
    }

    [Fact]
    public void Anzeige_ist_deutsch_formatiert()
    {
        Geld.Euro(1234, 5).ToString().ShouldBe("1.234,05 €");
    }

    [Fact]
    public void Rechnen_bleibt_exakt()
    {
        (Geld.Euro(0, 10) + Geld.Euro(0, 20)).ShouldBe(Geld.Euro(0, 30));
        (Geld.Euro(98) * 3 - Geld.Euro(4)).ShouldBe(Geld.Euro(290));
    }

    [Fact]
    public void Aufteilen_verliert_keinen_Cent()
    {
        var raten = Geld.Euro(100).Aufteilen(3);

        raten.Select(r => r.Cent).ShouldBe([3334, 3333, 3333]);
        raten.Aggregate(Geld.Null, (a, b) => a + b).ShouldBe(Geld.Euro(100));
    }

    [Fact]
    public void Ungueltige_Cent_werden_abgelehnt()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Geld.Euro(1, 100));
    }
}
