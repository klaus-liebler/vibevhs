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

    public PersonName(string vorname, string nachname, DateOnly gueltigVon, DateOnly? gueltigBis, DateTimeOffset erfasstAm, string grund)
        : base(gueltigVon, gueltigBis, erfasstAm, grund)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vorname);
        ArgumentException.ThrowIfNullOrWhiteSpace(nachname);
        Vorname = vorname.Trim();
        Nachname = nachname.Trim();
    }

    public string Vorname { get; private set; }

    public string Nachname { get; private set; }

    public string Anzeige => $"{Vorname} {Nachname}";

    protected internal override PersonName KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, DateTimeOffset erfasstAm, string grund)
        => new(Vorname, Nachname, gueltigVon, gueltigBis, erfasstAm, grund);

    public override string ToString() => Anzeige;
}
