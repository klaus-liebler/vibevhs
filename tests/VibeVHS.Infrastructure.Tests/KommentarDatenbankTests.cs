using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VibeVHS.Application.Dokumentation;
using VibeVHS.Domain.Dokumentation;
using VibeVHS.Infrastructure.Dokumentation;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Kommentar-Datenbank: eigene Datei neben der Hauptdatenbank, übersteht deren Neuanlage, prüft ihre Schemaversion.</summary>
public sealed class KommentarDatenbankTests : IDisposable
{
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "vibevhs-kommentare-" + Guid.NewGuid().ToString("N"));

    public KommentarDatenbankTests() => Directory.CreateDirectory(_ordner);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_ordner, recursive: true); } catch (IOException) { /* Datei noch offen – Temp räumt auf */ }
    }

    private ServiceProvider Dienste(Dictionary<string, string?>? zusatz = null)
    {
        var werte = new Dictionary<string, string?> { ["ConnectionStrings:VibeVHS"] = $"Data Source={Path.Combine(_ordner, "vibevhs.db")}" };
        foreach (var (k, v) in zusatz ?? []) { werte[k] = v; }
        var konfiguration = new ConfigurationBuilder().AddInMemoryCollection(werte).Build();
        var s = new ServiceCollection();
        s.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        s.AddSingleton(TimeProvider.System);
        s.AddVibeVhsInfrastruktur(konfiguration, _ordner, strengeAbfragepruefung: true);
        return s.BuildServiceProvider();
    }

    [Fact]
    public void Standardort_ist_kommentare_db_neben_der_Hauptdatenbank()
    {
        using var d = Dienste();
        d.GetRequiredService<KommentarDatenbankOrt>().Datei.ShouldBe(Path.Combine(_ordner, "kommentare.db"));
    }

    [Fact]
    public void Eigener_Ort_ueber_ConnectionStrings_Kommentare()
    {
        using var d = Dienste(new() { ["ConnectionStrings:Kommentare"] = "Data Source=anderswo/k.db" });
        d.GetRequiredService<KommentarDatenbankOrt>().Datei.ShouldBe(Path.Combine(_ordner, "anderswo", "k.db"));
    }

    [Fact]
    public async Task Kommentare_ueberstehen_das_Neuanlegen_der_Hauptdatenbank()
    {
        var id = Guid.Empty;
        await using (var d = Dienste())
        {
            await KommentarDbContext.InitialisierenAsync(d, abbruch: TestContext.Current.CancellationToken);
            await using var bereich = d.CreateAsyncScope();
            var speicher = bereich.ServiceProvider.GetRequiredService<IKommentarSpeicher>();
            var k = new Kommentar("hilfe", "bedienung", "Bedienung", "Bleibt erhalten", "sschulte", "S. Schulte", "Leitung", DateTimeOffset.UtcNow);
            id = k.Id;
            speicher.Hinzufuegen(k);
            await speicher.SpeichernAsync(abbruch: TestContext.Current.CancellationToken);
        }

        SqliteConnection.ClearAllPools();
        // deploy.ps1 -DbNeu legt nur vibevhs.db (samt -wal/-shm) beiseite
        foreach (var f in Directory.GetFiles(_ordner, "vibevhs.db*")) { File.Delete(f); }

        await using (var d = Dienste())
        {
            await KommentarDbContext.InitialisierenAsync(d, abbruch: TestContext.Current.CancellationToken);
            await using var bereich = d.CreateAsyncScope();
            var k = await bereich.ServiceProvider.GetRequiredService<IKommentarSpeicher>().LadenAsync(id, abbruch: TestContext.Current.CancellationToken);
            k.ShouldNotBeNull();
            k.Text.ShouldBe("Bleibt erhalten");
        }
    }

    [Fact]
    public async Task Abweichende_Schemaversion_bricht_ab_und_raet_nicht_zum_Loeschen()
    {
        await using (var d = Dienste())
        {
            await KommentarDbContext.InitialisierenAsync(d, abbruch: TestContext.Current.CancellationToken);
            await using var bereich = d.CreateAsyncScope();
            await bereich.ServiceProvider.GetRequiredService<KommentarDbContext>().Database.ExecuteSqlRawAsync("PRAGMA user_version = 99", cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var d = Dienste())
        {
            var fehler = await Should.ThrowAsync<KommentarSchemaException>(() => KommentarDbContext.InitialisierenAsync(d, abbruch: TestContext.Current.CancellationToken));
            fehler.Message.ShouldContain("bitte NICHT löschen");
            fehler.Message.ShouldNotContain("Datenbankschema veraltet"); // sonst schlüge ausrollen.sh -DbNeu vor
        }
    }
}
