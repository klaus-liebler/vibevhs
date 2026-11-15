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
using VibeVHS.Infrastructure.Personen;
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

        dienste.AddDbContext<VibeVhsDbContext>(o => o.UseSqlite(verbindung.ToString(),
            s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

        dienste.AddScoped<IPersonenSpeicher, PersonenSpeicher>();
        dienste.AddScoped<INachrichtenSpeicher, NachrichtenSpeicher>();
        dienste.AddScoped<IAuditProtokoll, AuditProtokoll>();
        dienste.AddScoped<StammdatenSpeicher>();
        dienste.AddScoped<IStammdaten>(d => d.GetRequiredService<StammdatenSpeicher>());
        dienste.AddScoped<IBenutzerkonten>(d => d.GetRequiredService<StammdatenSpeicher>());
        dienste.AddScoped<StartdatenLader>();
        dienste.AddScoped<DublettenSuche>();
        dienste.AddScoped<IDublettenSuche>(d => d.GetRequiredService<DublettenSuche>());
        dienste.AddScoped<PersonenAbfrage>();
        dienste.AddScoped<IPersonenAbfrage>(d => d.GetRequiredService<PersonenAbfrage>());
        dienste.AddScoped<IPersonenExport>(d => d.GetRequiredService<PersonenAbfrage>());
        dienste.AddScoped<IOrganisationen, OrganisationenSpeicher>();
        dienste.AddScoped<Application.Programm.IProgrammSpeicher, Programm.ProgrammSpeicher>();
        dienste.AddScoped<Application.Programm.IGlobaleSuche, Programm.GlobaleSuche>();
        dienste.AddScoped<Application.Datenschutz.IDatenschutzSpeicher, Datenschutz.DatenschutzSpeicher>();
        dienste.AddSingleton<Application.Datenschutz.IAuskunftPdf, Datenschutz.AuskunftPdf>();
        dienste.AddScoped<Anmeldung.AnmeldungSpeicher>();
        dienste.AddScoped<Application.Anmeldung.IAnmeldungSpeicher>(d => d.GetRequiredService<Anmeldung.AnmeldungSpeicher>());
        dienste.AddScoped<Application.Anmeldung.IVorlagenSpeicher>(d => d.GetRequiredService<Anmeldung.AnmeldungSpeicher>());

        var takt = konfiguration.GetValue("Postausgang:TaktSekunden", 15);
        dienste.AddQuartz(q =>
        {
            q.AddJob<PostausgangJob>(j => j.WithIdentity(PostausgangJob.Schluessel));
            q.AddTrigger(t => t
                .ForJob(PostausgangJob.Schluessel)
                .WithIdentity("postausgang-takt", "automatik")
                .StartNow()
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromSeconds(takt)).RepeatForever()));

            // Täglich nachts sowie einmal kurz nach dem Start.
            q.AddJob<TermineFortschreibenJob>(j => j.WithIdentity(TermineFortschreibenJob.Schluessel));
            q.AddJob<MerkmalAblaufJob>(j => j.WithIdentity(MerkmalAblaufJob.Schluessel));
            q.AddJob<LoeschlaufJob>(j => j.WithIdentity(LoeschlaufJob.Schluessel));
            q.AddTrigger(t => t.ForJob(LoeschlaufJob.Schluessel).WithIdentity("loeschlauf-monatlich", "automatik").WithCronSchedule("0 0 3 1 * ?"));
            q.AddJob<AnmeldungJob>(j => j.WithIdentity(AnmeldungJob.Schluessel));
            q.AddTrigger(t => t.ForJob(AnmeldungJob.Schluessel).WithIdentity("anmeldung-stuendlich", "automatik").WithCronSchedule("0 5 * * * ?"));
            foreach (var schluessel in new[] { TermineFortschreibenJob.Schluessel, MerkmalAblaufJob.Schluessel })
            {
                q.AddTrigger(t => t.ForJob(schluessel).WithIdentity(schluessel.Name + "-nacht", "automatik").WithCronSchedule("0 15 2 * * ?"));
                q.AddTrigger(t => t.ForJob(schluessel).WithIdentity(schluessel.Name + "-start", "automatik").StartAt(DateTimeOffset.UtcNow.AddSeconds(20)));
            }
        });
        dienste.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        return dienste;
    }
}
