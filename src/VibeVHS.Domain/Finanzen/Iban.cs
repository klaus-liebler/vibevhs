using System.Numerics;
using System.Text;

namespace VibeVHS.Domain.Finanzen;

/// <summary>IBAN prüfen (Länge, Prüfziffer nach ISO 13616 / Modulo 97) und lesbar formatieren.</summary>
public static class Iban
{
    private static readonly Dictionary<string, int> Laengen = new()
    {
        ["DE"] = 22, ["AT"] = 20, ["CH"] = 21, ["NL"] = 18, ["BE"] = 16, ["FR"] = 27, ["IT"] = 27, ["ES"] = 24, ["PL"] = 28, ["LU"] = 20, ["DK"] = 18,
    };

    public static string Normalisieren(string? iban) => new string((iban ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>Leer bei gültiger IBAN, sonst eine verständliche Begründung.</summary>
    public static string? Fehler(string? eingabe)
    {
        var iban = Normalisieren(eingabe);
        if (iban.Length < 15 || iban.Length > 34 || !char.IsLetter(iban[0]) || !char.IsLetter(iban[1]) || !char.IsDigit(iban[2]) || !char.IsDigit(iban[3]))
        {
            return "Eine IBAN beginnt mit dem Ländercode und zwei Prüfziffern, z. B. DE12 3456 …";
        }

        if (Laengen.TryGetValue(iban[..2], out var laenge) && iban.Length != laenge)
        {
            return $"Eine IBAN aus {iban[..2]} hat {laenge} Zeichen, eingegeben wurden {iban.Length}.";
        }

        return Rest(iban) == 1 ? null : "Die Prüfziffer passt nicht – bitte die IBAN vergleichen (Zahlendreher?).";
    }

    public static bool Gueltig(string? iban) => Fehler(iban) is null;

    /// <summary>Gruppiert in Vierergruppen: DE12 3456 7890 …</summary>
    public static string Anzeige(string? iban)
    {
        var n = Normalisieren(iban);
        return string.Join(' ', Enumerable.Range(0, (n.Length + 3) / 4).Select(i => n.Substring(i * 4, Math.Min(4, n.Length - (i * 4)))));
    }

    /// <summary>Für Listen: nur die letzten vier Stellen.</summary>
    public static string Maskiert(string? iban)
    {
        var n = Normalisieren(iban);
        return n.Length < 8 ? "–" : $"{n[..2]}•• •••• {n[^4..]}";
    }

    /// <summary>Bildet eine gültige deutsche IBAN aus Bankleitzahl und Kontonummer (für Testdaten).</summary>
    public static string AusBlzUndKonto(string blz, string konto)
    {
        var bban = blz.PadLeft(8, '0') + konto.PadLeft(10, '0');
        var pruef = 98 - Rest(bban + "DE00");
        return $"DE{pruef:00}{bban}";
    }

    private static int Rest(string iban)
    {
        var umgestellt = iban.Length >= 4 && char.IsLetter(iban[0]) ? iban[4..] + iban[..4] : iban;
        var ziffern = new StringBuilder();
        foreach (var c in umgestellt)
        {
            ziffern.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : c.ToString());
        }

        return (int)(BigInteger.Parse(ziffern.ToString(), System.Globalization.CultureInfo.InvariantCulture) % 97);
    }
}
