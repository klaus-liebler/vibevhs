namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>Einstufung einer Spalte der Stammdatentabellen (Grundreinigung 11/2026, siehe CLAUDE.md „Stammdaten“).</summary>
public enum Spaltenart
{
    /// <summary>Führendes Stamm- oder Prozessdatum (z. B. Id, Status, Kursnummer). Fachliche Personenangaben gehören NICHT hierher, sondern in Merkmale.</summary>
    Fuehrend,

    /// <summary>Lese-Cache einer bitemporalen Quelle für Listen, Filter, Suche und Sortierung – nie direkt schreiben, wird fortgeschrieben.</summary>
    Cache,

    /// <summary>Nur nach der Anonymisierung gefüllt (grobe Statistikangaben statt personenbezogener Daten).</summary>
    Anonymisierungsrest,
}

/// <summary>
/// Verbindliche Liste aller Spalten der Stammdatentabellen mit Einstufung und Quelle. Der Architekturtest „Spalteneinstufung“ schlägt fehl,
/// sobald eine Spalte hinzukommt oder wegfällt, ohne hier eingetragen zu sein – neue Personenangaben werden so nicht versehentlich als
/// überschreibbare Spalte angelegt (Regel: neue Personenangabe = versioniertes Merkmal).
/// </summary>
public static class Spalteneinstufung
{
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, (Spaltenart Art, string Bedeutung)>> Tabellen { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, (Spaltenart, string)>>
        {
            ["Personen"] = new Dictionary<string, (Spaltenart, string)>
            {
                ["Id"] = (Spaltenart.Fuehrend, "Schlüssel"),
                ["AngelegtAm"] = (Spaltenart.Fuehrend, "Zeitpunkt der Ersterfassung"),
                ["Status"] = (Spaltenart.Fuehrend, "Prozesszustand (aktiv, ruhend, zusammengeführt …), Änderungen im Protokoll"),
                ["Datenschutz"] = (Spaltenart.Fuehrend, "Datenschutzstatus (aktiv, gesperrt, archiviert, anonymisiert)"),
                ["GesperrtAm"] = (Spaltenart.Fuehrend, "Sperre (Prozess)"),
                ["GesperrtBis"] = (Spaltenart.Fuehrend, "Sperre (Prozess)"),
                ["Sperrgrund"] = (Spaltenart.Fuehrend, "Sperre (Prozess)"),
                ["ArchiviertAm"] = (Spaltenart.Fuehrend, "Archivierung (Prozess)"),
                ["AnonymisiertAm"] = (Spaltenart.Fuehrend, "Anonymisierung (Prozess)"),
                ["ZusammengefuehrtIn"] = (Spaltenart.Fuehrend, "Dublettenzusammenführung (Prozess)"),
                ["Fachbereiche"] = (Spaltenart.Fuehrend, "Zuordnung zu Fachbereichen (Organisation, nicht personenbezogen)"),
                ["Geburtsdatum"] = (Spaltenart.Cache, "Merkmal Geburtsdatum"),
                ["Sprache"] = (Spaltenart.Cache, "Merkmal Sprache"),
                ["EMail"] = (Spaltenart.Cache, "bevorzugte E-Mail-Adresse (versioniert)"),
                ["Rollen"] = (Spaltenart.Cache, "Merkmal Rollen (Bitmaske Personenrolle) zum heutigen Tag"),
                ["Titel"] = (Spaltenart.Cache, "Merkmal Titel zum heutigen Tag"),
                ["Anzeigename"] = (Spaltenart.Cache, "Titel + Name zum heutigen Tag"),
                ["Sortiername"] = (Spaltenart.Cache, "Name zum heutigen Tag, normalisiert"),
                ["Sortierschluessel"] = (Spaltenart.Cache, "Sortiername + Id (Keyset-Paging)"),
                ["Wohnort"] = (Spaltenart.Cache, "Ort des üblichen Aufenthalts zum heutigen Tag (Adresse + Kennzeichen)"),
                ["AnonymisiertGeburtskohorte"] = (Spaltenart.Anonymisierungsrest, "Geburtsjahr-Kohorte statt Geburtsdatum"),
                ["AnonymisiertGebiet"] = (Spaltenart.Anonymisierungsrest, "Kreis/PLZ-Gebiet statt Adresse"),
            },
            ["Kurse"] = new Dictionary<string, (Spaltenart, string)>
            {
                ["Id"] = (Spaltenart.Fuehrend, "Schlüssel"),
                ["Kursnummer"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["Titel"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["Beschreibung"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["Fachbereich"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["KursartId"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["SemesterId"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["MinTeilnehmende"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["MaxTeilnehmende"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["Status"] = (Spaltenart.Fuehrend, "Prozesszustand"),
                ["Serie"] = (Spaltenart.Fuehrend, "Serienregel"),
                ["Richtlinie"] = (Spaltenart.Fuehrend, "Buchungsrichtlinie (aus der Kursart übernommen, kursbezogen änderbar)"),
                ["Umfang"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["Sichtbarkeit"] = (Spaltenart.Fuehrend, "Planungsdatum"),
                ["Bildungsurlaub"] = (Spaltenart.Fuehrend, "Anerkennung"),
                ["BenoetigteMerkmale"] = (Spaltenart.Fuehrend, "Raumanforderungen (aus der Kursart übernommen, kursbezogen änderbar)"),
                ["BenoetigteDozierendenmerkmale"] = (Spaltenart.Fuehrend, "Anforderungen an Lehrkräfte (z. B. sprachfreigabe=Englisch)"),
                ["AuftraggeberId"] = (Spaltenart.Fuehrend, "Verweis auf die Organisation"),
                ["ArchiviertAm"] = (Spaltenart.Fuehrend, "Archivierung (Prozess)"),
                ["Team"] = (Spaltenart.Cache, "Lehrkräfte (PersonId, Leitung führend; Name = Cache: Name zum letzten Termin bzw. heute)"),
                ["AuftraggeberName"] = (Spaltenart.Cache, "Name der Organisation"),
                ["Nutzungsart"] = (Spaltenart.Cache, "aus der Kursart"),
                ["Suchtext"] = (Spaltenart.Cache, "Suche"),
                ["ErzeugtBis"] = (Spaltenart.Cache, "wie weit die Termine einer unbefristeten Serie erzeugt sind"),
            },
            ["Orte"] = new Dictionary<string, (Spaltenart, string)>
            {
                ["Id"] = (Spaltenart.Fuehrend, "Schlüssel"),
                ["Art"] = (Spaltenart.Fuehrend, "Stammdatum Ort"),
                ["Name"] = (Spaltenart.Fuehrend, "Stammdatum Ort"),
                ["Kurzname"] = (Spaltenart.Fuehrend, "Stammdatum Ort"),
                ["ElternId"] = (Spaltenart.Fuehrend, "Hierarchie"),
                ["Strasse"] = (Spaltenart.Fuehrend, "Adresse des Orts (nicht personenbezogen)"),
                ["Postleitzahl"] = (Spaltenart.Fuehrend, "Adresse des Orts"),
                ["Stadt"] = (Spaltenart.Fuehrend, "Adresse des Orts"),
                ["Breitengrad"] = (Spaltenart.Fuehrend, "Geo"),
                ["Laengengrad"] = (Spaltenart.Fuehrend, "Geo"),
                ["Lage"] = (Spaltenart.Fuehrend, "Wegbeschreibung"),
                ["Zugang"] = (Spaltenart.Fuehrend, "Zugangshinweis"),
                ["Kapazitaeten"] = (Spaltenart.Fuehrend, "Kapazität je Nutzungsart"),
                ["Aktiv"] = (Spaltenart.Fuehrend, "Prozesszustand"),
                ["Suchtext"] = (Spaltenart.Cache, "Suche"),
            },
            ["Benutzerkonten"] = new Dictionary<string, (Spaltenart, string)>
            {
                ["Id"] = (Spaltenart.Fuehrend, "Schlüssel"),
                ["Login"] = (Spaltenart.Fuehrend, "Anmeldename"),
                ["Funktion"] = (Spaltenart.Fuehrend, "Funktionsbezeichnung"),
                ["Rollen"] = (Spaltenart.Fuehrend, "Berechtigungsrollen des Kontos"),
                ["Fachbereiche"] = (Spaltenart.Fuehrend, "Zuständigkeit"),
                ["Aktiv"] = (Spaltenart.Fuehrend, "Prozesszustand"),
                ["Hinweis"] = (Spaltenart.Fuehrend, "Notiz"),
                ["PersonId"] = (Spaltenart.Fuehrend, "einzige Verknüpfung Konto ↔ Person"),
                ["Name"] = (Spaltenart.Cache, "Anzeigename der verknüpften Person (ohne Person: führend)"),
            },
            ["Organisationen"] = new Dictionary<string, (Spaltenart, string)>
            {
                ["Id"] = (Spaltenart.Fuehrend, "Schlüssel"),
                ["Art"] = (Spaltenart.Fuehrend, "Stammdatum Organisation"),
                ["Name"] = (Spaltenart.Fuehrend, "Stammdatum Organisation"),
                ["Ort"] = (Spaltenart.Fuehrend, "Stammdatum Organisation"),
                ["Rechnungsanschrift"] = (Spaltenart.Fuehrend, "Belege frieren ihre Anschrift selbst ein (GoBD)"),
                ["RechnungsEMail"] = (Spaltenart.Fuehrend, "Stammdatum Organisation"),
                ["LeitwegId"] = (Spaltenart.Fuehrend, "XRechnung"),
            },
        };
}
