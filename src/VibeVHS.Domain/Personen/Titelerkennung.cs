using System.Text.RegularExpressions;

namespace VibeVHS.Domain.Personen;

/// <summary>
/// Akademische Grade und Titel, die versehentlich im Vor- oder Nachnamen stehen („Dr. Kai“, „Prof. Dr. Lüken“), erkennen und abtrennen.
/// Erkannt werden nur führende Teile, die wie ein Titel aussehen (Dr., Prof., Dipl.-Ing., Dr. med., Mag., PD, Priv.-Doz. …),
/// damit Namen wie „St. John“ oder Initialen („J. R.“) nicht fälschlich zerlegt werden.
/// </summary>
public static partial class Titelerkennung
{
    /// <summary>Titel abtrennen: zuerst vom Anfang des Vornamens, sonst vom Anfang des Nachnamens. Ohne Titel unverändert.</summary>
    public static (string? Titel, string Vorname, string Nachname) Abtrennen(string? vorname, string? nachname)
    {
        var (t1, v) = Fuehrend(vorname ?? "");
        var (t2, n) = Fuehrend(nachname ?? "");
        var titel = string.Join(' ', new[] { t1, t2 }.Where(t => t is not null));
        // Nachname darf nicht leer werden („Dr.“ allein ist kein Name).
        return n.Length == 0 && t2 is not null
            ? (t1, v, (nachname ?? "").Trim())
            : (titel.Length == 0 ? null : titel, v, n);
    }

    /// <summary>Erkannter Titel im Vor- oder Nachnamen (für den Hinweis „Als Titel übernehmen?“), sonst <c>null</c>.</summary>
    public static string? Erkannt(string? vorname, string? nachname) => Abtrennen(vorname, nachname).Titel;

    /// <summary>Titel zusammenführen (vorhandenen behalten, erkannten anhängen, ohne Doppelung).</summary>
    public static string? Zusammen(string? vorhanden, string? erkannt)
    {
        var teile = new List<string>();
        foreach (var t in $"{vorhanden} {erkannt}".Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!teile.Contains(t, StringComparer.OrdinalIgnoreCase))
            {
                teile.Add(t);
            }
        }

        return teile.Count == 0 ? null : string.Join(' ', teile);
    }

    private static (string? Titel, string Uebrig) Fuehrend(string text)
    {
        var teile = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var titel = new List<string>();
        while (teile.Count > 0 && IstTitel(teile[0]))
        {
            titel.Add(teile[0]);
            teile.RemoveAt(0);
        }

        return (titel.Count == 0 ? null : string.Join(' ', titel), string.Join(' ', teile));
    }

    internal static bool IstTitel(string teil) => TitelMuster().IsMatch(teil);

    [GeneratedRegex(@"^(Prof\.|Dr\.|Dres\.|Dr\.-Ing\.|Dr\.\w+\.|med\.|rer\.|nat\.|phil\.|jur\.|oec\.|pol\.|dent\.|vet\.|theol\.|habil\.|h\.\s?c\.|mult\.|Dipl\.-?[\wÄÖÜäöü]*\.?|Mag\.|PD|Priv\.-Doz\.|Ing\.|B\.(A|Sc|Eng)\.|M\.(A|Sc|Eng|Ed)\.)$", RegexOptions.IgnoreCase)]
    private static partial Regex TitelMuster();
}
