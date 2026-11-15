using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

/// <summary>
/// Fallbeispiel: Olena wohnt ab 01.03.2025 in der Lindenstraße. Ab 01.06.2026 soll Post an die Flüchtlingsberatung
/// gehen (c/o, erfasst 03.06.). Zum 01.08.2026 zieht sie in den Grevener Damm (erfasst erst am 10.08.).
/// </summary>
public class KontaktdatenTests
{
    private static readonly TimeSpan Mesz = TimeSpan.FromHours(2);
    private static readonly DateTimeOffset Start = new(2025, 3, 1, 10, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset CoErfasst = new(2026, 6, 3, 11, 0, 0, Mesz);
    private static readonly DateTimeOffset UmzugErfasst = new(2026, 8, 10, 9, 0, 0, Mesz);
    private static readonly DateTimeOffset Heute = new(2026, 11, 15, 8, 0, 0, TimeSpan.FromHours(1));

    private static (Person Person, Guid Wohnung1, Guid Co, Guid Wohnung2) Olena()
    {
        var p = Person.Anlegen("Olena", "Kovalenko", new DateOnly(2025, 3, 1), Start, new DateOnly(1991, 1, 21), null, "uk");
        var w1 = p.AdresseHinzufuegen(null, "Lindenstraße 14", "48282", "Emsdetten", new DateOnly(2025, 3, 1), Erfassung.Erst(Start));
        p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, w1, new DateOnly(2025, 3, 1), Erfassung.Erst(Start));
        var co = p.AdresseHinzufuegen("c/o Flüchtlingsberatung", "Kirchplatz 2", "48268", "Greven", new DateOnly(2026, 6, 1),
            Erfassung.Aenderung(CoErfasst, "Wunsch der Teilnehmerin"));
        p.AdressKennzeichnenAb(AdressKennzeichenArt.Korrespondenzadresse, co, new DateOnly(2026, 6, 1),
            Erfassung.Aenderung(CoErfasst, "Wunsch der Teilnehmerin"));
        var w2 = p.UmzugAb(w1, null, "Grevener Damm 120", "48282", "Emsdetten", new DateOnly(2026, 8, 1),
            Erfassung.Aenderung(UmzugErfasst, "Meldebescheinigung"));
        return (p, w1, co, w2);
    }

    [Fact]
    public void Stichtag_vor_dem_Umzug_liefert_die_alte_Adresse()
    {
        var (p, w1, _, w2) = Olena();

        p.StandAm(new DateOnly(2026, 7, 15), Heute).Aufenthaltsort!.AdressId.ShouldBe(w1);
        p.StandAm(new DateOnly(2026, 8, 15), Heute).Aufenthaltsort!.AdressId.ShouldBe(w2);
        p.StandAm(new DateOnly(2026, 8, 15), Heute).Aufenthaltsort!.Strasse.ShouldBe("Grevener Damm 120");
    }

    [Fact]
    public void Vor_der_Erfassung_des_Umzugs_war_nur_die_alte_Adresse_bekannt()
    {
        var (p, w1, _, _) = Olena();
        var amAchtenAugust = new DateTimeOffset(2026, 8, 8, 12, 0, 0, Mesz);

        var stand = p.StandAm(new DateOnly(2026, 8, 5), amAchtenAugust);

        stand.Aufenthaltsort!.AdressId.ShouldBe(w1);
        stand.Adressen.Count.ShouldBe(2); // Lindenstraße und c/o, der Grevener Damm war noch unbekannt
    }

    [Fact]
    public void Alte_Adresse_ist_nach_dem_Umzug_aufgegeben()
    {
        var (p, w1, _, _) = Olena();

        p.StandAm(new DateOnly(2026, 9, 1), Heute).Adressen.ShouldNotContain(a => a.AdressId == w1);
        p.StandAm(new DateOnly(2026, 7, 31), Heute).Adressen.ShouldContain(a => a.AdressId == w1);
    }

    [Fact]
    public void Korrespondenzadresse_wechselt_unabhaengig_vom_Aufenthaltsort()
    {
        var (p, w1, co, w2) = Olena();

        // Vor Juni: keine eigene Korrespondenzadresse, Briefe gehen an den Aufenthaltsort.
        var mai = p.StandAm(new DateOnly(2026, 5, 15), Heute);
        mai.Korrespondenzadresse.ShouldBeNull();
        mai.BriefAn!.AdressId.ShouldBe(w1);

        // Juni/Juli: Post an c/o, Wohnung unverändert.
        var juli = p.StandAm(new DateOnly(2026, 7, 1), Heute);
        juli.Korrespondenzadresse!.AdressId.ShouldBe(co);
        juli.Aufenthaltsort!.AdressId.ShouldBe(w1);

        // Nach dem Umzug: neue Wohnung, Korrespondenz bleibt c/o.
        var september = p.StandAm(new DateOnly(2026, 9, 1), Heute);
        september.Aufenthaltsort!.AdressId.ShouldBe(w2);
        september.Korrespondenzadresse!.AdressId.ShouldBe(co);
        september.BriefAn!.Zusatz.ShouldBe("c/o Flüchtlingsberatung");
    }

