using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Stammdaten;

namespace VibeVHS.Application.Stammdaten;

/// <summary>
/// Aktueller Briefkopf (Logo, Website, Name) für PDF-Erzeuger, die als Singleton laufen. Wird beim Start aus der Datenbank
/// übernommen und nach jeder Änderung in den Einstellungen aktualisiert.
/// </summary>
public sealed class Briefkopf
{
    public byte[]? Logo { get; private set; }

    public string? Website { get; private set; }

    public string? Name { get; private set; }

    public void Uebernehmen(Einrichtung? einrichtung)
    {
        Logo = einrichtung?.Logo;
        Website = einrichtung?.Website;
        Name = einrichtung?.Name;
    }
}

/// <summary>Änderungen am Datensatz der Einrichtung, die den Speicher für das Bearbeiten öffnen.</summary>
public interface IEinrichtungAendern
{
    Task<Einrichtung?> EinrichtungZumAendernAsync(CancellationToken abbruch = default);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Einstellungen der Einrichtung: Logo (Briefe, Kurs-QR-Bogen, Aushänge) und Website. Systemverwaltung und Leitung.</summary>
public sealed class EinrichtungDienst(IEinrichtungAendern speicher, Briefkopf briefkopf, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer)
{
    public static readonly Rolle[] Rollen = [Rolle.Systemverwaltung, Rolle.Leitung];

    public async Task LogoSetzenAsync(byte[]? bild, CancellationToken abbruch = default)
    {
        var e = await LadenAsync(abbruch);
        e.LogoSetzen(bild);
        await protokoll.ProtokollierenAsync("Einrichtung.Logo", bild is null ? "Logo entfernt" : $"Logo hinterlegt ({bild.Length / 1024} KB)", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        briefkopf.Uebernehmen(e);
    }

    public async Task WebsiteSetzenAsync(string? website, CancellationToken abbruch = default)
    {
        var e = await LadenAsync(abbruch);
        e.WebsiteSetzen(website);
        await protokoll.ProtokollierenAsync("Einrichtung.Website", $"Website: {e.Website ?? "–"}", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        briefkopf.Uebernehmen(e);
    }

    private async Task<Einrichtung> LadenAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (wer.Rolle is not { } r || !Rollen.Contains(r))
        {
            throw new InvalidOperationException("Logo und Website pflegen Systemverwaltung und Leitung.");
        }

        return await speicher.EinrichtungZumAendernAsync(abbruch) ?? throw new InvalidOperationException("Es ist noch keine Einrichtung angelegt.");
    }
}
