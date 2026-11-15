using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        dienste.AddScoped<Programm.ProgrammDienst>();
        dienste.AddScoped<Programm.QualifikationsDienst>();
        dienste.AddScoped<Programm.IDozenteneinsatz>(d => d.GetRequiredService<Programm.QualifikationsDienst>());
        dienste.AddScoped<Datenschutz.DatenschutzDienst>();
        dienste.AddScoped<Anmeldung.AnmeldungDienst>();
        dienste.AddScoped<Durchfuehrung.Termincodes>();
        dienste.AddSingleton(new Durchfuehrung.OeffentlicheAdresse(oeffentlicheAdresse));
        dienste.AddScoped<Durchfuehrung.CheckinZugang>();
        dienste.AddScoped<Durchfuehrung.AnwesenheitDienst>();
        dienste.AddScoped<Dokumente.DokumentDienst>();
        dienste.AddScoped<Anmeldung.VorlagenDienst>();
        dienste.AddScoped<Aufgaben.AufgabenDienst>();
        dienste.AddScoped<Aufgaben.Routinen>();
        dienste.AddScoped<Programm.Entfernungen>();
        dienste.TryAddSingleton(new Integration.IntegrationOptionen());
        dienste.AddScoped<Integration.IntegrationDienst>();
        dienste.AddScoped<Integration.IntegrationRoutinen>();
        dienste.AddScoped<Finanzen.FinanzDienst>();
        dienste.AddScoped<Finanzen.ZahlungsDienst>();
        dienste.AddScoped<Finanzen.MahnDienst>();
        dienste.AddScoped<Finanzen.GobdDienst>();
        dienste.AddScoped<Finanzen.FinanzRoutinen>();
        dienste.AddScoped<Finanzen.Honorare.HonorarDienst>();
        dienste.AddSingleton<Finanzen.Honorare.IHonorarModell, Finanzen.Honorare.HonorarProUe>();
        dienste.AddSingleton<Finanzen.Honorare.IHonorarModell, Finanzen.Honorare.HonorarBasisUeTeilnehmende>();
        dienste.AddSingleton<Finanzen.Honorare.HonorarModelle>();
        return dienste;
    }
}
