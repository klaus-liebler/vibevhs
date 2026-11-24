using Microsoft.Extensions.Time.Testing;
using VibeVHS.Application.Betrieb;

namespace VibeVHS.Application.Tests.Betrieb;

/// <summary>Spielzeit im Schulungsbetrieb (QA-68): anhalten, Tempo, nur vorwärts stellen, Weltstand bei Sprüngen.</summary>
public class SpielzeitTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 7, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Angehalten_steht_die_Uhr_mit_Tempo_laeuft_sie_schneller()
    {
        var echt = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var uhr = new Spielzeit(Start, 0, echt);
        echt.Advance(TimeSpan.FromMinutes(5));
        uhr.GetUtcNow().ShouldBe(Start);

        uhr.TempoSetzen(60);
        echt.Advance(TimeSpan.FromMinutes(1));
        uhr.GetUtcNow().ShouldBe(Start.AddHours(1));

        uhr.Anhalten();
        echt.Advance(TimeSpan.FromMinutes(10));
        uhr.GetUtcNow().ShouldBe(Start.AddHours(1));
        uhr.Weiter();
        uhr.Tempo.ShouldBe(60);
    }

    [Fact]
    public void Zurueck_nur_per_Sprung_Weltstand_steigt_und_haelt_an()
    {
        var echt = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var uhr = new Spielzeit(Start, 1, echt);
        Should.Throw<InvalidOperationException>(() => uhr.Vorstellen(Start.AddHours(-1))).Message.ShouldContain("Sprungmarke");
        uhr.Vorstellen(Start.AddDays(1));
        uhr.GetUtcNow().ShouldBe(Start.AddDays(1));

        var vorher = uhr.Weltstand;
        uhr.NeuerWeltstand(Start);
        uhr.Weltstand.ShouldBe(vorher + 1);
        uhr.Angehalten.ShouldBeTrue();
        uhr.LetztesTempo.ShouldBe(1);
    }

    [Fact]
    public void Ortszeit_stimmt_auch_wenn_der_Start_einen_Versatz_hat()
    {
        var uhr = new Spielzeit(Start, 0, zone: TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        uhr.GetUtcNow().Offset.ShouldBe(TimeSpan.Zero);
        uhr.GetLocalNow().Hour.ShouldBe(7);
        uhr.GetLocalNow().Minute.ShouldBe(30);
    }

    [Fact]
    public void Tempo_wird_begrenzt() => new Spielzeit(Start, 1000).Tempo.ShouldBe(Spielzeit.MaxTempo);

    [Fact]
    public void Bezugstag_ist_der_Montag_der_Woche()
    {
        Schulungswoche.Bezugstag(new DateOnly(2026, 11, 26)).ShouldBe(new DateOnly(2026, 11, 23)); // Donnerstag → Montag
        Schulungswoche.Bezugstag(new DateOnly(2026, 11, 29)).ShouldBe(new DateOnly(2026, 11, 23)); // Sonntag → Montag davor
        Schulungswoche.Marken.Select(m => m.Versatz).ShouldBe(Schulungswoche.Marken.Select(m => m.Versatz).Order()); // zeitlich geordnet
    }
}
