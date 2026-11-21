using VibeVHS.Application.Betrieb;

namespace VibeVHS.Application.Tests.Betrieb;

/// <summary>Vorbereitung Mandantenfähigkeit: Auflösung aus Hostname oder Pfad, Einzelbetrieb unverändert.</summary>
public class MandantenTests
{
    [Fact]
    public void Ein_Mandant_gilt_immer()
    {
        var m = new Mandanten([new Mandant("standard", "Data Source=a.db")]);
        m.Aufloesen("irgendwo.example", "/vhs-x/personen").Kennung.ShouldBe("standard");
    }

    [Fact]
    public void Mehrere_Mandanten_nach_Host_oder_Pfad()
    {
        var m = new Mandanten([new Mandant("vhs-egs", "Data Source=egs.db", "egs.example.org"), new Mandant("vhs-ms", "Data Source=ms.db")]);
        m.Aufloesen("EGS.example.org", "/").Kennung.ShouldBe("vhs-egs");
        m.Aufloesen("server.example.org", "/vhs-ms/kurse").Kennung.ShouldBe("vhs-ms");
        m.Aufloesen("server.example.org", "/unbekannt").Kennung.ShouldBe("vhs-egs"); // Standard = erster
    }
}
