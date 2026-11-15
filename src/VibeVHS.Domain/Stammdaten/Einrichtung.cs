namespace VibeVHS.Domain.Stammdaten;

/// <summary>
/// Die Volkshochschule selbst: Briefkopf-Stammdaten (Name, Kurzname, Orte, Bundesland).
/// Es gibt genau eine Einrichtung je Installation.
/// </summary>
public sealed class Einrichtung
{
    /// <summary>Fester Schlüssel des einzigen Datensatzes.</summary>
    public const int EinzigeId = 1;

    private Einrichtung()
    {
        Name = string.Empty;
        Kurzname = string.Empty;
        Bundesland = string.Empty;
    }

    public Einrichtung(string name, string kurzname, IEnumerable<string> orte, string bundesland)
    {
        Id = EinzigeId;
        Aktualisieren(name, kurzname, orte, bundesland);
        Name = name.Trim();
        Kurzname = kurzname.Trim();
        Bundesland = bundesland.Trim();
    }

    public int Id { get; private set; }

    public string Name { get; private set; }

    public string Kurzname { get; private set; }

    /// <summary>Standorte (z. B. Emsdetten, Greven, Saerbeck).</summary>
    public List<string> Orte { get; private set; } = [];

    public string Bundesland { get; private set; }

    /// <summary>Logo für Briefe und Aushänge (PNG oder JPEG, höchstens <see cref="LogoMaxBytes"/>).</summary>
    public byte[]? Logo { get; private set; }

    /// <summary>Website für Fußzeilen (z. B. „www.vhs-egs.de“).</summary>
    public string? Website { get; private set; }

    public const int LogoMaxBytes = 512 * 1024;

    /// <summary>Setzt oder entfernt (null) das Logo. Nur PNG oder JPEG.</summary>
    public void LogoSetzen(byte[]? bild)
    {
        if (bild is { Length: > 0 })
        {
            if (bild.Length > LogoMaxBytes)
            {
                throw new ArgumentException($"Das Logo ist zu groß (höchstens {LogoMaxBytes / 1024} KB).", nameof(bild));
            }

            if (!IstPng(bild) && !IstJpeg(bild))
            {
                throw new ArgumentException("Das Logo muss eine PNG- oder JPEG-Datei sein.", nameof(bild));
            }
        }

        Logo = bild is { Length: > 0 } ? bild : null;
    }

    public void WebsiteSetzen(string? website) => Website = string.IsNullOrWhiteSpace(website) ? null : website.Trim();

    private static bool IstPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    private static bool IstJpeg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    /// <summary>"Emsdetten · Greven · Saerbeck (NRW)"</summary>
    public string OrteAnzeige => string.Join(" · ", Orte) + (Bundesland.Length > 0 ? $" ({Bundesland})" : string.Empty);

    public void Aktualisieren(string name, string kurzname, IEnumerable<string> orte, string bundesland)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(kurzname);
        ArgumentNullException.ThrowIfNull(orte);
        Name = name.Trim();
        Kurzname = kurzname.Trim();
        Orte = orte.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();
        Bundesland = bundesland?.Trim() ?? string.Empty;
    }
}
