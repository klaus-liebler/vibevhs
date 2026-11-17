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
    /// <summary>
    /// Liest die Verbindungszeichenfolge (ConnectionStrings:VibeVHS bzw. ConnectionStrings__VibeVHS) und löst relative Pfade auf.
    /// Fehlt sie, gilt „Data Source=data/vibevhs.db“; ist sie ungültig, bricht der Start mit einer verständlichen Meldung ab.
    /// </summary>
    public static SqliteConnectionStringBuilder Datenbankverbindung(string? wert, string basisVerzeichnis)
    {
        const string hinweis = "Erwartet z. B. „Data Source=/var/lib/vibevhs/vibevhs.db“. In einer systemd-Unit die ganze Zuweisung " +
                               "in Anführungszeichen setzen: Environment=\"ConnectionStrings__VibeVHS=Data Source=…\" (sonst trennt systemd am Leerzeichen).";
        SqliteConnectionStringBuilder verbindung;
        try
        {
            verbindung = new SqliteConnectionStringBuilder(string.IsNullOrWhiteSpace(wert) ? "Data Source=data/vibevhs.db" : wert);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Ungültige Datenbank-Verbindungszeichenfolge ConnectionStrings:VibeVHS = „{wert}“. {hinweis}", ex);
        }

        if (string.IsNullOrWhiteSpace(verbindung.DataSource))
        {
            throw new InvalidOperationException($"Datenbank-Verbindungszeichenfolge ConnectionStrings:VibeVHS = „{wert}“ nennt keine Datei (Data Source fehlt). {hinweis}");
        }

        if (!Path.IsPathRooted(verbindung.DataSource) && verbindung.DataSource != ":memory:")
        {
            verbindung.DataSource = Path.GetFullPath(Path.Combine(basisVerzeichnis, verbindung.DataSource));
        }

        return verbindung;
    }

    /// <summary>
    /// Verbindung der Kommentar-Datenbank: ConnectionStrings:Kommentare, sonst „kommentare.db“ im Verzeichnis der (ersten)
    /// Hauptdatenbank; liegt diese im Arbeitsspeicher, unter data/ im Basisverzeichnis.
    /// </summary>
    public static string KommentarVerbindung(IConfiguration konfiguration, string hauptverbindung, string basisVerzeichnis)
    {
        if (konfiguration.GetConnectionString("Kommentare") is { Length: > 0 } eigene)
        {
            return Datenbankverbindung(eigene, basisVerzeichnis).ToString();
        }

        var haupt = new SqliteConnectionStringBuilder(hauptverbindung).DataSource;
        var verzeichnis = haupt == ":memory:" ? Path.Combine(basisVerzeichnis, "data") : Path.GetDirectoryName(Path.GetFullPath(haupt))!;
        return new SqliteConnectionStringBuilder { DataSource = Path.Combine(verzeichnis, "kommentare.db") }.ToString();
    }

    /// <param name="strengeAbfragepruefung">Abfragen ohne eindeutige Sortierung brechen ab (Entwicklung, Tests) statt nur protokolliert zu werden.</param>
    /// <param name="basisVerzeichnis">Relative Datenbankpfade werden hierzu aufgelöst (Inhaltsverzeichnis der Anwendung).</param>
    public static IServiceCollection AddVibeVhsInfrastruktur(this IServiceCollection dienste, IConfiguration konfiguration,
        string basisVerzeichnis, bool strengeAbfragepruefung = false)
    {
        // Anzeige- und Eingabezeitzone (Einstellung „Zeitzone“, z. B. Europe/Berlin); ohne Einstellung die Systemzeitzone des Servers.
        Domain.Gemeinsam.Zeitzone.Festlegen(konfiguration["Zeitzone"]);

        // Mandanten (Vorbereitung „eine Datenbank je VHS“): Mandanten:<Kennung>:Datenbank / :Host; ohne Eintrag genau ein Mandant „standard“.
        var mandanten = konfiguration.GetSection("Mandanten").GetChildren()
            .Select(m => new Application.Betrieb.Mandant(m.Key, Datenbankverbindung(m["Datenbank"], basisVerzeichnis).ToString(), m["Host"]))
            .ToList();
        if (mandanten.Count == 0)
        {
            mandanten.Add(new Application.Betrieb.Mandant("standard", Datenbankverbindung(konfiguration.GetConnectionString("VibeVHS"), basisVerzeichnis).ToString()));
        }

        dienste.AddSingleton(new Application.Betrieb.Mandanten(mandanten));
        dienste.AddScoped<Application.Betrieb.Mandantenkontext>();
        dienste.AddDbContext<VibeVhsDbContext>((d, o) => o.UseSqlite(d.GetRequiredService<Application.Betrieb.Mandantenkontext>().Aktuell.Verbindung,
            s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).Konfigurieren(strengeAbfragepruefung)
            .AddInterceptors(StichzeitBeimLaden.Instanz));

        // Kommentare zu Spezifikation/Doku: eigene Datei (Standard: kommentare.db neben der Hauptdatenbank), übersteht deren Neuanlage.
        var kommentarOrt = new Dokumentation.KommentarDatenbankOrt(KommentarVerbindung(konfiguration, mandanten[0].Verbindung, basisVerzeichnis));
        dienste.AddSingleton(kommentarOrt);
        dienste.AddDbContext<Dokumentation.KommentarDbContext>(o => o.UseSqlite(kommentarOrt.Verbindung).Konfigurieren(strengeAbfragepruefung));
        dienste.AddScoped<Application.Dokumentation.IKommentarSpeicher, Dokumentation.KommentarSpeicher>();

        dienste.AddScoped<IPersonenSpeicher, PersonenSpeicher>();
        dienste.AddScoped<INachrichtenSpeicher, NachrichtenSpeicher>();
        dienste.AddScoped<IAuditProtokoll, AuditProtokoll>();
        dienste.AddScoped<StammdatenSpeicher>();
        dienste.AddScoped<IStammdaten>(d => d.GetRequiredService<StammdatenSpeicher>());
        dienste.AddScoped<IBenutzerkonten>(d => d.GetRequiredService<StammdatenSpeicher>());
        dienste.AddScoped<IEinrichtungAendern>(d => d.GetRequiredService<StammdatenSpeicher>());
        dienste.AddScoped<IBenutzerkontenPflege>(d => d.GetRequiredService<StammdatenSpeicher>());
        dienste.AddScoped<StartdatenLader>();
        dienste.AddScoped<DublettenSuche>();
        dienste.AddScoped<IDublettenSuche>(d => d.GetRequiredService<DublettenSuche>());
        dienste.AddScoped<Application.Programm.INamensquelle, Namensquelle>();
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

            // Täglich nachts; einmal nach dem Start in fester Reihenfolge über den StartabgleichDienst.
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
            }

            // Aufgaben-Routinen stündlich (nach dem Start als letzter Schritt des Startabgleichs), Erinnerungen morgens.
            q.AddJob<AufgabenJob>(j => j.WithIdentity(AufgabenJob.Schluessel));
            q.AddTrigger(t => t.ForJob(AufgabenJob.Schluessel).WithIdentity("aufgaben-stuendlich", "automatik").WithCronSchedule("0 10 * * * ?"));
            q.AddJob<ErinnerungJob>(j => j.WithIdentity(ErinnerungJob.Schluessel));
            q.AddTrigger(t => t.ForJob(ErinnerungJob.Schluessel).WithIdentity("erinnerung-morgens", "automatik").WithCronSchedule("0 0 7 * * ?"));
            q.AddJob<IntegrationJob>(j => j.WithIdentity(IntegrationJob.Schluessel));
            q.AddTrigger(t => t.ForJob(IntegrationJob.Schluessel).WithIdentity("integration-nachts", "automatik").WithCronSchedule("0 45 2 * * ?"));
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
        });
        dienste.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
        dienste.AddSingleton(d => new Application.Betrieb.Startabgleich(d.GetRequiredService<TimeProvider>(),
            StartabgleichDienst.Reihenfolge.Select(s => (s.Schluessel.ToString(), s.Anzeige))));
        dienste.AddHostedService<StartabgleichDienst>();
        return dienste;
    }
}
