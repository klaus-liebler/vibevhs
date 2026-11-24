using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Application.Oberflaeche;

/// <summary>Eine Seite oder Funktion der Verwaltung, die die Alles-Suche findet (QA-61).</summary>
/// <param name="Titel">So heißt die Funktion in der Oberfläche.</param>
/// <param name="Ziel">Seitenpfad (mit Tab oder Anker).</param>
/// <param name="Stichwoerter">Weitere Wörter, unter denen man sie sucht („Mahnung“ → Mahnwesen).</param>
/// <param name="Rollen">Wer sie öffnen darf; leer = alle Verwaltungsrollen.</param>
public sealed record Funktion(string Titel, string Ziel, string Stichwoerter, IReadOnlyList<Rolle> Rollen)
{
    public bool Erlaubt(Rolle? rolle) => Rollen.Count == 0 || rolle is { } r && Rollen.Contains(r);
}

/// <summary>
/// Funktionen der Verwaltung für die Alles-Suche: Navigation und häufige Unterseiten. Die Rollen spiegeln die Rechteprüfung der Dienste;
/// die Suche zeigt nur, was die aktive Rolle auch öffnen darf.
/// </summary>
public static class Funktionskatalog
{
    private static readonly Rolle[] Finanzen = Application.Finanzen.Finanzrollen.Lesen;
    private static readonly Rolle[] Buchen = Application.Finanzen.Finanzrollen.Buchen;

    public static IReadOnlyList<Funktion> Alle { get; } =
    [
        new("Startseite", "", "start übersicht was steht an", []),
        new("Aufgaben", "aufgaben", "wiedervorlage korb erledigen", []),
        new("Dashboard", "dashboard", "kennzahlen übersicht", []),
        new("Hilfe", "hilfe", "bedienung anleitung tour tastenkürzel", []),
        new("Kurse", "kurse", "programm kursliste veranstaltung", []),
        new("Kurs anlegen", "kurse/neu", "neuer kurs", []),
        new("Raumplan", "raumplan", "belegung räume woche", []),
        new("Orte und Räume", "orte", "raum gebäude außenstelle", []),
        new("Schlüssel", "schluessel", "transponder schließanlage ausgabe rückgabe", Programm.SchluesselDienst.Rollen),
        new("Info-Monitore", "monitore", "bildschirm foyer heute im haus aushang", Programm.InfomonitorDienst.PflegeRollen),
        new("Qualifikationen", "qualifikationen", "nachweis zulassung kursleitung", []),
        new("Ferien und Feiertage", "ferien", "schulferien feiertag", []),
        new("Anmeldung", "anmeldung", "buchen tresen anmelden warteliste", []),
        new("Personen", "personen", "teilnehmende kursleitung kunden adresse", []),
        new("Person anlegen", "personen?neu=1", "neue person", []),
        new("Dubletten", "personen/dubletten", "doppelt zusammenführen", []),
        new("Anwesenheit", "anwesenheit", "check-in liste anwesend", []),
        new("Unterrichts-Apps", "unterrichts-apps", "podium quiz abstimmung lern-app h5p werkzeug", []),
        new("Integrationskurse", "integration", "bamf integration lichtbild fahrtkosten", []),
        new("Anliegen der Kursleitungen", "portal-anliegen", "portal verfügbarkeit kursvorschlag", []),
        new("Finanzen", "finanzen", "rechnung beleg buchhaltung", Finanzen),
        new("GoBD-Prüfung", "finanzen?tab=pruefung", "gobd export prüfer kette", Application.Finanzen.GobdDienst.PruefRollen),
        new("Belege", "finanzen?tab=belege", "rechnung gutschrift storno", Finanzen),
        new("Mahnwesen", "finanzen?tab=mahnwesen", "mahnung mahnlauf zahlungserinnerung", Buchen),
        new("Zahlungsläufe", "finanzen?tab=zahlungen", "lastschrift sepa einzug", Buchen),
        new("Kontoauszüge", "finanzen?tab=konto", "zahlungseingang camt bank", Buchen),
        new("Honorare", "finanzen?tab=honorare", "honorar abrechnung kursleitung", Buchen),
        new("Postausgang", "postausgang", "mail brief versand", []),
        new("Rundschreiben", "personen", "serienmail newsletter rundmail", []),
        new("Vorlagen", "vorlagen", "textvorlage mail brief", []),
        new("Protokoll", "protokoll", "audit änderungen wer", []),
        new("Datenschutz", "datenschutz", "löschen auskunft sperre aufbewahrung", []),
        new("Auswertungen", "auswertungen", "statistik export excel", Betrieb.Auswertungsrollen.Lesen),
        new("Import", "import", "kufer altdaten csv", Betrieb.Betriebsrollen.Import),
        new("Betrieb und Sicherung", "betrieb", "sicherung backup wiederherstellung", Betrieb.Betriebsrollen.Sicherung),
        new("Einstellungen", "einstellungen", "logo website benutzer konto", []),
        new("Meine Einstellungen", "meine-einstellungen", "ansprache du sie", []),
        new("Dokumentation", "dokumentation", "spezifikation kommentare", Dokumentation.KommentarDienst.Rollen),
    ];
}
