using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using VibeVHS.Adapters.Mocks;
using VibeVHS.Application;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Shop;
using VibeVHS.Web.Sicherheit;

namespace VibeVHS.Web.Tests;

/// <summary>
/// Startet die Buchungs-API in diesem Prozess auf einem freien Port (Kestrel, echte HTTP-Aufrufe) mit eigener SQLite-Datei
/// und steuerbarer Uhr. Hintergrundjobs laufen nicht; Tests rufen die Routinen direkt auf.
/// </summary>
internal sealed class ShopTestHost : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _ordner;

    private ShopTestHost(WebApplication app, FakeTimeProvider uhr, string ordner, Uri adresse)
    {
        App = app;
        Uhr = uhr;
        _ordner = ordner;
        Http = new HttpClient { BaseAddress = new Uri(adresse, ShopApi.Basis + "/") };
    }

    public WebApplication App { get; }

    public FakeTimeProvider Uhr { get; }

    public HttpClient Http { get; }

    public static async Task<ShopTestHost> StartenAsync(IDictionary<string, string?>? konfiguration = null)
    {
        var ordner = Path.Combine(Path.GetTempPath(), "vibevhs-shoptest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var werte = new Dictionary<string, string?>
        {
            ["ConnectionStrings:VibeVHS"] = $"Data Source={Path.Combine(ordner, "shop.db")}",
            ["Shop:PowBits"] = "4",
            ["Shop:MockIdp"] = "true",
            ["Shop:ErlaubteOrigins:0"] = "https://www.vhs-test.example",
            // Auch abgewiesene Versuche (z. B. ohne Arbeitsnachweis) zählen.
            ["Shop:RateLimit:RegistrierungProStunde"] = "4",
            ["Shop:RateLimit:SchreibenProMinute"] = "1000",
            ["Shop:RateLimit:LesenProMinute"] = "1000",
        };
        foreach (var (k, v) in konfiguration ?? new Dictionary<string, string?>())
        {
            werte[k] = v;
        }

        builder.Configuration.AddInMemoryCollection(werte);
        var s = builder.Services;
        s.AddHttpContextAccessor();
        s.AddScoped<BenutzerKontext>();
        s.AddScoped<IAktuellerBenutzer, AktuellerBenutzer>();
        s.AddVibeVhsAnwendung(new Uri("http://localhost/"))
            .AddVibeVhsInfrastruktur(builder.Configuration, ordner)
            .AddVibeVhsMocks(builder.Configuration);
        s.RemoveAll<IHostedService>();
        var uhr = new FakeTimeProvider(DateTimeOffset.UtcNow);
        uhr.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        s.Replace(ServiceDescriptor.Singleton<TimeProvider>(uhr));
        s.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ordner, "schluessel")));
        Sicherheitsheader.AddRateLimits(s, builder.Configuration);
        s.AddShopApi(builder.Configuration, builder.Environment);

        var app = builder.Build();
        await DatenbankInitialisierung.InitialisierenAsync(app.Services);
        app.UseCors();
        app.UseRateLimiter();
        app.MapShopApi();
        await app.StartAsync();
        return new ShopTestHost(app, uhr, ordner, new Uri(app.Urls.First()));
    }

    /// <summary>Veröffentlichter Kurs mit 6 Terminen ab in 30 Tagen.</summary>
    public async Task<Kurs> KursAsync(string nummer, int max, Buchungsrichtlinie? richtlinie = null, Sichtbarkeit sichtbarkeit = Sichtbarkeit.Oeffentlich,
        Kursstatus status = Kursstatus.Veroeffentlicht)
    {
        await using var bereich = App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var jetzt = Uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var semester = new Semester($"Semester {nummer}", nummer[..3], heute.AddDays(-30), heute.AddDays(300));
        var art = new Kursart($"Art {nummer}", Nutzungsart.Bewegung, [], richtlinie ?? Buchungsrichtlinie.Oeffentlich, sichtbarkeit);
        var haus = new Ort(Ortsart.Gebaeude, $"Haus {nummer}").MitAdresse("Bildungsweg 1", "48282", "Emsdetten");
        var raum = new Ort(Ortsart.Raum, "Bewegungsraum", haus.Id, "B 1.01").MitKapazitaet(Nutzungsart.Bewegung, 30);
        var kurs = new Kurs(nummer, $"Yoga {nummer}", semester, 8, art, 1, max).MitBeschreibung("Sanftes Yoga für alle.");
        kurs.EntgeltSetzenAb(Geld.Euro(84), Geld.Euro(63), semester.Beginn, Erfassung.Erst(jetzt.AddMonths(-1)));
        var start = heute.AddDays(30);
        kurs.SerieFestlegen(new Serienregel
        {
            Start = start, Anzahl = 6, Wochentage = [start.DayOfWeek], Beginn = new TimeOnly(19, 0), DauerMinuten = 90, OrtIds = [raum.Id],
        }, Kalender.OhneFerien, _ => "B 1.01 Bewegungsraum", heute.AddDays(300));
        kurs.StatusSetzen(status);
        db.AddRange(semester, art, haus, raum, kurs);
        await db.SaveChangesAsync();
        return kurs;
    }

    public async Task<string> NachweisAsync()
    {
        var a = await Http.GetFromJsonAsync<Arbeitsnachweis.Aufgabe>("pow", Json);
        return Arbeitsnachweis.Loesen(a!.Challenge, a.Bits);
    }

    public async Task<HttpResponseMessage> SendenAsync(HttpMethod methode, string pfad, object? inhalt = null, bool nachweis = false,
        string? idempotenz = null, string? token = null, string? origin = null)
    {
        using var anfrage = new HttpRequestMessage(methode, pfad);
        if (inhalt is not null)
        {
            anfrage.Content = JsonContent.Create(inhalt, options: Json);
        }

        if (nachweis)
        {
            anfrage.Headers.Add("X-VHS-PoW", await NachweisAsync());
        }

        if (idempotenz is not null)
        {
            anfrage.Headers.Add("Idempotency-Key", idempotenz);
        }

        if (token is not null)
        {
            anfrage.Headers.Authorization = new("Bearer", token);
        }

        if (origin is not null)
        {
            anfrage.Headers.Add("Origin", origin);
        }

        return await Http.SendAsync(anfrage);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage antwort) => (await antwort.Content.ReadFromJsonAsync<JsonElement>(Json));

    public static object Anfrage(string zahlart = "rechnung", string vorname = "Erika", string agb = "2026-08") => new
    {
        teilnehmer = new { vorname, nachname = "Muster", geburtsdatum = "1985-04-01", eMail = "erika@example.org", strasse = "Hauptstr. 1", postleitzahl = "48282", ort = "Emsdetten", sprache = "de" },
        zahlart,
        lastschrift = zahlart == "lastschrift" ? new { kontoinhaber = "Erika Muster", iban = "DE89 3704 0044 0532 0130 00", bic = (string?)null } : null,
        einwilligungen = new object[]
        {
            new { art = "agb", version = agb, erteilt = true }, new { art = "datenschutz", version = "2026-08", erteilt = true },
            new { art = "sepa", version = "2026-08", erteilt = zahlart == "lastschrift" }, new { art = "werbung", version = "2026-08", erteilt = true },
        },
    };

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_ordner, true);
        }
        catch (IOException)
        {
            // Temporärer Ordner; ein Rest stört nicht.
        }
    }
}
