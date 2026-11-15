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
        ArgumentException.ThrowIfNullOrWhiteSpace(vorname);
        ArgumentException.ThrowIfNullOrWhiteSpace(nachname);
        Vorname = vorname.Trim();
        Nachname = nachname.Trim();
    }

    /// <summary>Kurzform: Änderung ab Datum.</summary>
    public PersonName(string vorname, string nachname, DateOnly gueltigVon, DateOnly? gueltigBis, DateTimeOffset erfasstAm, string grund)
        : this(vorname, nachname, gueltigVon, gueltigBis, Erfassung.Aenderung(erfasstAm, grund))
    {
    }

    public string Vorname { get; private set; }

    public string Nachname { get; private set; }

    public string Anzeige => $"{Vorname} {Nachname}";

    public override string Datenart => "Name";

    public override string Feld => "Name";

    public override string Gegenstand => "name";

    public override string Wert => Anzeige;

    protected internal override PersonName KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Vorname, Nachname, gueltigVon, gueltigBis, erfassung);

    public override string ToString() => Anzeige;
}
