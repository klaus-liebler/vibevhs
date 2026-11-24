using System.Text.RegularExpressions;

namespace VibeVHS.Web.Tests;

/// <summary>
/// Bedienregeln, die sich am Quelltext der Oberfläche prüfen lassen (aus SchoolCraft übernommen, docs/rueckport-schoolcraft.md):
/// Rückfrage vor folgenreichen Aktionen (QA-58), Tastenkürzel als &lt;kbd&gt; (QA-59).
/// </summary>
public sealed partial class BedienregelnTests
{
    private static string Wurzel([System.Runtime.CompilerServices.CallerFilePath] string quelle = "")
    {
        var verzeichnis = new DirectoryInfo(Path.GetDirectoryName(quelle)!);
        while (verzeichnis is not null && !File.Exists(Path.Combine(verzeichnis.FullName, "VibeVHS.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName ?? throw new InvalidOperationException("Repository nicht gefunden");
    }

    private static IEnumerable<(string Datei, string Inhalt)> Razor()
        => Directory.EnumerateFiles(Path.Combine(Wurzel(), "src", "VibeVHS.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Select(d => (Path.GetRelativePath(Wurzel(), d), File.ReadAllText(d)));

    /// <summary>Folgenreiche Wörter: was sich nicht mit einem Klick rückgängig machen lässt oder andere betrifft.</summary>
    [GeneratedRegex(@"(?i)\b(löschen|entfernen|stornieren|verwerfen|widerrufen|sperren|zurückziehen)\b")]
    private static partial Regex Folgenreich();

    [GeneratedRegex(@"<BegruendeterButton\b[^>]*?/>", RegexOptions.Singleline)]
    private static partial Regex Schaltflaeche();

    [GeneratedRegex(@"\bText=""([^""]*)""")]
    private static partial Regex Text();

    [GeneratedRegex(@"<form\b[^>]*>(?:(?!</form>).)*?<button[^>]*type=""submit""[^>]*>([^<]*)</button>", RegexOptions.Singleline)]
    private static partial Regex Formular();

    [Fact]
    public void Folgenreiche_Schaltflaechen_fragen_vorher_nach()
    {
        var ohne = new List<string>();
        foreach (var (datei, inhalt) in Razor())
        {
            foreach (Match m in Schaltflaeche().Matches(inhalt))
            {
                var text = Text().Match(m.Value).Groups[1].Value;
                if (Folgenreich().IsMatch(text) && !m.Value.Contains("Rueckfrage=", StringComparison.Ordinal))
                {
                    ohne.Add($"{datei}: „{text}“");
                }
            }

            foreach (Match f in Formular().Matches(inhalt))
            {
                if (Folgenreich().IsMatch(f.Groups[1].Value) && !f.Value.Contains("data-rueckfrage", StringComparison.Ordinal))
                {
                    ohne.Add($"{datei}: Formular „{f.Groups[1].Value.Trim()}“");
                }
            }
        }

        ohne.ShouldBeEmpty("Folgenreiche Aktionen ohne Rückfrage (BegruendeterButton Rueckfrage=… bzw. form data-rueckfrage=…)");
    }

    /// <summary>Tastenhinweise im Fließtext (außerhalb von &lt;kbd&gt;, Attributen und Code).</summary>
    [GeneratedRegex(@"(?i:\b(strg|alt|umschalt)\s*\+|\b(tab|enter|esc|eingabe)-?taste\b)|\bF([1-9]|1[0-2])\b")]
    private static partial Regex Tastenhinweis();

    [GeneratedRegex(@"<kbd>.*?</kbd>|`[^`]*`|""[^""\n]*""|<code>.*?</code>", RegexOptions.Singleline)]
    private static partial Regex Ausgenommen();

    [Fact]
    public void Tastenkuerzel_stehen_in_kbd()
    {
        var dateien = Razor().Select(r => r.Datei)
            .Append(Path.Combine("docs", "hilfe.md"))
            .Concat(Directory.EnumerateFiles(Path.Combine(Wurzel(), "src", "VibeVHS.Web", "wwwroot", "touren"), "*.json").Select(d => Path.GetRelativePath(Wurzel(), d)));
        var funde = new List<string>();
        foreach (var datei in dateien)
        {
            var text = Ausgenommen().Replace(File.ReadAllText(Path.Combine(Wurzel(), datei)), " ");
            foreach (Match m in Tastenhinweis().Matches(text))
            {
                funde.Add($"{datei}: „{m.Value}“");
            }
        }

        funde.ShouldBeEmpty("Tastenkürzel bitte als <kbd>…</kbd> schreiben (QA-59)");
    }
}
