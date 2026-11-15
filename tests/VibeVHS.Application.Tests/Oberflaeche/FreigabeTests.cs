using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Application.Tests.Oberflaeche;

public class FreigabeTests
{
    [Fact]
    public void Erlaubte_Rolle_bekommt_Freigabe_ohne_Begruendung()
    {
        var f = Freigabe.NurFuer(Rolle.Fachbereichsleitung, Rolle.Fachbereichsleitung);

        f.Erlaubt.ShouldBeTrue();
        f.Begruendung.ShouldBeNull();
    }

    [Fact]
    public void Gesperrte_Aktion_erklaert_benoetigte_und_eigene_Rolle()
    {
        var f = Freigabe.NurFuer(Rolle.Teilnehmerservice, Rolle.Fachbereichsleitung, Rolle.IntegrationskursTeam);

        f.Erlaubt.ShouldBeFalse();
        f.Begruendung.ShouldBe("Nur für die Rolle „Fachbereichsleitung“ oder „Integrationskurs-Team“. Ihre Rolle: Teilnehmerservice.");
    }

    [Fact]
    public void Ohne_Begruendung_darf_nichts_gesperrt_werden()
    {
        Should.Throw<ArgumentException>(() => Freigabe.Nein(""));
    }

    [Fact]
    public void Und_liefert_die_erste_nicht_erfuellte_Bedingung()
    {
        var f = Freigabe.NurFuer(Rolle.Buchhaltung, Rolle.Buchhaltung)
            .Und(() => Freigabe.Nein("Der Kurs ist bereits abgerechnet."))
            .Und(() => Freigabe.Nein("Wird nicht mehr erreicht."));

        f.Begruendung.ShouldBe("Der Kurs ist bereits abgerechnet.");
    }

    [Fact]
    public void Naechster_Schritt_ist_ohne_Angabe_freigegeben()
    {
        new NaechsterSchritt("Postausgang ansehen", "/postausgang").Status.Erlaubt.ShouldBeTrue();
    }
}
