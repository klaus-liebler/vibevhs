using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Aufgaben;

public enum Aufgabenstatus
{
    /// <summary>Liegt im Gruppenkorb, niemand bearbeitet sie.</summary>
    Offen,

    /// <summary>Jemand hat sie übernommen oder sie wurde zugewiesen (persönliche Liste).</summary>
    InBearbeitung,

    /// <summary>Von einem Menschen erledigt.</summary>
    Erledigt,

    /// <summary>Automatisch geschlossen, weil der Anlass entfallen ist (z. B. Mindestzahl erreicht).</summary>
    Geschlossen,
}

public enum Dringlichkeit
{
    Normal,
    Hoch,
    Kritisch,
}

/// <summary>Wer ist zuständig? Eine Rolle (Gruppenkorb), optional eingeschränkt auf einen Fachbereich.</summary>
public sealed record Zustaendigkeit(string Rolle, int? Fachbereich = null);

/// <summary>
/// Aufgabe aus einer Regel oder von Hand. Sie liegt im Gruppenkorb der zuständigen Rolle (ggf. je Fachbereich),
/// bis jemand sie übernimmt oder zugewiesen bekommt. Überfällige Aufgaben werden an die Leitung eskaliert;
/// entfällt der Anlass, schließt die Regel sie automatisch.
/// </summary>
public sealed class Aufgabe
{
    private Aufgabe()
    {
        Typ = string.Empty;
        Titel = string.Empty;
        Schluessel = string.Empty;
        Gruppe = string.Empty;
    }

    public Aufgabe(string typ, string schluessel, string titel, string? beschreibung, DateOnly faellig, Zustaendigkeit zustaendig, string? ziel,
        DateTimeOffset angelegtAm, Dringlichkeit dringlichkeit = Dringlichkeit.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typ);
        ArgumentException.ThrowIfNullOrWhiteSpace(schluessel);
        ArgumentException.ThrowIfNullOrWhiteSpace(titel);
        ArgumentNullException.ThrowIfNull(zustaendig);
        ArgumentException.ThrowIfNullOrWhiteSpace(zustaendig.Rolle);
        Id = Ids.Neu();
        Typ = typ;
        Schluessel = schluessel;
        Titel = titel;
        Beschreibung = beschreibung;
        Faellig = faellig;
        Gruppe = zustaendig.Rolle;
        Fachbereich = zustaendig.Fachbereich;
        Ziel = ziel;
        AngelegtAm = angelegtAm;
        Dringlichkeit = dringlichkeit;
        Status = Aufgabenstatus.Offen;
    }

    public Guid Id { get; private set; }

    /// <summary>Aufgabentyp (Regel), z. B. "mindestteilnehmer". Bestimmt Zuständigkeit und Eskalation.</summary>
    public string Typ { get; private set; }

    /// <summary>Fachlicher Schlüssel des Anlasses, damit eine Regel dieselbe Aufgabe nicht doppelt anlegt.</summary>
    public string Schluessel { get; private set; }

    public string Titel { get; private set; }

    public string? Beschreibung { get; private set; }

    public DateOnly Faellig { get; private set; }

    public Dringlichkeit Dringlichkeit { get; private set; }

    /// <summary>Zuständige Rolle (Gruppenkorb), technischer Name, z. B. "IntegrationskursTeam".</summary>
    public string Gruppe { get; private set; }

    /// <summary>Nur Mitarbeitende dieses Fachbereichs sehen die Aufgabe im Korb (leer = alle der Rolle).</summary>
    public int? Fachbereich { get; private set; }

    /// <summary>Relative Adresse in der Anwendung, z. B. "kurse/…" – ein Klick führt dorthin.</summary>
    public string? Ziel { get; private set; }

    public DateTimeOffset AngelegtAm { get; private set; }

    public Aufgabenstatus Status { get; private set; }

    /// <summary>Anmeldename der Person, die die Aufgabe bearbeitet.</summary>
    public string? BearbeiterKennung { get; private set; }

    public string? BearbeiterName { get; private set; }

    public DateTimeOffset? UebernommenAm { get; private set; }

    public DateTimeOffset? EskaliertAm { get; private set; }

    public DateTimeOffset? ErledigtAm { get; private set; }

    /// <summary>Wer erledigt hat ("Automatik" beim automatischen Schließen).</summary>
    public string? ErledigtVon { get; private set; }

    /// <summary>Ergebnis bzw. Grund des Schließens.</summary>
    public string? Vermerk { get; private set; }

    public bool Offen => Status is Aufgabenstatus.Offen or Aufgabenstatus.InBearbeitung;

    public bool Ueberfaellig(DateOnly heute) => Offen && Faellig < heute;

    public void Uebernehmen(string kennung, string name, DateTimeOffset jetzt)
    {
        OffenPruefen();
        ArgumentException.ThrowIfNullOrWhiteSpace(kennung);
        if (BearbeiterKennung == kennung)
        {
            return;
        }

        BearbeiterKennung = kennung;
        BearbeiterName = name;
        UebernommenAm = jetzt;
        Status = Aufgabenstatus.InBearbeitung;
    }

    /// <summary>Zurück in den Gruppenkorb.</summary>
    public void Zurueckgeben()
    {
        OffenPruefen();
        BearbeiterKennung = null;
        BearbeiterName = null;
        UebernommenAm = null;
        Status = Aufgabenstatus.Offen;
    }

    public void Erledigen(string von, string? vermerk, DateTimeOffset jetzt)
    {
        OffenPruefen();
        Status = Aufgabenstatus.Erledigt;
        ErledigtAm = jetzt;
        ErledigtVon = von;
        Vermerk = string.IsNullOrWhiteSpace(vermerk) ? null : vermerk.Trim();
    }

    /// <summary>Anlass entfallen: Die Regel schließt die Aufgabe selbst.</summary>
    public void AutomatischSchliessen(string grund, DateTimeOffset jetzt)
    {
        OffenPruefen();
        Status = Aufgabenstatus.Geschlossen;
        ErledigtAm = jetzt;
        ErledigtVon = "Automatik";
        Vermerk = grund;
    }

    /// <summary>Der Anlass ist nach dem automatischen Schließen wieder da (z. B. wieder unter der Mindestzahl).</summary>
    public void Wiedereroeffnen()
    {
        if (Status != Aufgabenstatus.Geschlossen)
        {
            throw new InvalidOperationException("Nur automatisch geschlossene Aufgaben werden wieder geöffnet.");
        }

        Status = BearbeiterKennung is null ? Aufgabenstatus.Offen : Aufgabenstatus.InBearbeitung;
        ErledigtAm = null;
        ErledigtVon = null;
        Vermerk = null;
        EskaliertAm = null;
    }

    public void Eskalieren(DateTimeOffset jetzt)
    {
        OffenPruefen();
        EskaliertAm ??= jetzt;
        Dringlichkeit = Dringlichkeit.Kritisch;
    }

    /// <summary>Die Regel liefert neue Angaben zum selben Anlass (z. B. andere Teilnehmendenzahl).</summary>
    public bool Aktualisieren(string titel, string? beschreibung, DateOnly faellig, string? ziel)
    {
        if (Titel == titel && Beschreibung == beschreibung && Faellig == faellig && Ziel == ziel)
        {
            return false;
        }

        Titel = titel;
        Beschreibung = beschreibung;
        Faellig = faellig;
        Ziel = ziel;
        return true;
    }

    /// <summary>Die betroffene Person wurde anonymisiert: Titel und Beschreibung ohne Namen, offene Aufgabe schließen.</summary>
    public void Anonymisieren(string platzhalter, DateTimeOffset jetzt)
    {
        Titel = $"Aufgabe zu {platzhalter}";
        Beschreibung = null;
        if (Offen)
        {
            AutomatischSchliessen("Person anonymisiert", jetzt);
        }
    }

    private void OffenPruefen()
    {
        if (!Offen)
        {
            throw new InvalidOperationException("Die Aufgabe ist bereits abgeschlossen.");
        }
    }
}

