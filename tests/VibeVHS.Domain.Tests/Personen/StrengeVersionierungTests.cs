using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

/// <summary>
/// Strenges bitemporales Modell (Architektur, docs/datenmodell.md „Bitemporale Datenhaltung“): Versionen werden nie geändert. Auch das
/// Beenden einer Gültigkeit (Umzug, Heirat) erzeugt eine neue Zeile und setzt ErsetztAm an der alten – GueltigBis einer bestehenden
/// Zeile wird nie überschrieben. Beispiel Frau Muster: Umzug gemeldet 15.03. zum 01.08., Brief am 10.07., Korrektur 20.09. auf 01.07.
/// </summary>
public class StrengeVersionierungTests
{
    private static readonly TimeSpan Mesz = TimeSpan.FromHours(2);

    [Fact]
    public void Beenden_der_Gueltigkeit_ersetzt_die_alte_Zeile_statt_sie_zu_aendern()
    {
        var p = Person.Anlegen("Erika", "Muster", new DateOnly(2020, 1, 1), new DateTimeOffset(2020, 1, 10, 9, 0, 0, TimeSpan.FromHours(1)));
        var linden = p.AdresseHinzufuegen(null, "Lindenstr. 5", "48282", "Emsdetten", new DateOnly(2020, 1, 1), Erfassung.Erst(new DateTimeOffset(2020, 1, 10, 9, 0, 0, TimeSpan.FromHours(1))));
        p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, linden, new DateOnly(2020, 1, 1), Erfassung.Erst(new DateTimeOffset(2020, 1, 10, 9, 0, 0, TimeSpan.FromHours(1))));
        var zeileA = p.Adressen.Single();
        var vorher = p.AlleVersionen.Select(v => (v, v.GueltigVon, v.GueltigBis)).ToList();

        // 15.03.: Umzug zum 01.08. gemeldet
        var gemeldet = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.FromHours(1));
        p.UmzugAb(linden, null, "Bergweg 2", "48282", "Emsdetten", new DateOnly(2026, 8, 1), Erfassung.Aenderung(gemeldet, "Umzug gemeldet"));

        // Keine bestehende Zeile wurde in ihrer Gültigkeit verändert; A ist ersetzt (Wissenszeit), B ist die neue, abgegrenzte Zeile.
        foreach (var (v, von, bis) in vorher)
        {
            (v.GueltigVon, v.GueltigBis).ShouldBe((von, bis));
        }

        zeileA.ErsetztAm.ShouldBe(gemeldet);
        p.Adressen.ShouldContain(a => a.Strasse == "Lindenstr. 5" && a.GueltigBis == new DateOnly(2026, 8, 1) && a.ErsetztAm == null);

        // Abfragen: am 10.07. nach Wissensstand 10.07. → Lindenstraße; am 10.08. → Bergweg
        var brief = new DateTimeOffset(2026, 7, 10, 9, 0, 0, Mesz);
        p.StandAm(new DateOnly(2026, 7, 10), brief).Aufenthaltsort!.Strasse.ShouldBe("Lindenstr. 5");
        p.StandAm(new DateOnly(2026, 8, 10), brief).Aufenthaltsort!.Strasse.ShouldBe("Bergweg 2");
        // Wissensstand vor der Meldung (10.03.): Umzug noch unbekannt
        p.StandAm(new DateOnly(2026, 8, 10), new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.FromHours(1))).Aufenthaltsort!.Strasse.ShouldBe("Lindenstr. 5");
    }

    [Fact]
    public void Auch_Aendern_ab_Datum_bei_Merkmalen_ueberschreibt_keine_Gueltigkeit()
    {
        var erfasst = new DateTimeOffset(2020, 1, 10, 9, 0, 0, TimeSpan.FromHours(1));
        var p = Person.Anlegen("Erika", "Muster", new DateOnly(2020, 1, 1), erfasst, sprache: "de");
        var vorher = p.AlleVersionen.Select(v => (v, v.GueltigVon, v.GueltigBis)).ToList();
        p.SpracheAendernAb("tr", new DateOnly(2026, 8, 1), Erfassung.Aenderung(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.FromHours(1)), "Wunsch"));
        p.NameAendernAb("Erika", "Berg", new DateOnly(2026, 6, 14), "Heirat", new DateTimeOffset(2026, 7, 2, 10, 0, 0, Mesz));
        foreach (var (v, von, bis) in vorher)
        {
            (v.GueltigVon, v.GueltigBis).ShouldBe((von, bis));
        }

        p.Merkmale.Count(m => m.Art == MerkmalArt.Sprache).ShouldBe(3); // alt (ersetzt) + abgegrenzt + neu
        p.Namen.Count.ShouldBe(3);
    }
}
