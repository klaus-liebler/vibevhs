using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

public class BeziehungenTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 10, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Heute = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Vollmacht_gilt_nur_im_Zeitraum_und_ab_Erfassung()
    {
        var kind = Person.Anlegen("Lena", "Wolf", new DateOnly(2026, 1, 1), Start, new DateOnly(2012, 5, 1));
        var mutter = Person.Anlegen("Sabine", "Wolf", new DateOnly(2026, 1, 1), Start);
        kind.VollmachtErteilen(Bevollmaechtigtenart.Person, mutter.Id, "Sabine Wolf", "Sorgerecht (Geburtsurkunde)",
            new DateOnly(2026, 2, 1), new DateOnly(2026, 8, 1), Erfassung.Aenderung(Start, "Vollmacht"));

        kind.DarfBuchen(mutter.Id, new DateOnly(2026, 1, 31), Heute).ShouldBeFalse();
        kind.DarfBuchen(mutter.Id, new DateOnly(2026, 2, 1), Heute).ShouldBeTrue();
        kind.DarfBuchen(mutter.Id, new DateOnly(2026, 7, 31), Heute).ShouldBeTrue();
        kind.DarfBuchen(mutter.Id, new DateOnly(2026, 8, 1), Heute).ShouldBeFalse();
        kind.DarfBuchen(mutter.Id, new DateOnly(2026, 3, 1), Start.AddMinutes(-1)).ShouldBeFalse();
        kind.DarfBuchen(kind.Id, new DateOnly(2026, 3, 1), Heute).ShouldBeTrue();
    }

    [Fact]
    public void Widerrufene_Vollmacht_gilt_ab_Widerruf_nicht_mehr_bleibt_aber_in_der_Historie()
    {
        var p = Person.Anlegen("Ahmad", "Rahimi", new DateOnly(2026, 1, 1), Start);
        var jobcenter = new Organisation("Jobcenter Beispielkreis", Organisationsart.Jobcenter);
        var id = p.VollmachtErteilen(Bevollmaechtigtenart.Organisation, jobcenter.Id, jobcenter.Name, "Kostenübernahme",
            new DateOnly(2026, 1, 1), null, Erfassung.Aenderung(Start, "Vollmacht"));
        p.VollmachtWiderrufenAb(id, new DateOnly(2026, 6, 1), Erfassung.Ende(Heute, "Maßnahme beendet"));

        var spaeter = Heute.AddMinutes(1);
        p.DarfBuchen(jobcenter.Id, new DateOnly(2026, 5, 31), spaeter).ShouldBeTrue();
        p.DarfBuchen(jobcenter.Id, new DateOnly(2026, 6, 1), spaeter).ShouldBeFalse();
        p.Vollmachten.Count.ShouldBeGreaterThanOrEqualTo(3);
        p.StandAm(new DateOnly(2026, 3, 1), spaeter).Vollmachten.ShouldHaveSingleItem().Nachweis.ShouldBe("Kostenübernahme");
    }

    [Fact]
    public void Einwilligung_ist_je_Kanal_und_Zweck_zeitlich_versioniert()
    {
        var p = Person.Anlegen("Mehmet", "Yılmaz", new DateOnly(2026, 1, 1), Start);
        p.EinwilligungSetzenAb(Kontaktkanal.Sms, Kontaktzweck.Kursorganisation, true, new DateOnly(2026, 1, 1), Erfassung.Erst(Start));
        p.EinwilligungSetzenAb(Kontaktkanal.EMail, Kontaktzweck.Werbung, false, new DateOnly(2026, 1, 1), Erfassung.Erst(Start));
        p.EinwilligungSetzenAb(Kontaktkanal.EMail, Kontaktzweck.Werbung, true, new DateOnly(2026, 9, 1), Erfassung.Aenderung(Heute, "Formular"));

        var spaeter = Heute.AddMinutes(1);
        p.EinwilligungAm(Kontaktkanal.Sms, Kontaktzweck.Kursorganisation, new DateOnly(2026, 5, 1), spaeter).ShouldBeTrue();
        p.EinwilligungAm(Kontaktkanal.Sms, Kontaktzweck.Werbung, new DateOnly(2026, 5, 1), spaeter).ShouldBeFalse();
        p.EinwilligungAm(Kontaktkanal.EMail, Kontaktzweck.Werbung, new DateOnly(2026, 8, 31), spaeter).ShouldBeFalse();
        p.EinwilligungAm(Kontaktkanal.EMail, Kontaktzweck.Werbung, new DateOnly(2026, 9, 1), spaeter).ShouldBeTrue();
        p.StandAm(new DateOnly(2026, 10, 1), spaeter).Erlaubt(Kontaktkanal.EMail, Kontaktzweck.Werbung).ShouldBeTrue();
    }

    [Fact]
    public void Organisationszugehoerigkeit_ist_zeitlich_gueltig()
    {
        var p = Person.Anlegen("Jonas", "Hoffmann", new DateOnly(2026, 1, 1), Start);
        var stadtwerke = new Organisation("Stadtwerke Beispielstadt", Organisationsart.Firma);
        p.OrganisationZuordnenAb(stadtwerke, "Mitarbeitende", new DateOnly(2026, 1, 1), Erfassung.Erst(Start));
        p.OrganisationVerlassenAb(stadtwerke.Id, new DateOnly(2026, 10, 1), Erfassung.Ende(Heute, "Arbeitgeberwechsel"));

        var spaeter = Heute.AddMinutes(1);
        p.StandAm(new DateOnly(2026, 9, 30), spaeter).Organisationen.ShouldHaveSingleItem().OrganisationName.ShouldBe("Stadtwerke Beispielstadt");
        p.StandAm(new DateOnly(2026, 10, 1), spaeter).Organisationen.ShouldBeEmpty();
    }

    [Fact]
    public void Zusammenfuehren_uebernimmt_fehlende_Daten_und_behaelt_beide_Historien()
    {
        var ziel = Person.Anlegen("Mehmet", "Yılmaz", new DateOnly(2025, 1, 1), Start, new DateOnly(1988, 5, 3), "mehmet@example.org", "tr");
        var dublette = Person.Anlegen("Mehmet", "Yilmaz", new DateOnly(2026, 3, 1), Start.AddDays(60), new DateOnly(1988, 5, 3), "m.yilmaz@example.org");
        var adresse = dublette.AdresseHinzufuegen(null, "Lindenstraße 3", "48282", "Emsdetten", new DateOnly(2026, 3, 1), Erfassung.Erst(Start.AddDays(60)));
        dublette.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, adresse, new DateOnly(2026, 3, 1), Erfassung.Erst(Start.AddDays(60)));
        dublette.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2026, 3, 1), Erfassung.Erst(Start.AddDays(60)));
        var dublettenVersionen = dublette.AlleVersionen.Count();

        ziel.DubletteUebernehmen(dublette, Erfassung.Aenderung(Heute, "Dublette zusammengeführt", "J. Krüger"));

        var stand = ziel.StandAm(DateOnly.FromDateTime(Heute.Date), Heute);
        stand.EMails.Select(e => e.Adresse).ShouldBe(["mehmet@example.org", "m.yilmaz@example.org"], ignoreOrder: true);
        stand.Aufenthaltsort!.Strasse.ShouldBe("Lindenstraße 3");
        stand.Rollen.HasFlag(Personenrolle.Teilnehmende).ShouldBeTrue();
        ziel.Wohnort.ShouldBe("Emsdetten");

        dublette.Status.ShouldBe(Personenstatus.Zusammengefuehrt);
        dublette.ZusammengefuehrtIn.ShouldBe(ziel.Id);
        dublette.AlleVersionen.Count().ShouldBe(dublettenVersionen);
        Should.Throw<InvalidOperationException>(() => ziel.DubletteUebernehmen(dublette, Erfassung.Aenderung(Heute, "nochmal")));
    }

    [Fact]
    public void Suchtext_enthaelt_auch_fruehere_Namen()
    {
        var p = Person.Anlegen("Anna", "Schmidt", new DateOnly(2019, 3, 1), Start, new DateOnly(1986, 8, 12));
        p.NameAendernAb("Anna", "Becker", new DateOnly(2025, 6, 14), "Heirat", Heute);

        var text = p.SuchtextFuerIndex();
        text.ShouldContain("schmidt");
        text.ShouldContain("becker");
        text.ShouldContain("12.08.1986");
        p.Anzeigename.ShouldBe("Anna Becker");
        p.Sortiername.ShouldBe("becker anna");
    }
}
