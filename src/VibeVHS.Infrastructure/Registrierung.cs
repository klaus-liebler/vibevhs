using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Infrastructure.Startdaten;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Hintergrund;

namespace VibeVHS.Infrastructure;

public static class Registrierung
{
    /// <param name="basisVerzeichnis">Relative Datenbankpfade werden hierzu aufgelöst (Inhaltsverzeichnis der Anwendung).</param>
    public static IServiceCollection AddVibeVhsInfrastruktur(this IServiceCollection dienste, IConfiguration konfiguration,
        string basisVerzeichnis)
    {
        var verbindung = new SqliteConnectionStringBuilder(konfiguration.GetConnectionString("VibeVHS") ?? "Data Source=data/vibevhs.db");
        if (!Path.IsPathRooted(verbindung.DataSource) && verbindung.DataSource != ":memory:")
        {
            verbindung.DataSource = Path.GetFullPath(Path.Combine(basisVerzeichnis, verbindung.DataSource));
        }

        dienste.AddDbContext<VibeVhsDbContext>(o => o.UseSqlite(verbindung.ToString()));

        dienste.AddScoped<IPersonenSpeicher, PersonenSpeicher>();
        dienste.AddScoped<INachrichtenSpeicher, NachrichtenSpeicher>();
        dienste.AddScoped<IAuditProtokoll, AuditProtokoll>();
        dienste.AddScoped<StammdatenSpeicher>();
        dienste.AddScoped<IStammdaten>(d => d.GetRequiredService<StammdatenSpeicher>());
        dienste.AddScoped<IBenutzerkonten>(d => d.GetRequiredService<StammdatenSpeicher>());
        dienste.AddScoped<StartdatenLader>();

        var takt = konfiguration.GetValue("Postausgang:TaktSekunden", 15);
        dienste.AddQuartz(q =>
        {
            q.AddJob<PostausgangJob>(j => j.WithIdentity(PostausgangJob.Schluessel));
            q.AddTrigger(t => t
                .ForJob(PostausgangJob.Schluessel)
                .WithIdentity("postausgang-takt", "automatik")
                .StartNow()
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromSeconds(takt)).RepeatForever()));
        });
        dienste.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        return dienste;
    }
}
