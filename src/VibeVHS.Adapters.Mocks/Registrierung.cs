using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Postausgang;

namespace VibeVHS.Adapters.Mocks;

public static class Registrierung
{
    /// <summary>Registriert Testanmeldung und simulierte Zustellkanäle. Echte Adapter ersetzen sie später je Kanal.</summary>
    public static IServiceCollection AddVibeVhsMocks(this IServiceCollection dienste, IConfiguration konfiguration)
    {
        dienste.Configure<TestanmeldungOptionen>(konfiguration.GetSection(TestanmeldungOptionen.Abschnitt));
        dienste.AddSingleton<Zugangsschutz>();
        dienste.AddSingleton<IZustellKanal, MockMailKanal>();
        dienste.AddSingleton<IZustellKanal, MockSchnittstellenKanal>();
        dienste.AddSingleton<VibeVHS.Application.Dokumente.IUebersetzer, MockUebersetzer>();
        dienste.AddSingleton<VibeVHS.Application.Programm.IGeokodierer, MockGeokodierer>();
        dienste.AddScoped<VibeVHS.Application.Integration.IBamfSchnittstelle, MockBamf>();
        return dienste;
    }
}
