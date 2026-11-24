using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace VibeVHS.Domain.Vorlagen;

/// <summary>Fester Rahmen eines Rundschreibens (QA-66, aus SchoolCraft FA-822): gleiche Darstellung in Mail und Vorschau, nur Inline-Stile.</summary>
public enum Gestaltung
{
    Schlicht,
    Briefkopf,
    Winter,
    Fruehling,
    Sommer,
}

/// <summary>Angaben für den Briefkopf (aus den Einstellungen der Einrichtung).</summary>
public sealed record Kopfdaten(string Name, string? Anschrift, string? Website);

public static class Briefgestaltung
{
    public static string Anzeige(Gestaltung g) => g switch
    {
        Gestaltung.Briefkopf => "mit Briefkopf",
        Gestaltung.Winter => "Winter",
        Gestaltung.Fruehling => "Frühling",
        Gestaltung.Sommer => "Sommer",
        _ => "schlicht",
    };

    /// <summary>Bereinigter Inhalt im Rahmen der Gestaltung (vollständiges, eigenständiges HTML-Fragment mit Inline-Stilen).</summary>
    public static string Rahmen(string inhaltHtml, Gestaltung gestaltung, Kopfdaten kopf, string? fussHtml = null)
    {
        var (farbe, hintergrund, zeichen) = gestaltung switch
        {
            Gestaltung.Winter => ("#1f4e79", "#eef4fb", "❄ "),
            Gestaltung.Fruehling => ("#2e7d32", "#f1f8e9", "✿ "),
            Gestaltung.Sommer => ("#b45309", "#fff7e6", "☀ "),
            _ => ("#1f2937", "#ffffff", ""),
        };
        var sb = new StringBuilder();
        sb.Append($"<div style=\"font-family: Arial, Helvetica, sans-serif; font-size: 15px; line-height: 1.5; color: #1f2937; background-color: {hintergrund}; padding: 20px;\">");
        sb.Append("<div style=\"max-width: 640px; margin: 0 auto; background-color: #ffffff; padding: 24px; border-radius: 8px;");
        sb.Append(gestaltung == Gestaltung.Schlicht ? "\">" : $" border-top: 6px solid {farbe};\">");
        if (gestaltung != Gestaltung.Schlicht)
        {
            sb.Append($"<div style=\"color: {farbe}; font-weight: bold; font-size: 18px; margin-bottom: 4px;\">{zeichen}{WebUtility.HtmlEncode(kopf.Name)}</div>");
            if (gestaltung == Gestaltung.Briefkopf && (kopf.Anschrift is not null || kopf.Website is not null))
            {
                sb.Append($"<div style=\"color: #6b7280; font-size: 12px; margin-bottom: 16px;\">{WebUtility.HtmlEncode(string.Join(" · ", new[] { kopf.Anschrift, kopf.Website }.Where(x => !string.IsNullOrWhiteSpace(x))))}</div>");
            }
            else
            {
                sb.Append("<div style=\"margin-bottom: 16px;\"></div>");
            }
        }

        sb.Append(inhaltHtml);
        if (fussHtml is not null)
        {
            sb.Append($"<div style=\"margin-top: 24px; padding-top: 12px; border-top: 1px solid #e5e7eb; color: #6b7280; font-size: 12px;\">{fussHtml}</div>");
        }

        sb.Append("</div></div>");
        return sb.ToString();
    }
}

/// <summary>
/// Bereinigt HTML aus dem Editor (QA-66, aus SchoolCraft FA-821): nur eine Whitelist an Elementen, keine Skripte, Ereignisse oder fremden
/// Einbettungen; Links nur http, https und mailto; Stile nur Ausrichtung, Farbe und Schriftschnitt.
/// </summary>
public static partial class HtmlBereinigung
{
    private static readonly HashSet<string> Erlaubt =
        ["p", "br", "b", "strong", "i", "em", "u", "s", "ul", "ol", "li", "h2", "h3", "h4", "blockquote", "a", "span", "div", "hr"];

    private static readonly HashSet<string> Stile = ["text-align", "color", "background-color", "font-weight", "font-style", "text-decoration"];

    [GeneratedRegex(@"<(script|style|iframe|object|embed|noscript|template)[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Gefaehrlich();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Kommentar();

    [GeneratedRegex(@"<\s*(/?)\s*([a-zA-Z0-9]+)([^>]*)>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"href\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    [GeneratedRegex(@"style\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex Stil();

    [GeneratedRegex(@"^[#a-zA-Z0-9(),.%\s-]+$")]
    private static partial Regex Stilwert();

    public static string Bereinigen(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var s = Gefaehrlich().Replace(html, string.Empty);
        s = Kommentar().Replace(s, string.Empty);
        return Tag().Replace(s, m =>
        {
            var zu = m.Groups[1].Value == "/";
            var name = m.Groups[2].Value.ToLowerInvariant();
            name = name == "h1" ? "h2" : name;
            if (!Erlaubt.Contains(name))
            {
                return string.Empty;
            }

            if (zu)
            {
                return $"</{name}>";
            }

            var attr = new StringBuilder();
            if (name == "a" && Href().Match(m.Groups[3].Value) is { Success: true } h)
            {
                var url = WebUtility.HtmlDecode(h.Groups[2].Success ? h.Groups[2].Value : h.Groups[3].Value).Trim();
                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    attr.Append($" href=\"{WebUtility.HtmlEncode(url)}\" rel=\"noopener\" target=\"_blank\"");
                }
            }

            if (Stil().Match(m.Groups[3].Value) is { Success: true } st)
            {
                var gute = (st.Groups[2].Success ? st.Groups[2].Value : st.Groups[3].Value).Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Split(':', 2))
                    .Where(d => d.Length == 2 && Stile.Contains(d[0].Trim().ToLowerInvariant()) && Stilwert().IsMatch(d[1].Trim())
                                && !d[1].Contains("url", StringComparison.OrdinalIgnoreCase) && !d[1].Contains("expression", StringComparison.OrdinalIgnoreCase))
                    .Select(d => $"{d[0].Trim().ToLowerInvariant()}: {d[1].Trim()}").ToList();
                if (gute.Count > 0)
                {
                    attr.Append($" style=\"{string.Join("; ", gute)}\"");
                }
            }

            return $"<{name}{attr}>";
        });
    }

    /// <summary>Reiner Text (Textteil der Mail, Vorschau im Postausgang).</summary>
    public static string AlsText(string html)
    {
        var s = Regex.Replace(html, @"<\s*(br|/p|/li|/h[1-4]|/div|/blockquote)\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*li[^>]*>", "• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", string.Empty);
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t]+\n", "\n");
        return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
    }
}

/// <summary>Platzhalter in Rundschreiben: {Anrede}, {Vorname}, {Nachname}, {Name} – Werte werden für HTML maskiert.</summary>
public static class Rundschreibenplatzhalter
{
    public static readonly IReadOnlyList<string> Alle = ["Anrede", "Vorname", "Nachname", "Name"];

    public static string Ersetzen(string text, IReadOnlyDictionary<string, string> werte, bool html)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var (k, v) in werte)
        {
            text = text.Replace("{" + k + "}", html ? WebUtility.HtmlEncode(v) : v, StringComparison.Ordinal);
        }

        return text;
    }
}
