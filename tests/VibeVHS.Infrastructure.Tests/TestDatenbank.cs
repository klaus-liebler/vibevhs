using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Startdaten;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>SQLite im Arbeitsspeicher mit echten Migrationen; je Test frisch.</summary>
internal sealed class TestDatenbank : IAsyncDisposable
{
    private readonly SqliteConnection _verbindung = new("Data Source=:memory:");

    public FakeTimeProvider Uhr { get; } = new(new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.FromHours(1)));

    public static async Task<TestDatenbank> ErzeugenAsync()
    {
        var t = new TestDatenbank();
        await t._verbindung.OpenAsync();
        await using var db = t.NeuerKontext();
        await db.Database.MigrateAsync();
        return t;
    }

    public VibeVhsDbContext NeuerKontext()
        => new(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(_verbindung, s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).Konfigurieren(streng: true).Options);

    public StartdatenLader Lader(VibeVhsDbContext db)
        => new(db, new AuditProtokoll(db, new Automatik(), Uhr), Uhr, NullLogger<StartdatenLader>.Instance);

    public ValueTask DisposeAsync() => _verbindung.DisposeAsync();

    private sealed class Automatik : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(Benutzer.Automatik);
    }
}
