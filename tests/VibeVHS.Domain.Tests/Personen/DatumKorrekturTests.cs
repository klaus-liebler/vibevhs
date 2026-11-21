using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

/// <summary>
/// Gültigkeitsbeginn bzw. -ende korrigieren (Datum war falsch erfasst): strenges Modell – die bisherigen Zeilen bleiben als ersetzt erhalten,
/// der frühere Wissensstand bleibt abfragbar, beide Seiten der Grenze werden gemeinsam korrigiert.
/// </summary>
public sealed class DatumKorrekturTests
{
    private static readonly DateTimeOffset Anfang = new(2020, 1, 10, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Gemeldet = new(2026, 3, 15, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Korrigiert = new(2026, 9, 20, 9, 0, 0, TimeSpan.FromHours(2));

    private static (Person P, Guid Alt, Guid Neu) MitUmzug()
    {
        var p = Person.Anlegen("Erika", "Muster", new DateOnly(2020, 1, 1), Anfang);
        var alt = p.AdresseHinzufuegen(null, "Lindenstr. 5", "48282", "Emsdetten", new DateOnly(2020, 1, 1), Erfassung.Erst(Anfang));
        p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, alt, new DateOnly(2020, 1, 1), Erfassung.Erst(Anfang));
        var neu = p.UmzugAb(alt, null, "Bergweg 2", "48268", "Greven", new DateOnly(2026, 8, 1), Erfassung.Aenderung(Gemeldet, "Umzug gemeldet"));
        return (p, alt, neu);
    }

    [Fact]
    public void Umzugsdatum_korrigieren_verschiebt_Ende_der_alten_und_Beginn_der_neuen_Adresse()
    {
        var (p, _, neu) = MitUmzug();
        p.StandAm(new DateOnly(2026, 7, 10), Korrigiert).Aufenthaltsort!.Ort.ShouldBe("Emsdetten");

        p.UmzugsdatumKorrigieren(neu, new DateOnly(2026, 7, 1), Erfassung.Korrektur(Korrigiert, "Meldebescheinigung: Umzug am 01.07."));

        var jetzt = Korrigiert.AddMinutes(1);
        p.StandAm(new DateOnly(2026, 7, 10), jetzt).Aufenthaltsort!.Ort.ShouldBe("Greven");
        p.StandAm(new DateOnly(2026, 6, 30), jetzt).Aufenthaltsort!.Ort.ShouldBe("Emsdetten");
        // Früherer Wissensstand: „Wohin hätten wir am 10.07. geschrieben?“ – nach Emsdetten.
        p.StandAm(new DateOnly(2026, 7, 10), new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.FromHours(2))).Aufenthaltsort!.Ort.ShouldBe("Emsdetten");
        // Strenges Modell: nichts überschrieben, die korrigierten Zeilen verweisen auf denselben Vorgang.
        var ersetzt = p.Adressen.Where(a => a.ErsetztAm == Korrigiert).ToList();
        ersetzt.Count.ShouldBe(3); // Ende der alten Adresse, deren Endemarke und Beginn der neuen
        p.Adressen.Where(a => a.ErfasstAm == Korrigiert).Select(a => a.ErfasstDurch).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public void Datum_einer_Namensaenderung_korrigieren()
    {
        var p = Person.Anlegen("Erika", "Muster", new DateOnly(2020, 1, 1), Anfang);
        p.NameAendernAb("Erika", "Berg", new DateOnly(2026, 8, 1), "Heirat", Gemeldet);

        p.NamensaenderungsdatumKorrigieren(new DateOnly(2026, 8, 1), new DateOnly(2026, 6, 1), Erfassung.Korrektur(Korrigiert, "Heiratsurkunde vom 01.06."));

        p.StandAm(new DateOnly(2026, 6, 15), Korrigiert).Name!.Nachname.ShouldBe("Berg");
        p.StandAm(new DateOnly(2026, 5, 31), Korrigiert).Name!.Nachname.ShouldBe("Muster");
    }

    [Fact]
    public void Datum_einer_Angabe_korrigieren_und_Grenzen_pruefen()
    {
        var p = Person.Anlegen("Erika", "Muster", new DateOnly(2020, 1, 1), Anfang);
        p.AngabeAendern(MerkmalArt.Titel, "Dr.", new DateOnly(2026, 8, 1), Erfassung.Aenderung(Gemeldet, "Promotion"), korrektur: false);

        p.AngabedatumKorrigieren(MerkmalArt.Titel, new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 15), Erfassung.Korrektur(Korrigiert, "Urkunde"));
        p.StandAm(new DateOnly(2026, 7, 20), Korrigiert).Titel.ShouldBe("Dr.");

        Should.Throw<InvalidOperationException>(() =>
                p.AngabedatumKorrigieren(MerkmalArt.Titel, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), Erfassung.Korrektur(Korrigiert.AddDays(1), "x")))
            .Message.ShouldContain("bisherige Datum prüfen");
    }
}
