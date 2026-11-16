using System.Globalization;
using System.Text;

namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Sortierung nach DIN 5007-2 („Telefonbuch“): ä→ae, ö→oe, ü→ue, ß→ss, übrige Akzente ohne Zeichen (é→e), Groß-/Kleinschreibung egal.
/// In SQLite als Collation „DIN5007_2“ registriert (Spalten Nachname/Vorname der Namensversionen); Namensvorsätze („von“) stehen in
/// einer eigenen Spalte und zählen für die Sortierung nicht.
/// </summary>
public static class Din5007
{
    public const string Collation = "DIN5007_2";

    public static string Schluessel(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length + 4);
        foreach (var z in text.ToLowerInvariant())
        {
            switch (z)
            {
                case 'ä': sb.Append("ae"); break;
                case 'ö': sb.Append("oe"); break;
                case 'ü': sb.Append("ue"); break;
                case 'ß': sb.Append("ss"); break;
                default: sb.Append(z); break;
            }
        }

        var zerlegt = sb.ToString().Normalize(NormalizationForm.FormD);
        sb.Clear();
        foreach (var z in zerlegt.Where(z => CharUnicodeInfo.GetUnicodeCategory(z) != UnicodeCategory.NonSpacingMark))
        {
            sb.Append(z);
        }

        return sb.ToString();
    }

    public static int Vergleichen(string? a, string? b)
    {
        var v = string.CompareOrdinal(Schluessel(a), Schluessel(b));
        return v;
    }
}
