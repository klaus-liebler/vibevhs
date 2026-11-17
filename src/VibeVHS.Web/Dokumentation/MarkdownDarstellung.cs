using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace VibeVHS.Web.Dokumentation;

/// <summary>Ein Abschnitt eines Dokuments: Überschrift (Ebene 1–3) und der Inhalt bis zur nächsten.</summary>
/// <param name="Id">Anker der Überschrift (GitHub-Stil, eindeutig im Dokument); leer für den Text vor der ersten Überschrift.</param>
/// <param name="Ebene">1–3; 0 für den Text vor der ersten Überschrift.</param>
/// <param name="TitelHtml">Überschrift als HTML (Hervorhebungen, Code).</param>
/// <param name="Titel">Überschrift als reiner Text (Inhaltsverzeichnis, Kommentare).</param>
public sealed record Abschnitt(string Id, int Ebene, string TitelHtml, string Titel, string InhaltHtml);

/// <summary>
/// Markdown → HTML für Hilfe, Spezifikation, Doku und Impressum (Markdig). Sicher auch für Text aus den Einstellungen:
/// HTML im Markdown wird nicht übernommen (als Text ausgegeben), Links nur http(s), mailto, Anker und relative Pfade.
/// </summary>
public static partial class MarkdownDarstellung
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseListExtras()
        .UseTaskLists()
        .UseAutoLinks()
        .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
        .Build();

    /// <summary>Ganzes Dokument als HTML.</summary>
    /// <param name="linkZiel">Schreibt relative Links um (z. B. „anforderungen.md#x“ → Seite in der Anwendung); null = Link entfernen.</param>
    public static string AlsHtml(string markdown, Func<string, string?>? linkZiel = null)
    {
        var dokument = Parsen(markdown, linkZiel);
        using var schreiber = new StringWriter();
        var renderer = new HtmlRenderer(schreiber);
        Pipeline.Setup(renderer);
        renderer.Render(dokument);
        return schreiber.ToString();
    }

    /// <summary>Zerlegt das Dokument an den Überschriften der Ebenen 1–3 (für Inhaltsverzeichnis und Kommentare je Abschnitt).</summary>
    public static IReadOnlyList<Abschnitt> InAbschnitte(string markdown, Func<string, string?>? linkZiel = null)
    {
        var dokument = Parsen(markdown, linkZiel);
        var abschnitte = new List<Abschnitt>();
        HeadingBlock? kopf = null;
        var bloecke = new List<Block>();

        foreach (var block in dokument)
        {
            if (block is HeadingBlock { Level: <= 3 } h)
            {
                Abschliessen();
                kopf = h;
                bloecke.Clear();
            }
            else
            {
                bloecke.Add(block);
            }
        }

        Abschliessen();
        return abschnitte;

        void Abschliessen()
        {
            if (kopf is null && bloecke.Count == 0)
            {
                return;
            }

            using var inhalt = new StringWriter();
            var renderer = new HtmlRenderer(inhalt);
            Pipeline.Setup(renderer);
            foreach (var b in bloecke)
            {
                renderer.Render(b);
            }

            if (kopf is null)
            {
                abschnitte.Add(new Abschnitt(string.Empty, 0, string.Empty, string.Empty, inhalt.ToString()));
                return;
            }

            using var titel = new StringWriter();
            var titelRenderer = new HtmlRenderer(titel);
            Pipeline.Setup(titelRenderer);
            titelRenderer.WriteLeafInline(kopf);
            var titelHtml = titel.ToString();
            abschnitte.Add(new Abschnitt(kopf.GetAttributes().Id ?? string.Empty, kopf.Level, titelHtml, ReinerText(titelHtml), inhalt.ToString()));
        }
    }

    private static MarkdownDocument Parsen(string markdown, Func<string, string?>? linkZiel)
    {
        var dokument = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        foreach (var link in dokument.Descendants<LinkInline>())
        {
            link.Url = SichererLink(link.Url, linkZiel);
        }

        foreach (var link in dokument.Descendants<AutolinkInline>())
        {
            if (SichererLink(link.Url, null) is null)
            {
                link.Url = string.Empty;
            }
        }

        return dokument;
    }

    /// <summary>Erlaubt http(s), mailto und Anker; relative Pfade gehen über <paramref name="linkZiel"/>; alles andere (javascript:, data: …) entfällt.</summary>
    internal static string? SichererLink(string? url, Func<string, string?>? linkZiel)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var u = url.Trim();
        if (u.StartsWith('#'))
        {
            return u;
        }

        if (Schema().Match(u) is { Success: true } m)
        {
            var schema = m.Groups[1].Value.ToLowerInvariant();
            return schema is "http" or "https" or "mailto" ? u : null;
        }

        // Relativ (ohne Schema): nur über die Umschreibung, damit keine Links ins Leere oder auf fremde Pfade zeigen.
        return linkZiel?.Invoke(u);
    }

    private static string ReinerText(string html) => WebUtility.HtmlDecode(Tags().Replace(html, string.Empty)).Trim();

    [GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.\-]*):")]
    private static partial Regex Schema();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();
}
