using Microsoft.Extensions.Time.Testing;
using VibeVHS.Application.Betrieb;

namespace VibeVHS.Application.Tests.Oberflaeche;

/// <summary>Startabgleich nach dem Serverstart: Die Startseite weist darauf hin, bis alle Schritte durch sind, und wird bei jedem Schritt benachrichtigt.</summary>
public sealed class StartabgleichTests
{
    private static Startabgleich Neu(FakeTimeProvider uhr) => new(uhr, [("a", "Qualifikationen"), ("b", "Aufgabenregeln und Eskalation")]);

    [Fact]
    public void Laeuft_bis_alle_Schritte_beendet_sind_und_meldet_jede_Aenderung()
    {
        var uhr = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-15T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var abgleich = Neu(uhr);
        var meldungen = 0;
        abgleich.Geaendert += () => meldungen++;
        abgleich.Laeuft.ShouldBeTrue(); // schon vor dem ersten Schritt: der Server hat gerade erst gestartet

        abgleich.Begonnen("a");
        abgleich.Schritte[0].Zustand.ShouldBe(Schrittzustand.Laeuft);
        abgleich.Beendet("a", true);
        abgleich.Begonnen("b");
        abgleich.Laeuft.ShouldBeTrue();
        abgleich.Beendet("b", false);

        abgleich.Laeuft.ShouldBeFalse();
        abgleich.Schritte.Select(s => s.Zustand).ShouldBe([Schrittzustand.Fertig, Schrittzustand.Fehler]);
        meldungen.ShouldBe(4);
        abgleich.Beendet("unbekannt", true); // fremde Schlüssel ändern nichts
        meldungen.ShouldBe(4);
    }

    [Fact]
    public void Ohne_Hintergrunddienst_endet_der_Hinweis_nach_der_Hoechstdauer()
    {
        var uhr = new FakeTimeProvider();
        var abgleich = Neu(uhr);
        abgleich.Laeuft.ShouldBeTrue();
        uhr.Advance(Startabgleich.Hoechstdauer);
        abgleich.Laeuft.ShouldBeFalse();
    }
}
