using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Postausgang;

namespace VibeVHS.Adapters.Mocks;

public static class Registrierung
{
    /// <summary>Registriert simulierte Zustellkanäle. Echte Adapter ersetzen sie später je Kanal.</summary>
    public static IServiceCollection AddVibeVhsMocks(this IServiceCollection dienste)
    {
        dienste.AddSingleton<IZustellKanal, MockMailKanal>();
        dienste.AddSingleton<IZustellKanal, MockSchnittstellenKanal>();
        return dienste;
    }
}
