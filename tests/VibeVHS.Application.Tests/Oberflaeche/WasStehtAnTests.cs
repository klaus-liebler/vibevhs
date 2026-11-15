using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Stammdaten;

namespace VibeVHS.Application.Tests.Oberflaeche;

public class WasStehtAnTests
{
    private static readonly IReadOnlyList<Fachbereich> Alle = Enumerable.Range(1, 8)
        .Select(nr => new Fachbereich(nr, $"Bereich {nr}"))
        .ToList();

    private static readonly WasStehtAn.Lage Ruhig = new(0);

    [Fact]
    public void Fachbereichsleitung_sieht_nur_ihre_eigenen_Fachbereiche()
    {
        var schatz = new Benutzer("varvara.schatz", "Varvara Schatz", Rolle.Fachbereichsleitung,
            [Rolle.Fachbereichsleitung], [2, 3, 6, 7]);

        var schritte = WasStehtAn.Fuer(schatz, Alle, Ruhig);

        var fbTitel = schritte.Where(s => s.Titel.StartsWith("FB ", StringComparison.Ordinal)).Select(s => s.Titel).ToList();
        fbTitel.Count.ShouldBe(4);
        fbTitel.ShouldAllBe(t => t.StartsWith("FB 2 ") || t.StartsWith("FB 3 ") || t.StartsWith("FB 6 ") || t.StartsWith("FB 7 "));
        fbTitel[0].ShouldStartWith("FB 2 ");
    }

    [Fact]
    public void Gleiche_Person_in_anderer_Rolle_sieht_keine_Fachbereichseintraege()
    {
        var steinhorst = new Benutzer("heike.steinhorst", "Dr. Heike Steinhorst", Rolle.IntegrationskursTeam,
            [Rolle.Fachbereichsleitung, Rolle.IntegrationskursTeam], [4, 5]);

        var schritte = WasStehtAn.Fuer(steinhorst, Alle, Ruhig);

        schritte.ShouldNotContain(s => s.Titel.StartsWith("FB ", StringComparison.Ordinal));
        schritte.ShouldContain(s => s.Titel == "BAMF-Meldungen prüfen");
    }

    [Fact]
    public void Fachbereichsleitung_ohne_Zuordnung_bekommt_erklaerenden_Hinweis()
    {
        var ohne = new Benutzer("x", "X", Rolle.Fachbereichsleitung, [Rolle.Fachbereichsleitung], []);

        var schritte = WasStehtAn.Fuer(ohne, Alle, Ruhig);

        var hinweis = schritte.Single(s => s.Titel == "Fachbereiche zuordnen lassen");
        hinweis.Status.Erlaubt.ShouldBeFalse();
        hinweis.Status.Begruendung!.ShouldContain("kein Fachbereich");
    }

    [Fact]
    public void Fehlgeschlagene_Nachrichten_stehen_fuer_jede_Rolle_ganz_oben()
    {
        foreach (var rolle in Enum.GetValues<Rolle>())
        {
            var b = new Benutzer("x", "X", rolle, [rolle], [1]);
            var schritte = WasStehtAn.Fuer(b, Alle, new WasStehtAn.Lage(2));
            schritte[0].Titel.ShouldStartWith("2 fehlgeschlagene");
            schritte.Count(s => s.Ziel == "postausgang").ShouldBeLessThanOrEqualTo(1);
        }
    }
}
