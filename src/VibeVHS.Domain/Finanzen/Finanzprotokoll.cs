using System.Security.Cryptography;
using System.Text;

namespace VibeVHS.Domain.Finanzen;

/// <summary>
/// Eintrag im manipulationsgeschützten Änderungsprotokoll für Finanzdaten (FA-128). Jeder Eintrag enthält den Hash
/// seines Vorgängers; wer einen Eintrag ändert oder löscht, bricht die Kette ab dieser Stelle. Inhalte nur mit
/// Pseudonymen, damit die Kette bei einer Anonymisierung nicht angefasst werden muss.
/// </summary>
public sealed class Finanzprotokolleintrag
{
    public const string Anfang = "0000000000000000000000000000000000000000000000000000000000000000";

    private Finanzprotokolleintrag()
    {
        BenutzerKennung = string.Empty;
        Aktion = string.Empty;
        ObjektTyp = string.Empty;
        VorigerHash = Anfang;
        Hash = Anfang;
    }

    public Finanzprotokolleintrag(long nr, DateTimeOffset zeitpunkt, string benutzerKennung, string aktion, string objektTyp, Guid? objektId,
        string? vorher, string? nachher, string vorigerHash)
    {
        Nr = nr;
        // Auf Millisekunden gekürzt: so übersteht der Zeitpunkt die Speicherung unverändert (Prüfsumme bleibt gleich).
        Zeitpunkt = DateTimeOffset.FromUnixTimeMilliseconds(zeitpunkt.ToUnixTimeMilliseconds());
        BenutzerKennung = benutzerKennung;
        Aktion = aktion;
        ObjektTyp = objektTyp;
        ObjektId = objektId;
        Vorher = vorher;
        Nachher = nachher;
        VorigerHash = vorigerHash;
        Hash = Berechnen();
    }

    /// <summary>Fortlaufend ab 1, ohne Lücken.</summary>
    public long Nr { get; private set; }

    public DateTimeOffset Zeitpunkt { get; private set; }

    public string BenutzerKennung { get; private set; }

    public string Aktion { get; private set; }

    public string ObjektTyp { get; private set; }

    public Guid? ObjektId { get; private set; }

    /// <summary>Vorheriger Wert (bei Änderungen), kompakt als Text.</summary>
    public string? Vorher { get; private set; }

    public string? Nachher { get; private set; }

    public string VorigerHash { get; private set; }

    public string Hash { get; private set; }

    public string Berechnen()
    {
        var inhalt = string.Join('|', Nr, Zeitpunkt.ToUnixTimeMilliseconds(), BenutzerKennung, Aktion, ObjektTyp, ObjektId?.ToString("N") ?? "", Vorher ?? "", Nachher ?? "", VorigerHash);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhalt)));
    }

    /// <summary>Prüft die Kette. Ergebnis: null = unversehrt, sonst die erste fehlerhafte Nummer mit Grund.</summary>
    public static (long Nr, string Grund)? KettePruefen(IEnumerable<Finanzprotokolleintrag> eintraege)
    {
        ArgumentNullException.ThrowIfNull(eintraege);
        var erwartet = 1L;
        var voriger = Anfang;
        foreach (var e in eintraege.OrderBy(e => e.Nr))
        {
            if (e.Nr != erwartet)
            {
                return (erwartet, $"Eintrag {erwartet} fehlt (gefunden: {e.Nr}).");
            }

            if (e.VorigerHash != voriger)
            {
                return (e.Nr, "Der Verweis auf den vorigen Eintrag passt nicht – ein früherer Eintrag wurde verändert oder entfernt.");
            }

            if (e.Berechnen() != e.Hash)
            {
                return (e.Nr, "Der Inhalt passt nicht zur Prüfsumme – der Eintrag wurde nachträglich verändert.");
            }

            voriger = e.Hash;
            erwartet++;
        }

        return null;
    }
}
