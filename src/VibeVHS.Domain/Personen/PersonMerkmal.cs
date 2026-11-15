using System.Globalization;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

public enum MerkmalArt
{
    Geburtsdatum,
    Sprache,
    Rollen,
}

/// <summary>
/// Einfaches zeitlich versioniertes Merkmal einer Person (Geburtsdatum, bevorzugte Sprache, Rollen).
/// Der Wert wird als Text in einem festen, kulturunabhängigen Format gespeichert.
/// </summary>
public sealed class PersonMerkmal : BitemporaleVersion<PersonMerkmal>
{
    private PersonMerkmal()
    {
        Inhalt = string.Empty;
    }

    public PersonMerkmal(MerkmalArt art, string inhalt, DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        Art = art;
        Inhalt = inhalt ?? string.Empty;
    }

    public MerkmalArt Art { get; private set; }

    /// <summary>Gespeicherter Wert: Datum als yyyy-MM-dd, Sprache als ISO-Code, Rollen als Zahl.</summary>
    public string Inhalt { get; private set; }

    public override string Datenart => Art switch
    {
        MerkmalArt.Geburtsdatum => "Geburtsdatum",
        MerkmalArt.Sprache => "Sprache",
        _ => "Rollen",
    };

    public override string Feld => Art switch
    {
        MerkmalArt.Geburtsdatum => "Geburtsdatum",
        MerkmalArt.Sprache => "Bevorzugte Sprache",
        _ => "Rollen",
    };

    public override string Gegenstand => Art.ToString();

    public override string Wert => IstEndemarke ? "(beendet)" : Art switch
    {
        MerkmalArt.Geburtsdatum => AlsDatum()?.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE")) ?? "–",
        MerkmalArt.Rollen => RollenAnzeige(AlsRollen()),
        _ => Inhalt,
    };

    public DateOnly? AlsDatum() => DateOnly.TryParseExact(Inhalt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public Personenrolle AlsRollen() => int.TryParse(Inhalt, CultureInfo.InvariantCulture, out var r) ? (Personenrolle)r : Personenrolle.Keine;

    public static string DatumText(DateOnly datum) => datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string RollenText(Personenrolle rollen) => ((int)rollen).ToString(CultureInfo.InvariantCulture);

    public static string RollenAnzeige(Personenrolle rollen)
    {
        var teile = new List<string>();
        if (rollen.HasFlag(Personenrolle.Teilnehmende))
        {
            teile.Add("Teilnehmende");
        }

        if (rollen.HasFlag(Personenrolle.Dozierende))
        {
            teile.Add("Dozierende");
        }

        if (rollen.HasFlag(Personenrolle.Mitarbeitende))
        {
            teile.Add("Mitarbeitende");
        }

        return teile.Count == 0 ? "keine" : string.Join(", ", teile);
    }

    protected internal override PersonMerkmal KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Art, Inhalt, gueltigVon, gueltigBis, erfassung);
}
