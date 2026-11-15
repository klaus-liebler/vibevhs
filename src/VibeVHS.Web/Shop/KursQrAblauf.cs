using System.Text.RegularExpressions;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Application.Shop;
using VibeVHS.Web.Sicherheit;

namespace VibeVHS.Web.Shop;

/// <summary>Gemeinsame Abläufe der Schnellanmeldung (Kurs-QR auf dem gemerkten Gerät oder über den persönlichen Link).</summary>
internal static partial class KursQrAblauf
{
    /// <summary>Benutzer im Protokoll für Schnellanmeldungen (keine Rolle).</summary>
    public static readonly Application.Sicherheit.Benutzer Selbst = new("selbstanmeldung", "Schnellanmeldung (Teilnehmende)", null);

    [GeneratedRegex(@"/k/([A-Za-z0-9\-_.]{1,40})/?$")]
    private static partial Regex KursQr();

    /// <summary>Kursnummer aus dem Inhalt eines Kurs-QR-Codes (URL …/k/{nummer}), sonst null.</summary>
    public static string? KursnummerAusQr(string? inhalt)
    {
        if (string.IsNullOrWhiteSpace(inhalt))
        {
            return null;
        }

        var pfad = Uri.TryCreate(inhalt.Trim(), UriKind.Absolute, out var uri) ? uri.AbsolutePath : inhalt.Trim();
        var m = KursQr().Match(pfad);
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null;
    }

    /// <summary>Inhalt des Kurs-QR-Codes: immer die VibeVHS-Adresse /k/{nummer} – leitet je nach Gerät weiter.</summary>
    public static string QrInhalt(OeffentlicheAdresse adresse, string nummer) => adresse.Link($"k/{Uri.EscapeDataString(nummer)}").ToString();

    /// <summary>Kursseite für Besucher ohne gemerktes Gerät (Shop:KursSeite, Platzhalter {nummer} und {slug}).</summary>
    public static string Kursseite(IConfiguration konfiguration, string nummer, string? titel = null)
    {
        var vorlage = konfiguration["Shop:KursSeite"] ?? "shop/kurs.html#kurs={nummer}";
        return vorlage.Replace("{nummer}", Uri.EscapeDataString(nummer), StringComparison.Ordinal)
            .Replace("{slug}", Uri.EscapeDataString(ShopText.Slug(titel is null ? nummer : $"{nummer}-{titel}")), StringComparison.Ordinal);
    }

    public static async Task<(SchnellErgebnis? Ergebnis, string? Fehler)> AusfuehrenAsync(IServiceScopeFactory bereiche, Guid personId, string nummer,
        string? agb, string? datenschutz, string? agbVersion, string? datenschutzVersion, string weg)
    {
        if (agb != "ja" || datenschutz != "ja")
        {
            return (null, "Bitte AGB und Datenschutzhinweise bestätigen.");
        }

        await using var bereich = bereiche.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = Selbst;
        var adresse = bereich.ServiceProvider.GetRequiredService<OeffentlicheAdresse>();
        try
        {
            var e = await bereich.ServiceProvider.GetRequiredService<Schnellanmeldedienst>()
                .AnmeldenAsync(personId, nummer, agbVersion ?? "", datenschutzVersion ?? "", weg, t => adresse.Link($"k/storno/{t}"));
            return (e, null);
        }
        catch (ShopFehler f)
        {
            return (null, f.Message);
        }
    }
}
