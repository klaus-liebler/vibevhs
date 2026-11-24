using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Startdaten;
using VibeVHS.Tests.Gemeinsam;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Wegwerf-Datenbank je Test im lokalen PostgreSQL-Container (deploy/docker-compose.yml), Schema wie im Betrieb. Jede Datenbank ist eine
/// Kopie der Vorlage <c>vibevhs_testvorlage_&lt;Schemaversion&gt;</c> (<c>CREATE DATABASE … TEMPLATE</c>, wenige Millisekunden), die
/// Vorlage entsteht einmal je Schemaversion. Verbindung über VIBEVHS_TEST_DB, Standard ist der Entwicklungscontainer.
/// </summary>
internal sealed class TestDatenbank : IAsyncDisposable
{
    private TestDatenbank(string verbindung) => Verbindung = verbindung;

    public string Verbindung { get; }

    public string Name => new NpgsqlConnectionStringBuilder(Verbindung).Database!;

    public FakeTimeProvider Uhr { get; } = new(new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.FromHours(1)));

    /// <summary>Neue, leere Datenbank mit Schema (Kopie der Vorlage).</summary>
    public static async Task<TestDatenbank> ErzeugenAsync()
        => new(await Wegwerfdatenbanken.NeuAsync("vibevhs_test"));

    public VibeVhsDbContext NeuerKontext()
        => new(Wegwerfdatenbanken.Optionen(Verbindung).Konfigurieren(streng: true).AddInterceptors(StichzeitBeimLaden.Instanz).Options, Uhr);

    public StartdatenLader Lader(VibeVhsDbContext db)
        => new(db, new AuditProtokoll(db, new Automatik(), Uhr), Uhr, NullLogger<StartdatenLader>.Instance);

    public ValueTask DisposeAsync() => new(Wegwerfdatenbanken.LoeschenAsync(Verbindung));

    private sealed class Automatik : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(Benutzer.Automatik);
    }
}