/// <summary>
/// Regel je Aufgabentyp: zuständige Rolle, Einschränkung auf den Fachbereich, optional eine feste Person,
/// Eskalation an die Leitung und – bei kritischen Automatismen – ob ein Mensch freigeben muss.
/// </summary>
public sealed class Aufgabentyp
{
    private Aufgabentyp()
    {
        Schluessel = string.Empty;
        Name = string.Empty;
        Rolle = string.Empty;
    }

    public Aufgabentyp(string schluessel, string name, string? beschreibung, string rolle, bool jeFachbereich, int eskalationNachTagen,
        int? vorlaufTage = null, bool? menschGibtFrei = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schluessel);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Schluessel = schluessel;
        Name = name;
        Beschreibung = beschreibung;
        Rolle = rolle;
        Aendern(rolle, jeFachbereich, null, eskalationNachTagen, vorlaufTage, menschGibtFrei);
    }

    public string Schluessel { get; private set; }

    public string Name { get; private set; }

    public string? Beschreibung { get; private set; }

    /// <summary>Technischer Name der zuständigen Rolle.</summary>
    public string Rolle { get; private set; }

    /// <summary>Aufgabe landet im Korb des Fachbereichs, zu dem der Anlass gehört.</summary>
    public bool JeFachbereich { get; private set; }

    /// <summary>Anmeldename einer festen Person: Die Aufgabe wird ihr direkt zugewiesen.</summary>
    public string? FestePerson { get; private set; }

    /// <summary>So viele Tage nach Fälligkeit geht die Aufgabe an die Leitung.</summary>
    public int EskalationNachTagen { get; private set; }

    /// <summary>Bei Prüfregeln: so viele Tage vorher wird geprüft (z. B. Mindestteilnehmerzahl).</summary>
    public int? VorlaufTage { get; private set; }

    /// <summary>
    /// Nur bei kritischen Automatismen (z. B. Absage): true = die Regel schlägt nur vor, ein Mensch gibt frei;
    /// false = die Regel handelt bei Fälligkeit selbst. Leer = kein kritischer Automatismus.
    /// </summary>
    public bool? MenschGibtFrei { get; private set; }

    public void Aendern(string rolle, bool jeFachbereich, string? festePerson, int eskalationNachTagen, int? vorlaufTage, bool? menschGibtFrei)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rolle);
        if (eskalationNachTagen < 0 || vorlaufTage < 0)
        {
            throw new ArgumentException("Tage dürfen nicht negativ sein.");
        }

        Rolle = rolle;
        JeFachbereich = jeFachbereich;
        FestePerson = string.IsNullOrWhiteSpace(festePerson) ? null : festePerson.Trim().ToLowerInvariant();
        EskalationNachTagen = eskalationNachTagen;
        VorlaufTage = vorlaufTage;
        MenschGibtFrei = menschGibtFrei;
    }

    public Zustaendigkeit Fuer(int? fachbereich) => new(Rolle, JeFachbereich ? fachbereich : null);
}
