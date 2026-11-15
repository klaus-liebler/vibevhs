using System.Security.Cryptography;
using VibeVHS.Application.Postausgang;

namespace VibeVHS.Adapters.Mocks;

/// <summary>
/// Simulierter Mailversand: Es verlässt nichts das System. Die Mail bleibt mit vollem Text im Postausgang sichtbar.
/// </summary>
public sealed class MockMailKanal : IZustellKanal
{
    public Kanal Kanal => Kanal.EMail;

    public Task<string?> ZustellenAsync(AusgehendeNachricht nachricht, CancellationToken abbruch = default)
        => Task.FromResult<string?>("simuliert, nicht versendet");
}

/// <summary>
/// Simulierte Schnittstellen (BAMF, Finanzsoftware, Statistik): Antwortet wie die echte Gegenstelle
/// mit einer Vorgangsnummer, sendet aber nichts.
/// </summary>
public sealed class MockSchnittstellenKanal(TimeProvider uhr) : IZustellKanal
{
    public Kanal Kanal => Kanal.Schnittstelle;

    public Task<string?> ZustellenAsync(AusgehendeNachricht nachricht, CancellationToken abbruch = default)
    {
        var vorgang = $"{uhr.GetLocalNow():yyyy}-MOCK-{RandomNumberGenerator.GetInt32(0, 10000):D4}";
        return Task.FromResult<string?>($"simuliert: angenommen, Vorgang {vorgang}");
    }
}
