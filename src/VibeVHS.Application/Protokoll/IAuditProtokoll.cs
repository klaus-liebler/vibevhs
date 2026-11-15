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
}
