namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Fehlertolerante Bewertung für Personenauswahl und Schnellsuche (QA-60, aus SchoolCraft FA-3227): vergleicht Suchbegriffe mit Namen so, wie
/// Menschen tippen – Groß-/Kleinschreibung, Umlaute (ä = ae = a), ß/ss, Akzente und Umschriften gleich (<see cref="Suchtext"/>), Wortanfänge
/// („Bren“ → Brenner) und kleine Tippfehler (ein vertauschter, fehlender oder falscher Buchstabe, ab 8 Buchstaben zwei). Jedes Wort der Eingabe
/// muss zu einem Wort des Namens passen. Rein und deterministisch; die Datenbank liefert die Kandidaten (Trigramm-Index), hier wird bewertet.
/// </summary>
public static class Unscharf
{
    /// <summary>Bewertung 0 (kein Treffer) bis 100 (genau gleich); je besser, desto weiter oben.</summary>
    public static int Bewertung(string? eingabe, string? text)
    {
        var suche = Suchtext.Woerter(eingabe);
        if (suche.Count == 0)
        {
            return 0;
        }

        var woerter = Suchtext.Woerter(text);
        if (woerter.Count == 0)
        {
            return 0;
        }

        var gesamt = 0;
        foreach (var s in suche)
        {
            var beste = woerter.Max(w => Wortbewertung(s, w));
            if (beste == 0)
            {
                return 0;
            }

            gesamt += beste;
        }

        return gesamt / suche.Count;
    }

    /// <summary>Ob <paramref name="text"/> zur Eingabe passt.</summary>
    public static bool Passt(string? eingabe, string? text) => Bewertung(eingabe, text) > 0;

    /// <summary>Kandidaten nach Bewertung sortiert (bei Gleichstand nach DIN 5007-2), nur Treffer.</summary>
    public static IReadOnlyList<T> Ordnen<T>(string? eingabe, IEnumerable<T> kandidaten, Func<T, string> text, int hoechstens = 20) =>
        kandidaten.Select(k => (k, b: Bewertung(eingabe, text(k)))).Where(x => x.b > 0)
            .OrderByDescending(x => x.b).ThenBy(x => text(x.k), Comparer<string>.Create(Din5007.Vergleichen)).Take(hoechstens).Select(x => x.k).ToList();

    /// <summary>Höchstens so viele Tippfehler je Wort: einer, ab 8 Buchstaben zwei, unter 3 Buchstaben keiner.</summary>
    public static int ErlaubteFehler(string wort) => wort.Length < 3 ? 0 : wort.Length >= 8 ? 2 : 1;

    private static int Wortbewertung(string s, string w)
    {
        if (s == w)
        {
            return 100;
        }

        if (w.StartsWith(s, StringComparison.Ordinal))
        {
            return 80 + Math.Min(15, 15 * s.Length / w.Length);
        }

        if (s.Length >= 3 && w.Contains(s, StringComparison.Ordinal))
        {
            return 60;
        }

        var erlaubt = ErlaubteFehler(s);
        if (erlaubt == 0)
        {
            return 0;
        }

        // Tippfehler: gegen das ganze Wort und gegen den gleich langen Anfang (Wortanfang mit Fehler: „Brnner“, „Hartm“)
        var abstand = Math.Min(Abstand(s, w), w.Length > s.Length ? Abstand(s, w[..s.Length]) : int.MaxValue);
        return abstand <= erlaubt ? 50 - (10 * abstand) : 0;
    }

    /// <summary>Damerau-Levenshtein (eingeschränkt): Einfügen, Löschen, Ersetzen, Vertauschen zweier Nachbarn.</summary>
    public static int Abstand(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var kosten = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + kosten);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }
}
