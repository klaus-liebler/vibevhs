using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

/// <summary>
/// Fallbeispiel aus dem Konzept: Dozentin Anna wird 2019 mit Tippfehler als "Schmitt" erfasst,
/// drei Tage später auf "Schmidt" korrigiert, heiratet am 14.06.2025 und heißt seitdem "Becker".
/// Das Personalbüro trägt die Heirat erst am 02.07.2025 ein.
/// </summary>
public class PersonNameHistorieTests
{
    private static readonly TimeSpan Mez = TimeSpan.FromHours(1);
    private static readonly TimeSpan Mesz = TimeSpan.FromHours(2);

    private static readonly DateTimeOffset Ersterfassung = new(2019, 3, 1, 9, 0, 0, Mez);
    private static readonly DateTimeOffset KorrekturErfasst = new(2019, 3, 4, 10, 0, 0, Mez);
    private static readonly DateTimeOffset BescheinigungGedruckt = new(2025, 5, 20, 11, 0, 0, Mesz);
    private static readonly DateTimeOffset HeiratErfasst = new(2025, 7, 2, 14, 30, 0, Mesz);
    private static readonly DateTimeOffset Heute = new(2026, 11, 15, 8, 0, 0, Mez);

    private static readonly DateOnly Eintritt = new(2019, 3, 1);
    private static readonly DateOnly Kursende = new(2025, 5, 15);
    private static readonly DateOnly Heirat = new(2025, 6, 14);

    private static Person DozentinMitGanzerGeschichte()
    {
        var anna = Person.Anlegen("Anna", "Schmitt", Eintritt, Ersterfassung);
        anna.NameKorrigieren("Anna", "Schmidt", Eintritt, "Tippfehler bei Ersterfassung", KorrekturErfasst);
        anna.NameAendernAb("Anna", "Becker", Heirat, "Heirat, Nachweis: Heiratsurkunde", HeiratErfasst);
        return anna;
    }

    [Fact]
    public void Dozentin_heiratet_Nachdruck_der_alten_Bescheinigung_zeigt_alten_Namen()
    {
        var anna = DozentinMitGanzerGeschichte();

        // Nachdruck heute: Stichtag = Kursende, Wissensstand = Zeitpunkt des Originaldrucks.
        anna.NameAm(Kursende, BescheinigungGedruckt)!.Nachname.ShouldBe("Schmidt");

        // Auch nach heutigem Wissen hieß sie am Kursende noch Schmidt: die Heirat war später.
        anna.NameAm(Kursende, Heute)!.Nachname.ShouldBe("Schmidt");
    }

    [Fact]
    public void Dozentin_heiratet_heute_gilt_der_neue_Name()
    {
        var anna = DozentinMitGanzerGeschichte();

        anna.AktuellerName(Heute).Anzeige.ShouldBe("Anna Becker");
        anna.NameAm(Heirat, Heute)!.Nachname.ShouldBe("Becker");
        anna.NameAm(Heirat.AddDays(-1), Heute)!.Nachname.ShouldBe("Schmidt");
    }

    [Fact]
    public void Heirat_die_noch_nicht_erfasst_war_ist_im_damaligen_Wissensstand_unbekannt()
    {
        var anna = DozentinMitGanzerGeschichte();
        var zwischenHeiratUndErfassung = new DateTimeOffset(2025, 6, 30, 12, 0, 0, Mesz);

        anna.NameAm(new DateOnly(2025, 6, 20), zwischenHeiratUndErfassung)!.Nachname.ShouldBe("Schmidt");
        anna.NameAm(new DateOnly(2025, 6, 20), Heute)!.Nachname.ShouldBe("Becker");
    }

    [Fact]
    public void Korrektur_gilt_rueckwirkend_der_falsche_Wert_bleibt_aber_nachvollziehbar()
    {
        var anna = DozentinMitGanzerGeschichte();
        var amTagDerErsterfassung = new DateTimeOffset(2019, 3, 2, 12, 0, 0, Mez);

        // Damals wusste das System nur "Schmitt".
        anna.NameAm(Eintritt, amTagDerErsterfassung)!.Nachname.ShouldBe("Schmitt");

        // Nach heutigem Wissen hieß sie schon immer "Schmidt": Korrektur wirkt auf den ganzen Zeitraum.
        anna.NameAm(Eintritt, Heute)!.Nachname.ShouldBe("Schmidt");
        anna.NameAm(new DateOnly(2024, 1, 1), Heute)!.Nachname.ShouldBe("Schmidt");
    }

