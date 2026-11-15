using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Hintergrund;

namespace VibeVHS.Infrastructure;

public static class Registrierung
{
    public static IServiceCollection AddVibeVhsInfrastruktur(this IServiceCollection dienste, IConfiguration konfiguration)
    {
        var verbindung = konfiguration.GetConnectionString("VibeVHS") ?? "Data Source=data/vibevhs.db";
        dienste.AddDbContext<VibeVhsDbContext>(o => o.UseSqlite(verbindung));

        dienste.AddScoped<IPersonenSpeicher, PersonenSpeicher>();
        dienste.AddScoped<INachrichtenSpeicher, NachrichtenSpeicher>();
        dienste.AddScoped<IAuditProtokoll, AuditProtokoll>();

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
