using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

/// <summary>
/// Regel für mögliche Dubletten: gleiches Geburtsdatum und ähnlicher normalisierter Name
/// (gleiche Wörter in anderer Reihenfolge oder höchstens zwei Buchstaben Unterschied).
/// </summary>
public static class Dublettenregel
{
    public const int MaxAbstand = 2;

    /// <summary>Begründung, warum zwei normalisierte Namen ähnlich sind, sonst <c>null</c>.</summary>
    public static string? Aehnlichkeit(string normalisiertA, string normalisiertB)
    {
        if (string.IsNullOrEmpty(normalisiertA) || string.IsNullOrEmpty(normalisiertB))
        {
            return null;
        }

        if (normalisiertA == normalisiertB)
        {
            return "gleicher Name (ggf. andere Schreibweise)";
        }

        var a = normalisiertA.Split(' ').Order().ToArray();
        var b = normalisiertB.Split(' ').Order().ToArray();
        if (a.SequenceEqual(b))
        {
            return "Vor- und Nachname vertauscht";
        }

        var abstand = Suchtext.Abstand(normalisiertA, normalisiertB);
        return abstand <= MaxAbstand ? $"ähnlicher Name ({abstand} Buchstabe{(abstand == 1 ? "" : "n")} Unterschied)" : null;
    }

    public static string Normalform(string vorname, string nachname) => Suchtext.Normalisieren($"{nachname} {vorname}");
}

public sealed record DublettenKandidat(Guid Id, string Anzeigename, DateOnly? Geburtsdatum, string? Wohnort, string Grund);

public sealed record DublettenPaar(Guid A, Guid B, string Grund);

public interface IDublettenSuche
{
    /// <summary>Mögliche Dubletten zu einem (neuen oder vorhandenen) Namen mit Geburtsdatum.</summary>
    Task<IReadOnlyList<DublettenKandidat>> KandidatenAsync(string vorname, string nachname, DateOnly? geburtsdatum, Guid? ausser = null,
        CancellationToken abbruch = default);

    /// <summary>Alle Paare möglicher Dubletten (ohne als "keine Dublette" markierte Paare).</summary>
    Task<IReadOnlyList<DublettenPaar>> PaareAsync(CancellationToken abbruch = default);
}

/// <summary>Festgehaltene Entscheidung: Zwei ähnliche Datensätze sind verschiedene Personen.</summary>
public sealed class DublettenAusschluss
{
    private DublettenAusschluss()
    {
        Von = string.Empty;
    }

    public DublettenAusschluss(Guid personA, Guid personB, DateTimeOffset am, string von)
    {
        (PersonA, PersonB) = personA.CompareTo(personB) < 0 ? (personA, personB) : (personB, personA);
        Am = am;
        Von = von;
    }

    public Guid PersonA { get; private set; }

    public Guid PersonB { get; private set; }

    public DateTimeOffset Am { get; private set; }

    public string Von { get; private set; }
}
