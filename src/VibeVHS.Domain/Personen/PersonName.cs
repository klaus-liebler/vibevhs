using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

/// <summary>Eine Version des Namens einer Person (bitemporal).</summary>
public sealed class PersonName : BitemporaleVersion<PersonName>
{
    private PersonName()
    {
        Vorname = string.Empty;
        Nachname = string.Empty;
    }

    public PersonName(string vorname, string nachname, DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        // Vorname leer = „nur ein Name laut Ausweis“ (z. B. indonesische oder afghanische Dokumente); der Name steht dann im Nachnamen.
        ArgumentException.ThrowIfNullOrWhiteSpace(nachname);
        Vorname = vorname?.Trim() ?? string.Empty;
        Nachname = nachname.Trim();
    }

    /// <summary>Kurzform: Änderung ab Datum.</summary>
    public PersonName(string vorname, string nachname, DateOnly gueltigVon, DateOnly? gueltigBis, DateTimeOffset erfasstAm, string grund)
        : this(vorname, nachname, gueltigVon, gueltigBis, Erfassung.Aenderung(erfasstAm, grund))
    {
    }

    public string Vorname { get; private set; }

    public string Nachname { get; private set; }

    /// <summary>Kennzeichen „nur ein Name laut Ausweis“: kein Vorname, der einzige Name steht im Nachnamen.</summary>
    public bool NurEinName => Vorname.Length == 0;

    public string Anzeige => NurEinName ? Nachname : $"{Vorname} {Nachname}";

    internal void Anonymisieren(string vorname, string nachname)
    {
        Vorname = vorname;
        Nachname = nachname;
    }

    public override string Datenart => "Name";

    public override string Feld => "Name";

    public override string Gegenstand => "name";

    public override string Wert => Anzeige;

    protected internal override PersonName KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Vorname, Nachname, gueltigVon, gueltigBis, erfassung);

    public override string ToString() => Anzeige;
}
