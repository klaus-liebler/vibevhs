using System.Text;
using Microsoft.AspNetCore.Components;

namespace VibeVHS.Web.Components;

/// <summary>
/// Rücksprung von Detailseiten zur Liste mit demselben Zustand: Listen schreiben Suche und Filter in ihre URL, Detail-Links
/// tragen diese URL als <c>?zurueck=</c>. Angenommen werden nur eigene, basisrelative Pfade (kein Open Redirect).
/// </summary>
public static class Ruecksprung
{
    public const string Parameter = "zurueck";

    /// <summary>Basisrelatives, sicheres Ziel (ohne führenden Schrägstrich) oder null.</summary>
    public static string? Sicher(string? ziel)
    {
        if (string.IsNullOrWhiteSpace(ziel) || ziel.Length > 2000)
        {
            return null;
        }

        var z = ziel.Trim();
        if (z.StartsWith('/') && !z.StartsWith("//", StringComparison.Ordinal))
        {
            z = z[1..];
        }

        var pfad = z.Split('?', '#')[0];
        // Nur eigene Pfade: beginnt mit Buchstabe, kein Schema („javascript:“, „https:“), kein „//“, kein Backslash,
        // keine Steuerzeichen, kein Zurückgehen aus der Anwendung („..“).
        if (pfad.Length == 0 || !char.IsAsciiLetter(pfad[0]) || pfad.Contains(':', StringComparison.Ordinal)
            || pfad.Contains("//", StringComparison.Ordinal) || z.Contains('\\', StringComparison.Ordinal) || pfad.Contains("..", StringComparison.Ordinal)
            || z.Any(char.IsControl))
        {
            return null;
        }

        return pfad.StartsWith("anmelden", StringComparison.OrdinalIgnoreCase) || pfad.StartsWith("abmelden", StringComparison.OrdinalIgnoreCase) ? null : z;
    }

    /// <summary>Link auf eine Detailseite, der den Rückweg zur aktuellen Liste mitnimmt.</summary>
    public static string Detail(string detailPfad, string listenUrl)
        => $"{detailPfad}{(detailPfad.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{Parameter}={Uri.EscapeDataString(listenUrl)}";

    /// <summary>Basisrelative URL der aktuellen Seite (z. B. „kurse?fb=3“).</summary>
    public static string Aktuell(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return navigation.ToBaseRelativePath(navigation.Uri);
    }

    /// <summary>Listen-URL aus Pfad und Zustandswerten; leere Werte entfallen, damit die Adresse kurz und teilbar bleibt.</summary>
    public static string Url(string pfad, params (string Name, string? Wert)[] werte)
    {
        var sb = new StringBuilder(pfad);
        var erstes = true;
        foreach (var (name, wert) in werte)
        {
            if (string.IsNullOrEmpty(wert))
            {
                continue;
            }

            sb.Append(erstes ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(wert));
            erstes = false;
        }

        return sb.ToString();
    }

    /// <summary>Schreibt den Listenzustand in die Adresszeile, ohne neuen Verlaufseintrag (Browser-Zurück bleibt sinnvoll).</summary>
    public static void ZustandSetzen(NavigationManager navigation, string url)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        if (!string.Equals(Aktuell(navigation), url, StringComparison.Ordinal))
        {
            navigation.NavigateTo(url, new NavigationOptions { ReplaceHistoryEntry = true });
        }
    }
}
