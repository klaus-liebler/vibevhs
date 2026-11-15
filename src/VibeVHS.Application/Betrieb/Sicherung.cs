using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Application.Betrieb;

/// <summary>Eine Sicherung der Datenbank mit Prüfsumme und Ergebnis des Wiederherstellungstests.</summary>
public sealed record Sicherungseintrag(string Datei, DateTimeOffset Am, long Bytes, string Sha256, bool? Wiederherstellbar, string? Pruefergebnis, string? Migration);

/// <summary>
/// Datensicherung: SQLite-Online-Sicherung (konsistent im laufenden Betrieb) mit SHA-256, anschließendem
/// Wiederherstellungstest (Kopie öffnen, Integrität prüfen, Datenstand vergleichen) und Aufbewahrung der letzten N Sicherungen.
/// </summary>
public interface ISicherung
{
    Task<Sicherungseintrag> SichernAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Sicherungseintrag>> ListeAsync(CancellationToken abbruch = default);

    /// <summary>Wiederherstellungstest einer vorhandenen Sicherung (ohne die laufende Datenbank zu verändern).</summary>
    Task<Sicherungseintrag> PruefenAsync(string datei, CancellationToken abbruch = default);
}

public static class Betriebsrollen
{
    public static readonly Rolle[] Sicherung = [Rolle.Systemverwaltung, Rolle.Leitung];

    public static readonly Rolle[] Import = [Rolle.Systemverwaltung, Rolle.Verwaltungsleitung, Rolle.Leitung];
}