    [Fact]
    public void Korrektur_und_Aenderung_ab_Datum_unterscheiden_sich_in_der_Vergangenheit()
    {
        var korrigiert = Person.Anlegen("Anna", "Schmidt", Eintritt, Ersterfassung);
        korrigiert.NameKorrigieren("Anna", "Becker", Eintritt, "War schon immer falsch", HeiratErfasst);

        var geaendert = Person.Anlegen("Anna", "Schmidt", Eintritt, Ersterfassung);
        geaendert.NameAendernAb("Anna", "Becker", Heirat, "Heirat", HeiratErfasst);

        // Heute gilt bei beiden "Becker" ...
        korrigiert.AktuellerName(Heute).Nachname.ShouldBe("Becker");
        geaendert.AktuellerName(Heute).Nachname.ShouldBe("Becker");

        // ... aber für 2020 sagt nur die Änderung ab Datum weiterhin "Schmidt".
        var tag2020 = new DateOnly(2020, 1, 1);
        korrigiert.NameAm(tag2020, Heute)!.Nachname.ShouldBe("Becker");
        geaendert.NameAm(tag2020, Heute)!.Nachname.ShouldBe("Schmidt");
    }

    [Fact]
    public void Historie_entspricht_dem_Beispiel_aus_dem_Konzept()
    {
        var anna = DozentinMitGanzerGeschichte();

        anna.Namen.Count.ShouldBe(4);
        anna.Namen.Count(n => n.IstAktuellerWissensstand).ShouldBe(2);

        var verlauf = anna.Namen.Where(n => n.IstAktuellerWissensstand).OrderBy(n => n.GueltigVon).ToList();
        verlauf[0].Nachname.ShouldBe("Schmidt");
        verlauf[0].GueltigVon.ShouldBe(Eintritt);
        verlauf[0].GueltigBis.ShouldBe(Heirat);
        verlauf[1].Nachname.ShouldBe("Becker");
        verlauf[1].GueltigVon.ShouldBe(Heirat);
        verlauf[1].GueltigBis.ShouldBeNull();

        anna.Namen.ShouldAllBe(n => !string.IsNullOrWhiteSpace(n.Grund));
    }

    [Fact]
    public void Gueltigkeit_ist_halboffen_der_Endtag_gehoert_nicht_mehr_dazu()
    {
        var anna = DozentinMitGanzerGeschichte();
        var schmidt = anna.Namen.Single(n => n.IstAktuellerWissensstand && n.Nachname == "Schmidt");

        schmidt.GiltAm(Heirat.AddDays(-1)).ShouldBeTrue();
        schmidt.GiltAm(Heirat).ShouldBeFalse();
    }

    [Fact]
    public void Vor_der_Ersterfassung_ist_kein_Name_bekannt()
    {
        var anna = DozentinMitGanzerGeschichte();

        anna.NameAm(Eintritt, Ersterfassung.AddMinutes(-1)).ShouldBeNull();
        anna.NameAm(Eintritt.AddDays(-1), Heute).ShouldBeNull();
    }

    [Fact]
    public void Erneute_Aenderung_mit_frueherem_Datum_ersetzt_eine_geplante_Zukunftsaenderung()
    {
        var person = Person.Anlegen("Lena", "Wolf", Eintritt, Ersterfassung);
        person.NameAendernAb("Lena", "Fuchs", new DateOnly(2027, 1, 1), "Heirat geplant", Heute);
        person.NameAendernAb("Lena", "Krause", new DateOnly(2026, 12, 1), "Heirat vorgezogen, anderer Name", Heute.AddHours(1));

        var spaeter = Heute.AddHours(2);
        person.NameAm(new DateOnly(2026, 11, 30), spaeter)!.Nachname.ShouldBe("Wolf");
        person.NameAm(new DateOnly(2027, 6, 1), spaeter)!.Nachname.ShouldBe("Krause");
        person.Namen.Where(n => n.IstAktuellerWissensstand).ShouldNotContain(n => n.Nachname == "Fuchs");
    }

    [Fact]
    public void Korrektur_ohne_gueltigen_Wert_am_Stichtag_wird_abgelehnt()
    {
        var person = Person.Anlegen("Lena", "Wolf", Eintritt, Ersterfassung);

        Should.Throw<InvalidOperationException>(() =>
            person.NameKorrigieren("Lena", "Wolff", Eintritt.AddDays(-10), "Tippfehler", Heute));
    }

    [Fact]
    public void Gueltig_bis_vor_gueltig_von_wird_abgelehnt()
    {
        Should.Throw<ArgumentException>(() =>
            new PersonName("Lena", "Wolf", Eintritt, Eintritt, Heute, "Test"));
    }

    [Fact]
    public void Ohne_Grund_wird_keine_Version_angelegt()
    {
        var person = Person.Anlegen("Lena", "Wolf", Eintritt, Ersterfassung);

        Should.Throw<ArgumentException>(() => person.NameAendernAb("Lena", "Fuchs", Heirat, " ", Heute));
    }
}
