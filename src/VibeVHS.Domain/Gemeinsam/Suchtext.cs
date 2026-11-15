using System.Globalization;
using System.Text;

namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Normalisierte Schreibweise für Suche und Dublettenprüfung: Groß/klein, Akzente und Umschriften
/// werden vereinheitlicht ("Yılmaz" = "Yilmaz", "Müller" = "Mueller" = "Muller", "Straße" = "Strasse").
/// Die Normalisierung wird auf gespeicherte Texte und auf Suchbegriffe gleich angewendet.
/// </summary>
public static class Suchtext
{
    public static string Normalisieren(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var s = text.Trim().ToLower(CultureInfo.InvariantCulture)
            .Replace("ß", "ss", StringComparison.Ordinal)
            .Replace("ı", "i", StringComparison.Ordinal)
            .Replace("ł", "l", StringComparison.Ordinal)
            .Replace("ø", "o", StringComparison.Ordinal)
            .Replace("æ", "ae", StringComparison.Ordinal)
            .Replace("œ", "oe", StringComparison.Ordinal)
            .Replace("đ", "d", StringComparison.Ordinal);

        var zerlegt = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(zerlegt.Length);
        foreach (var c in zerlegt)
        {
            var kategorie = CharUnicodeInfo.GetUnicodeCategory(c);
            if (kategorie == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // Bindestriche, Apostrophe und Satzzeichen trennen Wörter.
            sb.Append(char.IsLetterOrDigit(c) || c == '@' || c == '.' ? c : ' ');
        }

        // Deutsche Umschrift: ä/ae, ö/oe, ü/ue werden gleich behandelt.
        var ergebnis = sb.ToString().Normalize(NormalizationForm.FormC)
            .Replace("ae", "a", StringComparison.Ordinal)
            .Replace("oe", "o", StringComparison.Ordinal)
            .Replace("ue", "u", StringComparison.Ordinal);

        return string.Join(' ', ergebnis.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Wörter eines Suchbegriffs in normalisierter Form.</summary>
    public static IReadOnlyList<string> Woerter(string? text)
        => Normalisieren(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

    /// <summary>Levenshtein-Abstand zweier Texte (für ähnliche Namen in der Dublettenprüfung).</summary>
    public static int Abstand(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var vorher = new int[b.Length + 1];
        var aktuell = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            vorher[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            aktuell[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var kosten = a[i - 1] == b[j - 1] ? 0 : 1;
                aktuell[j] = Math.Min(Math.Min(aktuell[j - 1] + 1, vorher[j] + 1), vorher[j - 1] + kosten);
            }

            (vorher, aktuell) = (aktuell, vorher);
        }

        return vorher[b.Length];
    }
}
