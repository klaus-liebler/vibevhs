using VibeVHS.Infrastructure.Hintergrund;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Spielzeitplaner (QA-68): welche Läufe fällig sind, wenn die Spielzeit von → bis springt – je Lauf einmal.</summary>
public class ZeitplanTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static DateTimeOffset Ortszeit(int tag, int stunde, int minute = 0) => new(2026, 11, tag, stunde, minute, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Eine_Stunde_am_Morgen_startet_stuendliche_und_morgendliche_Laeufe()
    {
        var namen = Zeitplan.Faellig(Ortszeit(24, 6, 0), Ortszeit(24, 7, 30), Berlin).Select(e => e.Name).ToList();
        namen.ShouldContain("anmeldung-stuendlich");
        namen.ShouldContain("aufgaben-stuendlich");
        namen.ShouldContain("finanzen-morgens");      // 6:30
        namen.ShouldContain("erinnerung-morgens");    // 7:00
        namen.ShouldNotContain("termine-nacht");
    }

    [Fact]
    public void Eine_Woche_Zeitraffer_startet_jeden_Lauf_nur_einmal()
    {
        var faellig = Zeitplan.Faellig(Ortszeit(23, 8), Ortszeit(30, 8), Berlin);
        faellig.Select(e => e.Name).ShouldBeUnique();
        faellig.ShouldContain(e => e.Name == "schluessel-nachts");
        faellig.ShouldNotContain(e => e.Name == "loeschlauf-monatlich"); // erst am 1. des Monats
    }

    [Fact]
    public void Ohne_Fortschritt_ist_nichts_faellig() => Zeitplan.Faellig(Ortszeit(24, 9), Ortszeit(24, 9), Berlin).ShouldBeEmpty();
}
