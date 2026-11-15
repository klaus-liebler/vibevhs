using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;

namespace VibeVHS.Application;

public static class Registrierung
{
    public static IServiceCollection AddVibeVhsAnwendung(this IServiceCollection dienste, Uri oeffentlicheAdresse)
    {
        dienste.AddSingleton(TimeProvider.System);
        dienste.AddSingleton<IUebersetzungslinks>(new Uebersetzungslinks(oeffentlicheAdresse));
        dienste.AddScoped<PostausgangDienst>();
        dienste.AddScoped<Zustellung>();
        dienste.AddScoped<PersonenDienst>();
        dienste.AddScoped<Rundschreiben>();
        return dienste;
    }
}
