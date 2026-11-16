namespace VibeVHS.Domain.Personen;

/// <summary>
/// Namensvorsätze („von“, „van der“, „zu“ …) und Namenszusätze („jun.“, „sen.“). Der Vorsatz gehört in der Anzeige und Anrede zum Nachnamen
/// („Frau von Berg“), wird bei der Sortierung aber nachgestellt (Telefonbuch-Ordnung nach DIN 5007-2: „Berg, Anna von“).
/// </summary>
public static class Namensvorsatz
{
    /// <summary>Bekannte Vorsätze, längste zuerst (damit „von der“ vor „von“ erkannt wird).</summary>
    public static IReadOnlyList<string> Bekannte { get; } =
    [
        "von und zu", "von der", "von dem", "van der", "van den", "van de", "op den", "von", "vom", "zu", "zur", "van", "de", "ten", "ter",
    ];

    /// <summary>Bekannte Namenszusätze (nachgestellt).</summary>
    public static IReadOnlyList<string> Zusaetze { get; } = ["jun.", "sen.", "jr.", "sr.", "II", "III"];

    /// <summary>
    /// Vorsatz und Zusatz aus Vor- und Nachname herauslösen: „Anna von“ + „Berg“ oder „Anna“ + „von Berg jun.“ → („Anna“, „von“, „Berg“, „jun.“).
    /// Ein Vorsatz wird nur erkannt, wenn danach noch ein Nachname folgt.
    /// </summary>
    public static (string Vorname, string Vorsatz, string Nachname, string Zusatz) Zerlegen(string? vorname, string nachname)
    {
        var v = (vorname ?? "").Trim();
        var n = nachname.Trim();
        var zusatz = "";
        foreach (var z in Zusaetze)
        {
            if (n.EndsWith(" " + z, StringComparison.OrdinalIgnoreCase))
            {
                zusatz = n[^z.Length..];
                n = n[..^(z.Length + 1)].TrimEnd();
                break;
            }
        }

        foreach (var vs in Bekannte)
        {
            if (n.StartsWith(vs + " ", StringComparison.OrdinalIgnoreCase) && n.Length > vs.Length + 1)
            {
                return (v, n[..vs.Length], n[(vs.Length + 1)..].Trim(), zusatz);
            }

            if (v.EndsWith(" " + vs, StringComparison.OrdinalIgnoreCase))
            {
                return (v[..^(vs.Length + 1)].TrimEnd(), v[^vs.Length..], n, zusatz);
            }
        }

        return (v, "", n, zusatz);
    }
}
