using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
    /// <summary>Verbindung der lokalen Entwicklungsdatenbank (deploy/docker-compose.yml); nur in appsettings.Development.json eingetragen.</summary>
    public const string Entwicklungsverbindung = "Host=127.0.0.1;Port=5441;Database=vibevhs;Username=vibevhs;Password=entwicklung";

    /// <summary>
    /// Liest die Verbindungszeichenfolge (ConnectionStrings:VibeVHS bzw. ConnectionStrings__VibeVHS) zu PostgreSQL. Fehlt sie oder nennt sie
    /// keinen Server oder keine Datenbank, bricht der Start mit einer verständlichen Meldung ab.
    /// </summary>
    public static NpgsqlConnectionStringBuilder Datenbankverbindung(string? wert)
    {
        const string hinweis = "Erwartet z. B. „Host=127.0.0.1;Port=5432;Database=vibevhs;Username=vibevhs;Password=…“. In einer systemd-Unit die ganze " +
                               "Zuweisung in Anführungszeichen setzen: Environment=\"ConnectionStrings__VibeVHS=Host=…\" (sonst trennt systemd am Leerzeichen). " +
                               "Für die Entwicklung: docker compose -f deploy/docker-compose.yml up -d.";
        if (string.IsNullOrWhiteSpace(wert))
        {
            throw new InvalidOperationException($"Keine Datenbank-Verbindungszeichenfolge ConnectionStrings:VibeVHS. {hinweis}");
        }

        NpgsqlConnectionStringBuilder verbindung;
        try
        {
            verbindung = new NpgsqlConnectionStringBuilder(wert);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Ungültige Datenbank-Verbindungszeichenfolge ConnectionStrings:VibeVHS = „{Ohne(wert)}“. {hinweis}", ex);
        }

        if (string.IsNullOrWhiteSpace(verbindung.Host) || string.IsNullOrWhiteSpace(verbindung.Database))
        {
            throw new InvalidOperationException(
                $"Datenbank-Verbindungszeichenfolge ConnectionStrings:VibeVHS = „{Ohne(wert)}“ nennt keinen Server (Host) oder keine Datenbank (Database). {hinweis}");
        }

        return verbindung;
    }

    /// <summary>Verbindungszeichenfolge ohne Passwort (für Meldungen).</summary>
    private static string Ohne(string wert)
    {
        try
        {
            var b = new NpgsqlConnectionStringBuilder(wert);
            if (!string.IsNullOrEmpty(b.Password))
            {
                b.Password = "***";
            }

            return b.ConnectionString;
        }
        catch (ArgumentException)
        {
            return wert.Contains("Password", StringComparison.OrdinalIgnoreCase) ? "(nicht lesbar, enthält ein Passwort)" : wert;
        }
    }

    /// <summary>
    /// Verbindung der Kommentar-Datenbank: ConnectionStrings:Kommentare, sonst dieselbe Verbindung wie die (erste) Hauptdatenbank mit der
    /// Datenbank „&lt;name&gt;_kommentare“ – eine eigene Datenbank, die das Beiseitelegen der Hauptdatenbank übersteht.
    /// </summary>
    public static string KommentarVerbindung(IConfiguration konfiguration, string hauptverbindung)
    {
        if (konfiguration.GetConnectionString("Kommentare") is { Length: > 0 } eigene)
        {
            return Datenbankverbindung(eigene).ConnectionString;
        }

        var haupt = new NpgsqlConnectionStringBuilder(hauptverbindung);
        return new NpgsqlConnectionStringBuilder(hauptverbindung) { Database = haupt.Database + "_kommentare" }.ConnectionString;
    }

    /// <param name="strengeAbfragepruefung">Abfragen ohne eindeutige Sortierung brechen ab (Entwicklung, Tests) statt nur protokolliert zu werden.</param>
    /// <param name="basisVerzeichnis">Inhaltsverzeichnis der Anwendung (relative Pfade, z. B. eine alte Kommentar-Datei zur Übernahme).</param>
    public static IServiceCollection AddVibeVhsInfrastruktur(this IServiceCollection dienste, IConfiguration konfiguration,
        string basisVerzeichnis, bool strengeAbfragepruefung = false)
    {
        // Anzeige- und Eingabezeitzone (Einstellung „Zeitzone“, z. B. Europe/Berlin); ohne Einstellung die Systemzeitzone des Servers.
        Domain.Gemeinsam.Zeitzone.Festlegen(konfiguration["Zeitzone"]);

        // Mandanten (Vorbereitung „eine Datenbank je VHS“): Mandanten:<Kennung>:Datenbank / :Host; ohne Eintrag genau ein Mandant „standard“.
        var mandanten = konfiguration.GetSection("Mandanten").GetChildren()
            .Select(m => new Application.Betrieb.Mandant(m.Key, Datenbankverbindung(m["Datenbank"]).ConnectionString, m["Host"]))
            .ToList();
        if (mandanten.Count == 0)
        {
            mandanten.Add(new Application.Betrieb.Mandant("standard", Datenbankverbindung(konfiguration.GetConnectionString("VibeVHS")).ConnectionString));
        }

        dienste.AddSingleton(new Application.Betrieb.Mandanten(mandanten));
        dienste.AddScoped<Application.Betrieb.Mandantenkontext>();
        dienste.AddDbContext<VibeVhsDbContext>((d, o) => o.UseNpgsql(d.GetRequiredService<Application.Betrieb.Mandantenkontext>().Aktuell.Verbindung,
            s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).Konfigurieren(strengeAbfragepruefung)
            .AddInterceptors(StichzeitBeimLaden.Instanz));

        // Kommentare zu Spezifikation/Doku: eigene Datenbank (Standard: <haupt>_kommentare), übersteht das Beiseitelegen der Hauptdatenbank.
        // Eine alte Kommentar-Datei aus der SQLite-Zeit (Kommentare:Altdatei) wird beim ersten Start einmal übernommen.
        var altdatei = konfiguration["Kommentare:Altdatei"];
        var kommentarOrt = new Dokumentation.KommentarDatenbankOrt(KommentarVerbindung(konfiguration, mandanten[0].Verbindung),
            string.IsNullOrWhiteSpace(altdatei) ? null : Path.GetFullPath(Path.Combine(basisVerzeichnis, altdatei)));
        dienste.AddSingleton(kommentarOrt);
        dienste.AddDbContext<Dokumentation.KommentarDbContext>(o => o.UseNpgsql(kommentarOrt.Verbindung).Konfigurieren(strengeAbfragepruefung));
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
        dienste.AddSingleton(new Betrieb.SicherungsOrt(Path.GetFullPath(Path.Combine(basisVerzeichnis, konfiguration["Sicherung:Ordner"] ?? "data/sicherung"))));
        dienste.AddScoped<Application.Betrieb.ISicherung, Betrieb.PostgresSicherung>();
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
