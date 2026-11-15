using VibeVHS.Web.Components;

namespace VibeVHS.Web.Tests;

/// <summary>Rücksprung von Detailseiten zur gefilterten Liste: Zustand in der URL, nur eigene Pfade (kein Open Redirect).</summary>
public sealed class RuecksprungTests
{
    [Theory]
    [InlineData("kurse?semester=1&q=yoga&fb=3", "kurse?semester=1&q=yoga&fb=3")]
    [InlineData("/personen?rolle=Dozierende", "personen?rolle=Dozierende")]
    [InlineData("kurse/0b1c?zurueck=kurse%3Ffb%3D3", "kurse/0b1c?zurueck=kurse%3Ffb%3D3")]
    [InlineData("anwesenheit?tag=2026-11-15&eigene=true", "anwesenheit?tag=2026-11-15&eigene=true")]
    public void Eigene_Pfade_werden_angenommen(string ziel, string erwartet) => Ruecksprung.Sicher(ziel).ShouldBe(erwartet);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://boese.example/")]
    [InlineData("//boese.example/kurse")]
    [InlineData("/\\boese.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)//kurse")]
    [InlineData("data:text/html,x")]
    [InlineData("kurse/../../etc")]
    [InlineData("\\\\server\\freigabe")]
    [InlineData("kurse\r\nSet-Cookie: x=1")]
    [InlineData("anmelden?ReturnUrl=x")]
    [InlineData("abmelden")]
    [InlineData("1kurse")]
    public void Fremde_oder_gefaehrliche_Ziele_werden_abgelehnt(string? ziel) => Ruecksprung.Sicher(ziel).ShouldBeNull();

    [Fact]
    public void Zu_lange_Ziele_werden_abgelehnt() => Ruecksprung.Sicher("kurse?q=" + new string('x', 2100)).ShouldBeNull();

    [Fact]
    public void Listen_URL_enthaelt_nur_gesetzte_Werte_und_ist_kodiert()
    {
        Ruecksprung.Url("kurse", ("semester", null), ("q", "Yoga & Pilates"), ("fb", "3"), ("archiv", "")).ShouldBe("kurse?q=Yoga%20%26%20Pilates&fb=3");
        Ruecksprung.Url("personen").ShouldBe("personen");
    }

    [Fact]
    public void Detail_Link_traegt_die_Listen_URL_kodiert_und_sie_kommt_unverändert_zurueck()
    {
        var liste = Ruecksprung.Url("personen", ("q", "müller"), ("rolle", "Dozierende"), ("fb", "5"));
        var link = Ruecksprung.Detail("personen/7f3a", liste);
        link.ShouldStartWith("personen/7f3a?zurueck=");
        link.ShouldNotContain("&rolle"); // die Listenparameter stecken kodiert im einen Parameter
        var zurueck = Uri.UnescapeDataString(link[(link.IndexOf('=') + 1)..]);
        Ruecksprung.Sicher(zurueck).ShouldBe(liste);
        Ruecksprung.Detail("belegungen/1?x=1", "kurse").ShouldBe("belegungen/1?x=1&zurueck=kurse");
    }
}
