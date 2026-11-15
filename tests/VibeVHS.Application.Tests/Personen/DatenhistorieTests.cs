using VibeVHS.Application.Personen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Tests.Personen;

public class DatenhistorieTests
{
    private static readonly DateTimeOffset T0 = new(2025, 3, 1, 10, 0, 0, TimeSpan.FromHours(1));

    private static DateTimeOffset T(int tage) => T0.AddDays(tage);

    /// <summary>Je Datenart eine Änderung, jeweils einen Tag später erfasst.</summary>
    private static Person PersonMitAllenAenderungen()
    {
        var p = Person.Anlegen("Olena", "Kovalenko", new DateOnly(2025, 3, 1), T0, new DateOnly(1991, 1, 21), "olena@example.org", "uk", "Teilnehmerservice");
        var w1 = p.AdresseHinzufuegen(null, "Lindenstraße 14", "48282", "Emsdetten", new DateOnly(2025, 3, 1), Erfassung.Erst(T0));
        p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, w1, new DateOnly(2025, 3, 1), Erfassung.Erst(T0));
        var nr = p.RufnummerHinzufuegen(RufnummerArt.Mobil, "+49 151 1", Messagingdienst.Sms, true, true, new DateOnly(2025, 3, 1), Erfassung.Erst(T0));

        p.NameAendernAb("Olena", "Kovalenko-Weber", new DateOnly(2025, 4, 1), "Heirat", T(1), "J. Krüger");                      // Name
        p.GeburtsdatumKorrigieren(new DateOnly(1991, 1, 12), Erfassung.Korrektur(T(2), "Pass", "J. Krüger"));                      // Geburtsdatum
        p.UmzugAb(w1, null, "Grevener Damm 120", "48282", "Emsdetten", new DateOnly(2025, 6, 1), Erfassung.Aenderung(T(3), "Umzug")); // Adressen
        p.RufnummerAendernAb(nr, RufnummerArt.Mobil, "+49 151 1", Messagingdienst.Sms | Messagingdienst.Signal, true, true,
            new DateOnly(2025, 6, 1), Erfassung.Aenderung(T(4), "Signal"));                                                         // Telefon
        p.EMailHinzufuegen(EMailArt.Dienstlich, "o.kovalenko@firma.example", false, new DateOnly(2025, 6, 1), Erfassung.Aenderung(T(5), "neu")); // E-Mail
        p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2025, 6, 1), Erfassung.Aenderung(T(6), "Kursanmeldung"));    // Rollen
        p.SpracheAendernAb("de", new DateOnly(2025, 7, 1), Erfassung.Aenderung(T(7), "Wunsch: Post auf Deutsch"));                // Sprache
        return p;
    }

    [Fact]
    public void Historie_enthaelt_Aenderungen_aller_Datenarten_neueste_zuerst()
    {
        var eintraege = Datenhistorie.Erstellen(PersonMitAllenAenderungen());

        eintraege.Select(e => e.Datenart).Distinct().Order().ShouldBe(Datenhistorie.Datenarten.Order());
        eintraege.Select(e => e.ErfasstAm).ShouldBeInOrder(SortDirection.Descending);

        var juengste = eintraege.Where(e => e.ErfasstAm > T0).Select(e => e.Datenart).Distinct().ToList();
        juengste.ShouldBe(["Sprache", "Rollen", "E-Mail", "Telefon", "Adressen", "Geburtsdatum", "Name"]);
    }

    [Fact]
    public void Eintraege_zeigen_alten_und_neuen_Wert_Art_und_Erfasser()
    {
        var eintraege = Datenhistorie.Erstellen(PersonMitAllenAenderungen());

        var name = eintraege.Single(e => e.Datenart == "Name" && e.Art == Erfassungsart.AenderungAbDatum);
        name.AlterWert.ShouldBe("Olena Kovalenko");
        name.NeuerWert.ShouldBe("Olena Kovalenko-Weber");
        name.GueltigVon.ShouldBe(new DateOnly(2025, 4, 1));
        name.ErfasstVon.ShouldBe("J. Krüger");
        name.Grund.ShouldBe("Heirat");

        var geburt = eintraege.Single(e => e.Datenart == "Geburtsdatum" && e.Art == Erfassungsart.Korrektur);
        geburt.AlterWert.ShouldBe("21.01.1991");
        geburt.NeuerWert.ShouldBe("12.01.1991");

        var aufenthalt = eintraege.Single(e => e.Feld == "Üblicher Aufenthaltsort" && e.Art == Erfassungsart.AenderungAbDatum);
        aufenthalt.AlterWert.ShouldBe("Lindenstraße 14, 48282 Emsdetten");
        aufenthalt.NeuerWert.ShouldBe("Grevener Damm 120, 48282 Emsdetten");

        eintraege.ShouldContain(e => e.Feld == "Postadresse" && e.Art == Erfassungsart.Beendet);
    }

    [Fact]
    public void Ersetzte_Erfassungen_sind_markiert_technische_Abgrenzungen_fehlen()
    {
        var eintraege = Datenhistorie.Erstellen(PersonMitAllenAenderungen());

        eintraege.ShouldNotContain(e => e.Art == Erfassungsart.Abgrenzung);
        eintraege.Single(e => e.Datenart == "Geburtsdatum" && e.Art == Erfassungsart.Ersterfassung).Ersetzt.ShouldBeTrue();
        eintraege.Single(e => e.Datenart == "Geburtsdatum" && e.Art == Erfassungsart.Korrektur).Ersetzt.ShouldBeFalse();
    }

    [Fact]
    public void Nur_abgegrenzte_Werte_gelten_weiter_bis_zum_neuen_Ende()
    {
        var eintraege = Datenhistorie.Erstellen(PersonMitAllenAenderungen());

        var alterName = eintraege.Single(e => e.Datenart == "Name" && e.Art == Erfassungsart.Ersterfassung);
        alterName.Ersetzt.ShouldBeFalse();
        alterName.GueltigBis.ShouldBe(new DateOnly(2025, 4, 1));

        var auszug = eintraege.Single(e => e.Feld == "Postadresse" && e.Art == Erfassungsart.Beendet);
        auszug.AlterWert.ShouldBe("Lindenstraße 14, 48282 Emsdetten");
        auszug.NeuerWert.ShouldBe("(aufgegeben)");
    }
}
