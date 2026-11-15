using System.Text;
using VibeVHS.Application.Portal;

namespace VibeVHS.Adapters.Mocks;

/// <summary>
/// Virenprüfung als Platzhalter: erkennt nur die EICAR-Testdatei. In Produktion durch den Virenscanner der Kommune
/// (z. B. ClamAV per clamd) ersetzen.
/// </summary>
public sealed class MockVirenpruefung : IVirenpruefung
{
    private const string Eicar = "EICAR-STANDARD-ANTIVIRUS-TEST-FILE";

    public Task<string?> PruefenAsync(string dateiname, byte[] inhalt, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var text = Encoding.ASCII.GetString(inhalt, 0, Math.Min(inhalt.Length, 4096));
        return Task.FromResult(text.Contains(Eicar, StringComparison.Ordinal) ? "Testvirus EICAR erkannt (Mock-Virenprüfung)" : null);
    }
}
