namespace VibeVHS.Application.Protokoll;

/// <summary>
/// Schreibt Protokolleinträge in dieselbe Transaktion wie die fachliche Änderung:
/// Der Eintrag wird beim nächsten Speichern gemeinsam mit der Änderung festgeschrieben.
/// </summary>
public interface IAuditProtokoll
{
    Task ProtokollierenAsync(string aktion, string beschreibung, string? objektTyp = null, Guid? objektId = null,
        bool erfolgreich = true, CancellationToken abbruch = default);

    Task<IReadOnlyList<AuditEintrag>> NeuesteAsync(int anzahl, CancellationToken abbruch = default);

    /// <summary>Alle Einträge seit <paramref name="seit"/> (neueste zuerst), optional nur einer Kennung (z. B. der Automatik).</summary>
    async Task<IReadOnlyList<AuditEintrag>> SeitAsync(DateTimeOffset seit, string? benutzerKennung = null, CancellationToken abbruch = default)
        => (await NeuesteAsync(int.MaxValue, abbruch)).Where(e => e.Zeitpunkt >= seit && (benutzerKennung is null || e.BenutzerKennung == benutzerKennung)).ToList();
}
