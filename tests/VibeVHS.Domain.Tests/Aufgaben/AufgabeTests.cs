using Shouldly;
using VibeVHS.Domain.Aufgaben;

namespace VibeVHS.Domain.Tests.Aufgaben;

public class AufgabeTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(1));

    private static Aufgabe Neu() => new("mindestteilnehmer", "mindestteilnehmer:abc", "26H-811: 1 von mindestens 3", null,
        new DateOnly(2026, 11, 21), new Zustaendigkeit("Fachbereichsleitung", 8), "kurse/abc", Jetzt);

    [Fact]
    public void Neue_Aufgabe_liegt_offen_im_Korb()
    {
        var a = Neu();
        a.Status.ShouldBe(Aufgabenstatus.Offen);
        a.Gruppe.ShouldBe("Fachbereichsleitung");
        a.Fachbereich.ShouldBe(8);
        a.BearbeiterKennung.ShouldBeNull();
        a.Ueberfaellig(new DateOnly(2026, 11, 21)).ShouldBeFalse();
        a.Ueberfaellig(new DateOnly(2026, 11, 22)).ShouldBeTrue();
    }

    [Fact]
    public void Uebernehmen_Zurueckgeben_Erledigen()
    {
        var a = Neu();
        a.Uebernehmen("varvara.schatz", "Varvara Schatz", Jetzt);
        a.Status.ShouldBe(Aufgabenstatus.InBearbeitung);
        a.Zurueckgeben();
        a.Status.ShouldBe(Aufgabenstatus.Offen);
        a.BearbeiterName.ShouldBeNull();
        a.Uebernehmen("tom.berg", "Tom Berg", Jetzt);
        a.Erledigen("Tom Berg", "  ", Jetzt);
        a.Status.ShouldBe(Aufgabenstatus.Erledigt);
        a.Vermerk.ShouldBeNull();
        a.Offen.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => a.Uebernehmen("x", "X", Jetzt));
        Should.Throw<InvalidOperationException>(() => a.Wiedereroeffnen()); // von Menschen erledigt bleibt erledigt
    }

    [Fact]
    public void Automatisch_geschlossen_kann_wieder_geoeffnet_werden_und_behaelt_den_Bearbeiter()
    {
        var a = Neu();
        a.Uebernehmen("varvara.schatz", "Varvara Schatz", Jetzt);
        a.Eskalieren(Jetzt);
        a.AutomatischSchliessen("Mindestzahl erreicht", Jetzt);
        a.Status.ShouldBe(Aufgabenstatus.Geschlossen);
        a.ErledigtVon.ShouldBe("Automatik");
        a.Wiedereroeffnen();
        a.Status.ShouldBe(Aufgabenstatus.InBearbeitung);
        a.EskaliertAm.ShouldBeNull();
        a.Vermerk.ShouldBeNull();
    }

    [Fact]
    public void Anonymisieren_entfernt_den_Namen_und_schliesst()
    {
        var a = new Aufgabe("qualifikation-nachweis", "qualifikation-nachweis:p:bamf-lk", "Maria Rossi: Nachweis fehlt", "Nachweis eintragen.",
            new DateOnly(2026, 12, 1), new Zustaendigkeit("Fachbereichsleitung"), "personen/p", Jetzt);
        a.Anonymisieren("Gelöscht (DSGVO) 15.11.2026", Jetzt);
        a.Titel.ShouldNotContain("Maria");
        a.Beschreibung.ShouldBeNull();
        a.Status.ShouldBe(Aufgabenstatus.Geschlossen);
    }

    [Fact]
    public void Regel_bestimmt_Zustaendigkeit_je_Fachbereich()
    {
        var typ = new Aufgabentyp("mindestteilnehmer", "Mindestteilnehmerzahl", null, "Fachbereichsleitung", jeFachbereich: true, eskalationNachTagen: 1,
            vorlaufTage: 10, menschGibtFrei: true);
        typ.Fuer(8).ShouldBe(new Zustaendigkeit("Fachbereichsleitung", 8));
        typ.Aendern("Verwaltungsleitung", false, " Eva.Lang ", 3, 14, false);
        typ.Fuer(8).ShouldBe(new Zustaendigkeit("Verwaltungsleitung"));
        typ.FestePerson.ShouldBe("eva.lang");
        typ.MenschGibtFrei.ShouldBe(false);
        Should.Throw<ArgumentException>(() => typ.Aendern("Leitung", false, null, -1, null, null));
    }
}