    [Fact]
    public void Adresskorrektur_gilt_rueckwirkend_fuer_den_ganzen_Zeitraum()
    {
        var (p, w1, _, _) = Olena();
        p.AdresseKorrigieren(w1, null, "Lindenstraße 41", "48282", "Emsdetten", new DateOnly(2025, 3, 1),
            Erfassung.Korrektur(Heute, "Zahlendreher"));

        var spaeter = Heute.AddMinutes(1);
        p.StandAm(new DateOnly(2025, 6, 1), spaeter).Aufenthaltsort!.Strasse.ShouldBe("Lindenstraße 41");
        p.StandAm(new DateOnly(2025, 6, 1), Heute.AddMinutes(-1)).Aufenthaltsort!.Strasse.ShouldBe("Lindenstraße 14");
    }

    [Fact]
    public void Messagingdienst_kommt_ab_Datum_hinzu_und_abgemeldete_Nummer_entfaellt()
    {
        var p = Person.Anlegen("Mehmet", "Yılmaz", new DateOnly(2025, 1, 1), Start);
        var nr = p.RufnummerHinzufuegen(RufnummerArt.Mobil, "+49 151 1", Messagingdienst.Anruf | Messagingdienst.Sms, true, true,
            new DateOnly(2025, 1, 1), Erfassung.Erst(Start));
        p.RufnummerAendernAb(nr, RufnummerArt.Mobil, "+49 151 1", Messagingdienst.Anruf | Messagingdienst.Sms | Messagingdienst.Signal, true, true,
            new DateOnly(2026, 9, 1), Erfassung.Aenderung(Heute, "Einwilligung Signal"));
        p.RufnummerAbmeldenAb(nr, new DateOnly(2026, 12, 1), Erfassung.Ende(Heute.AddMinutes(1), "Nummer gekündigt"));

        var bekannt = Heute.AddMinutes(2);
        p.StandAm(new DateOnly(2026, 8, 1), bekannt).Rufnummern.Single().Dienste.HasFlag(Messagingdienst.Signal).ShouldBeFalse();
        p.StandAm(new DateOnly(2026, 10, 1), bekannt).Rufnummern.Single().Dienste.HasFlag(Messagingdienst.Signal).ShouldBeTrue();
        p.StandAm(new DateOnly(2026, 12, 2), bekannt).Rufnummern.ShouldBeEmpty();
    }

    [Fact]
    public void Geburtsdatum_wird_korrigiert_und_der_Zwischenstand_nachgefuehrt()
    {
        var p = Person.Anlegen("Nadia", "Haddad", new DateOnly(2025, 1, 1), Start, new DateOnly(1993, 10, 9));

        p.GeburtsdatumKorrigieren(new DateOnly(1993, 9, 10), Erfassung.Korrektur(Heute, "Tag und Monat vertauscht; Nachweis: Pass"));

        p.Geburtsdatum.ShouldBe(new DateOnly(1993, 9, 10));
        p.StandAm(new DateOnly(2025, 6, 1), Start.AddDays(1)).Geburtsdatum.ShouldBe(new DateOnly(1993, 10, 9));
        p.StandAm(new DateOnly(2025, 6, 1), Heute).Geburtsdatum.ShouldBe(new DateOnly(1993, 9, 10));
    }

    [Fact]
    public void Rollen_kommen_ab_Datum_hinzu_ohne_alte_zu_verlieren()
    {
        var p = Person.Anlegen("Anna", "Becker", new DateOnly(2019, 3, 1), Start);
        p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2019, 3, 1), Erfassung.Erst(Start));
        p.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(2024, 1, 1), Erfassung.Aenderung(Heute, "Honorarvertrag"));

        p.StandAm(new DateOnly(2023, 1, 1), Heute).Rollen.ShouldBe(Personenrolle.Teilnehmende);
        p.StandAm(new DateOnly(2025, 1, 1), Heute).Rollen.ShouldBe(Personenrolle.Teilnehmende | Personenrolle.Dozierende);
        p.Rollen.ShouldBe(Personenrolle.Teilnehmende | Personenrolle.Dozierende);
    }
}
