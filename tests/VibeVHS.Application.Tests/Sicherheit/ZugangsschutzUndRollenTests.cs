using Microsoft.Extensions.Options;
using VibeVHS.Adapters.Mocks;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Application.Tests.Sicherheit;

public class ZugangsschutzTests
{
    private static Zugangsschutz Mit(TestanmeldungOptionen o) => new(Options.Create(o));

    [Fact]
    public void Ohne_Konfiguration_ist_kein_Zugangscode_noetig()
    {
        var schutz = Mit(new TestanmeldungOptionen());

        schutz.ZugangscodeNoetig.ShouldBeFalse();
        schutz.ZugangscodePruefen(null).ShouldBeTrue();
    }

    [Fact]
    public void Erforderlicher_aber_fehlender_Zugangscode_sperrt_die_Anmeldung()
    {
        var schutz = Mit(new TestanmeldungOptionen { ZugangscodeErforderlich = true });

        schutz.AnmeldungEingerichtet.ShouldBeFalse();
        schutz.ZugangscodePruefen("irgendwas").ShouldBeFalse();
    }

    [Fact]
    public void Zugangscode_wird_genau_geprueft()
    {
        var schutz = Mit(new TestanmeldungOptionen { Zugangscode = "Geheim-123" });

        schutz.ZugangscodePruefen("Geheim-123").ShouldBeTrue();
        schutz.ZugangscodePruefen("geheim-123").ShouldBeFalse();
        schutz.ZugangscodePruefen("").ShouldBeFalse();
    }
}

public class RollenTests
{
    private static Benutzerkonto Steinhorst() => new("heike.steinhorst", "Dr. Heike Steinhorst", "Fachbereichsleitung",
        [Rolle.Fachbereichsleitung, Rolle.IntegrationskursTeam], [5, 4], Guid.NewGuid());

    [Theory]
    [InlineData("Integrationskurs-Team", Rolle.IntegrationskursTeam)]
    [InlineData("Leitung", Rolle.Leitung)]
    [InlineData("verwaltungsleitung", Rolle.Verwaltungsleitung)]
    [InlineData("Systemverwaltung", Rolle.Systemverwaltung)]
    [InlineData("IntegrationskursTeam", Rolle.IntegrationskursTeam)]
    public void Rollen_werden_aus_Anzeigenamen_gelesen(string text, Rolle erwartet)
    {
        RollenExtensions.TryParse(text, out var rolle).ShouldBeTrue();
        rolle.ShouldBe(erwartet);
    }

    [Fact]
    public void Unbekannte_Rolle_wird_nicht_erkannt()
    {
        RollenExtensions.TryParse("Hausmeister", out _).ShouldBeFalse();
    }

    [Fact]
    public void Nach_der_Anmeldung_ist_die_erste_Rolle_aktiv_und_alle_bleiben_verfuegbar()
    {
        var principal = MockAnmeldung.Anmelden(Steinhorst().AlsBenutzer(), "Test");

        var benutzer = MockAnmeldung.AlsBenutzer(principal)!;
        benutzer.Rolle.ShouldBe(Rolle.Fachbereichsleitung);
        benutzer.Rollen.ShouldBe([Rolle.Fachbereichsleitung, Rolle.IntegrationskursTeam]);
        benutzer.ZustaendigeFachbereiche.ShouldBe([4, 5]);
        benutzer.Name.ShouldBe("Dr. Heike Steinhorst");
    }

    [Fact]
    public void Rollenwechsel_nur_zu_eigenen_Rollen()
    {
        var principal = MockAnmeldung.Anmelden(Steinhorst().AlsBenutzer(), "Test");

        var gewechselt = MockAnmeldung.RolleWechseln(principal, Rolle.IntegrationskursTeam, "Test");
        MockAnmeldung.AlsBenutzer(gewechselt)!.Rolle.ShouldBe(Rolle.IntegrationskursTeam);
        MockAnmeldung.AlsBenutzer(gewechselt)!.ZustaendigeFachbereiche.ShouldBe([4, 5]);

        MockAnmeldung.RolleWechseln(principal, Rolle.Buchhaltung, "Test").ShouldBeNull();
    }

    [Fact]
    public void Konto_ohne_Rolle_ist_unzulaessig()
    {
        Should.Throw<ArgumentException>(() => new Benutzerkonto("x", "X Y", null, [], [], null));
    }
}
