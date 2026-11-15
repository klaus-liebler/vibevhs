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
        dienste.AddScoped<Application.Programm.IQualifikationsSpeicher, Programm.QualifikationsSpeicher>();
        dienste.AddScoped<Application.Durchfuehrung.IDurchfuehrungSpeicher, Durchfuehrung.DurchfuehrungSpeicher>();
        dienste.AddScoped<Application.Dokumente.IDokumentSpeicher, Durchfuehrung.DokumentSpeicher>();
        dienste.AddScoped<Application.Durchfuehrung.IZugangstokenSpeicher, Durchfuehrung.ZugangstokenSpeicher>();
        dienste.AddSingleton<Application.Dokumente.IQrCode, Durchfuehrung.QrCodeErzeuger>();
        dienste.AddSingleton<Application.Dokumente.IBriefPdf, Durchfuehrung.BriefPdf>();
        dienste.AddSingleton<Application.Shop.IKursQrBogen, Durchfuehrung.KursQrBogen>();
        dienste.AddScoped<Application.Datenschutz.IDatenschutzSpeicher, Datenschutz.DatenschutzSpeicher>();
        dienste.AddSingleton<Application.Datenschutz.IAuskunftPdf, Datenschutz.AuskunftPdf>();
        dienste.AddScoped<Application.Aufgaben.IAufgabenSpeicher, Aufgaben.AufgabenSpeicher>();
        dienste.AddScoped<Application.Finanzen.IFinanzSpeicher, Finanzen.FinanzSpeicher>();
        dienste.AddScoped<Application.Integration.IIntegrationSpeicher, Integration.IntegrationSpeicher>();
        dienste.AddScoped<Application.Betrieb.IAuswertungsquelle, Betrieb.Auswertungsquelle>();
        dienste.AddSingleton<Application.Betrieb.ITabellenExport, Betrieb.TabellenExport>();
        dienste.AddScoped<Application.Betrieb.ISicherung, Betrieb.SqliteSicherung>();
        dienste.AddScoped<Application.Betrieb.IImportSpeicher, Betrieb.ImportSpeicher>();
        dienste.AddScoped<Application.Finanzen.IFinanzprotokoll, Finanzen.Finanzprotokoll>();
        dienste.AddScoped<Application.Portal.IPortalSpeicher, Portal.PortalSpeicher>();
        dienste.AddScoped<Shop.ShopSpeicher>();
        dienste.AddScoped<Application.Shop.IShopSpeicher>(d => d.GetRequiredService<Shop.ShopSpeicher>());
        dienste.AddScoped<Application.Anmeldung.IPlatzreservierungen>(d => d.GetRequiredService<Shop.ShopSpeicher>());
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
            q.AddJob<QualifikationJob>(j => j.WithIdentity(QualifikationJob.Schluessel));
            q.AddJob<AnwesenheitJob>(j => j.WithIdentity(AnwesenheitJob.Schluessel));
            foreach (var schluessel in new[] { TermineFortschreibenJob.Schluessel, MerkmalAblaufJob.Schluessel, QualifikationJob.Schluessel, AnwesenheitJob.Schluessel })
            {
                q.AddTrigger(t => t.ForJob(schluessel).WithIdentity(schluessel.Name + "-nacht", "automatik").WithCronSchedule("0 15 2 * * ?"));
                q.AddTrigger(t => t.ForJob(schluessel).WithIdentity(schluessel.Name + "-start", "automatik").StartAt(DateTimeOffset.UtcNow.AddSeconds(20)));
            }

            // Aufgaben-Routinen stündlich und kurz nach dem Start (nach den nächtlichen Prüfungen), Erinnerungen morgens.
            q.AddJob<AufgabenJob>(j => j.WithIdentity(AufgabenJob.Schluessel));
            q.AddTrigger(t => t.ForJob(AufgabenJob.Schluessel).WithIdentity("aufgaben-stuendlich", "automatik").WithCronSchedule("0 10 * * * ?"));
            q.AddTrigger(t => t.ForJob(AufgabenJob.Schluessel).WithIdentity("aufgaben-start", "automatik").StartAt(DateTimeOffset.UtcNow.AddSeconds(40)));
            q.AddJob<ErinnerungJob>(j => j.WithIdentity(ErinnerungJob.Schluessel));
            q.AddTrigger(t => t.ForJob(ErinnerungJob.Schluessel).WithIdentity("erinnerung-morgens", "automatik").WithCronSchedule("0 0 7 * * ?"));
            q.AddJob<IntegrationJob>(j => j.WithIdentity(IntegrationJob.Schluessel));
            q.AddTrigger(t => t.ForJob(IntegrationJob.Schluessel).WithIdentity("integration-nachts", "automatik").WithCronSchedule("0 45 2 * * ?"));
            q.AddTrigger(t => t.ForJob(IntegrationJob.Schluessel).WithIdentity("integration-start", "automatik").StartAt(DateTimeOffset.UtcNow.AddSeconds(75)));
            if (konfiguration.GetValue("Sicherung:Aktiv", true))
            {
                q.AddJob<SicherungJob>(j => j.WithIdentity(SicherungJob.Schluessel));
                q.AddTrigger(t => t.ForJob(SicherungJob.Schluessel).WithIdentity("sicherung-nachts", "automatik").WithCronSchedule(konfiguration["Sicherung:Zeitplan"] ?? "0 30 1 * * ?"));
            }

            q.AddJob<Shop.ShopJob>(j => j.WithIdentity(Shop.ShopJob.Schluessel));
            q.AddTrigger(t => t.ForJob(Shop.ShopJob.Schluessel).WithIdentity("shop-minuetlich", "automatik").StartAt(DateTimeOffset.UtcNow.AddSeconds(30))
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()));

            q.AddJob<FinanzJob>(j => j.WithIdentity(FinanzJob.Schluessel));
            q.AddTrigger(t => t.ForJob(FinanzJob.Schluessel).WithIdentity("finanzen-morgens", "automatik").WithCronSchedule("0 30 6 * * ?"));
            q.AddTrigger(t => t.ForJob(FinanzJob.Schluessel).WithIdentity("finanzen-start", "automatik").StartAt(DateTimeOffset.UtcNow.AddSeconds(60)));
        });
        dienste.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        return dienste;
    }
}
