using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

/// <summary>Eine Version des Namens einer Person (bitemporal).</summary>
public sealed class PersonName : BitemporaleVersion<PersonName>
{
    private PersonName()
    {
        Vorname = string.Empty;
        Nachname = string.Empty;
        Vorsatz = string.Empty;
        Zusatz = string.Empty;
        Sortierschluessel = string.Empty;
    }

    public PersonName(string vorname, string nachname, DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung, string? vorsatz = null,
        string? zusatz = null)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        Vorsatz = vorsatz?.Trim() ?? string.Empty;
        Zusatz = zusatz?.Trim() ?? string.Empty;
        // Vorname leer = „nur ein Name laut Ausweis“ (z. B. indonesische oder afghanische Dokumente); der Name steht dann im Nachnamen.
        ArgumentException.ThrowIfNullOrWhiteSpace(nachname);
        Vorname = vorname?.Trim() ?? string.Empty;
        Nachname = nachname.Trim();
        Sortierschluessel = Suchtext.Normalisieren(Sortierform);
    }

    /// <summary>Kurzform: Änderung ab Datum.</summary>
    public PersonName(string vorname, string nachname, DateOnly gueltigVon, DateOnly? gueltigBis, DateTimeOffset erfasstAm, string grund)
        : this(vorname, nachname, gueltigVon, gueltigBis, Erfassung.Aenderung(erfasstAm, grund))
    {
    }

    public string Vorname { get; private set; }

    public string Nachname { get; private set; }

    /// <summary>Namensvorsatz („von“, „van der“): in Anzeige und Anrede vor dem Nachnamen, bei der Sortierung nachgestellt.</summary>
    public string Vorsatz { get; private set; }

    /// <summary>Namenszusatz („jun.“, „sen.“): in der Anzeige hinten.</summary>
    public string Zusatz { get; private set; }

    /// <summary>Nachname mit Vorsatz, z. B. „von Berg“ (für die Anrede „Frau von Berg“).</summary>
    public string NachnameMitVorsatz => Vorsatz.Length == 0 ? Nachname : $"{Vorsatz} {Nachname}";

    /// <summary>
    /// Normalisierte Sortierform dieser Version („berg anna von“) für Sortierung, Seitenschlüssel und Suche in SQL. Abgeleitet aus den
    /// Feldern derselben, unveränderlichen Zeile – kann daher nicht veralten (keine Kopie).
    /// </summary>
    public string Sortierschluessel { get; private set; }

    /// <summary>Sortierform nach DIN 5007-2 (Telefonbuch): Nachname, Vorname, Vorsatz – „Berg Anna von“.</summary>
    public string Sortierform => string.Join(' ', new[] { Nachname, Vorname, Vorsatz }.Where(t => t.Length > 0));

    /// <summary>Kennzeichen „nur ein Name laut Ausweis“: kein Vorname, der einzige Name steht im Nachnamen.</summary>
    public bool NurEinName => Vorname.Length == 0;

    public string Anzeige => string.Join(' ', new[] { Vorname, Vorsatz, Nachname, Zusatz }.Where(t => t.Length > 0));

    internal void Anonymisieren(string vorname, string nachname)
    {
        Vorname = vorname;
        Nachname = nachname;
        Vorsatz = string.Empty;
        Zusatz = string.Empty;
        Sortierschluessel = Suchtext.Normalisieren(Sortierform);
    }

    public override string Datenart => "Name";

    public override string Feld => "Name";

    public override string Gegenstand => "name";

    public override string Wert => Anzeige;

    protected internal override PersonName KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Vorname, Nachname, gueltigVon, gueltigBis, erfassung, Vorsatz, Zusatz);

    public override string ToString() => Anzeige;
}
