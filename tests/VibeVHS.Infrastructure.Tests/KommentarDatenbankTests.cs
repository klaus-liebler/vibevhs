using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VibeVHS.Application.Dokumentation;
using VibeVHS.Domain.Dokumentation;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Dokumentation;
using VibeVHS.Tests.Gemeinsam;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Kommentar-Datenbank: eigene Datenbank neben der Hauptdatenbank, übersteht deren Beiseitelegen, prüft ihre Schemaversion.</summary>
public sealed class KommentarDatenbankTests : IAsyncLifetime
{
    private readonly string _ordner = Path.Combine(Path.GetTempPath(), "vibevhs-kommentare-" + Guid.NewGuid().ToString("N"));
    private string _haupt = string.Empty;

    private string Kommentare => new Npgsql.NpgsqlConnectionStringBuilder(_haupt) { Database = new Npgsql.NpgsqlConnectionStringBuilder(_haupt).Database + "_kommentare" }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_ordner);
        _haupt = await Wegwerfdatenbanken.NeuAsync("vibevhs_k");
    }

    public async ValueTask DisposeAsync()
    {
        await Wegwerfdatenbanken.LoeschenAsync(_haupt);
        await Wegwerfdatenbanken.LoeschenAsync(Kommentare);
        try { Directory.Delete(_ordner, recursive: true); } catch (IOException) { /* Temp räumt auf */ }
    }

    private ServiceProvider Dienste(Dictionary<string, string?>? zusatz = null)
    {
        var werte = new Dictionary<string, string?> { ["ConnectionStrings:VibeVHS"] = _haupt };
        foreach (var (k, v) in zusatz ?? []) { werte[k] = v; }
        var konfiguration = new ConfigurationBuilder().AddInMemoryCollection(werte).Build();
        var s = new ServiceCollection();
        s.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        s.AddSingleton(TimeProvider.System);
        s.AddVibeVhsInfrastruktur(konfiguration, _ordner, strengeAbfragepruefung: true);
        return s.BuildServiceProvider();
    }

    [Fact]
    public void Standard_ist_eine_eigene_Datenbank_neben_der_Hauptdatenbank()
    {
        using var d = Dienste();
        d.GetRequiredService<KommentarDatenbankOrt>().Datenbank.ShouldBe(new Npgsql.NpgsqlConnectionStringBuilder(_haupt).Database + "_kommentare");
    }

    [Fact]
    public void Eigener_Ort_ueber_ConnectionStrings_Kommentare()
    {
        using var d = Dienste(new() { ["ConnectionStrings:Kommentare"] = "Host=anderswo;Database=k" });
        d.GetRequiredService<KommentarDatenbankOrt>().Datenbank.ShouldBe("k");
    }

    [Fact]
    public async Task Kommentare_ueberstehen_das_Beiseitelegen_der_Hauptdatenbank()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.Empty;
        await using (var d = Dienste())
        {
            await KommentarDbContext.InitialisierenAsync(d, abbruch: ct);
            await using var bereich = d.CreateAsyncScope();
            var speicher = bereich.ServiceProvider.GetRequiredService<IKommentarSpeicher>();
            var k = new Kommentar("hilfe", "bedienung", "Bedienung", "Bleibt erhalten", "sschulte", "S. Schulte", "Leitung", DateTimeOffset.UtcNow);
            id = k.Id;
            speicher.Hinzufuegen(k);
            await speicher.SpeichernAsync(abbruch: ct);
        }

        // deploy.ps1 -DbNeu legt nur die Hauptdatenbank beiseite
        var beiseite = await Schema.BeiseitelegenAsync(new Npgsql.NpgsqlConnectionStringBuilder(_haupt), ct);
        await Wegwerfdatenbanken.LoeschenAsync(Wegwerfdatenbanken.Mit(beiseite));

        await using (var d = Dienste())
        {
            await KommentarDbContext.InitialisierenAsync(d, abbruch: ct);
            await using var bereich = d.CreateAsyncScope();
            var k = await bereich.ServiceProvider.GetRequiredService<IKommentarSpeicher>().LadenAsync(id, abbruch: ct);
            k.ShouldNotBeNull();
            k.Text.ShouldBe("Bleibt erhalten");
        }
    }

    [Fact]
    public async Task Abweichende_Schemaversion_bricht_ab_und_raet_nicht_zum_Loeschen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var d = Dienste())
        {
            await KommentarDbContext.InitialisierenAsync(d, abbruch: ct);
            await using var bereich = d.CreateAsyncScope();
            await bereich.ServiceProvider.GetRequiredService<KommentarDbContext>().Database.ExecuteSqlRawAsync("UPDATE \"Schemainfo\" SET \"Version\" = 99", cancellationToken: ct);
        }

        await using (var d = Dienste())
        {
            var fehler = await Should.ThrowAsync<KommentarSchemaException>(() => KommentarDbContext.InitialisierenAsync(d, abbruch: ct));
            fehler.Message.ShouldContain("bitte NICHT löschen");
            fehler.Message.ShouldNotContain("Datenbankschema veraltet"); // sonst schlüge ausrollen.sh -DbNeu vor
        }
    }

    [Fact]
    public async Task Alte_Kommentar_Datei_aus_der_SQLite_Zeit_wird_einmal_uebernommen()
    {
        var ct = TestContext.Current.CancellationToken;
        var alt = Path.Combine(_ordner, "kommentare.db");
        var id = new Guid("0193a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
        await using (var s = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={alt};Pooling=False"))
        {
            await s.OpenAsync(ct);
            await using var cmd = s.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Kommentare (Id BLOB, Dokument TEXT, Abschnitt TEXT, AbschnittTitel TEXT, Text TEXT, VerfasstVonKennung TEXT,
                  VerfasstVonName TEXT, VerfasstVonRolle TEXT, Am INTEGER, AntwortAuf BLOB, Erledigt INTEGER, ErledigtVonName TEXT, ErledigtAm INTEGER);
                INSERT INTO Kommentare VALUES ($id, 'spezifikation/anforderungen', 'qa', 'QA', 'Bitte prüfen', 'kliebler', 'Klaus', 'Leitung',
                  1763380800000, NULL, 1, 'Klaus', 1763384400000);
                """;
            cmd.Parameters.AddWithValue("$id", id.ToByteArray(bigEndian: true));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var d = Dienste(new() { ["Kommentare:Altdatei"] = "kommentare.db" }))
        {
            await KommentarDbContext.InitialisierenAsync(d, abbruch: ct);
            await using var bereich = d.CreateAsyncScope();
            var k = await bereich.ServiceProvider.GetRequiredService<IKommentarSpeicher>().LadenAsync(id, abbruch: ct);
            k.ShouldNotBeNull();
            k.Text.ShouldBe("Bitte prüfen");
            k.Erledigt.ShouldBeTrue();
            k.Am.ToUniversalTime().ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1763380800000));
        }

        File.Exists(alt).ShouldBeFalse();
        File.Exists(alt + ".uebernommen").ShouldBeTrue();
    }
}
