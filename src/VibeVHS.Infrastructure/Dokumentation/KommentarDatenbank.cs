using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VibeVHS.Application.Dokumentation;
using VibeVHS.Domain.Dokumentation;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Dokumentation;

/// <summary>Pfad der Kommentar-Datenbank (eigene Datei neben der Hauptdatenbank, übersteht deren Neuanlage).</summary>
public sealed record KommentarDatenbankOrt(string Verbindung)
{
    public string Datei => new SqliteConnectionStringBuilder(Verbindung).DataSource;
}

/// <summary>
/// Eigene, kleine Datenbank für Kommentare zu Spezifikation und Doku. Bewusst getrennt von <see cref="VibeVhsDbContext"/>:
/// Die Hauptdatenbank wird in der Entwicklungsphase bei Schemaänderungen neu angelegt (deploy.ps1 -DbNeu), Kommentare
/// sollen das überstehen. Gleiche Speicherformate wie die Hauptdatenbank (Ids 16 Byte, Zeitpunkte UTC-ms).
/// </summary>
public sealed class KommentarDbContext(DbContextOptions<KommentarDbContext> optionen) : DbContext(optionen)
{
    /// <summary>
    /// Schemaversion (PRAGMA user_version). Bei jeder Änderung am Modell erhöhen und die vorhandene Datei per Skript
    /// anpassen – die Kommentare dürfen nicht verloren gehen.
    /// </summary>
    public const int Schemaversion = 1;

    public DbSet<Kommentar> Kommentare => Set<Kommentar>();

    protected override void ConfigureConventions(ModelConfigurationBuilder konfiguration)
    {
        konfiguration.Properties<Guid>().HaveConversion<IdAlsBytes>();
        konfiguration.Properties<DateTimeOffset>().HaveConversion<ZeitpunktAlsMillisekunden>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Kommentar>(b =>
        {
            b.ToTable("Kommentare");
            b.HasKey(k => k.Id);
            b.Property(k => k.Id).ValueGeneratedNever();
            b.Property(k => k.Dokument).HasMaxLength(200);
            b.Property(k => k.Abschnitt).HasMaxLength(200);
            b.Property(k => k.AbschnittTitel).HasMaxLength(300);
            b.Property(k => k.Text).HasMaxLength(Kommentar.TextMaxZeichen);
            b.Property(k => k.VerfasstVonKennung).HasMaxLength(100);
            b.Property(k => k.VerfasstVonName).HasMaxLength(200);
            b.Property(k => k.VerfasstVonRolle).HasMaxLength(100);
            b.Property(k => k.ErledigtVonName).HasMaxLength(200);
            b.HasIndex(k => new { k.Dokument, k.Abschnitt });
            b.HasIndex(k => k.AntwortAuf);
        });
    }

    /// <summary>Legt die Datei bei Bedarf an und prüft die Schemaversion; WAL-Modus wie die Hauptdatenbank.</summary>
    public static async Task InitialisierenAsync(IServiceProvider dienste, CancellationToken abbruch = default)
    {
        await using var scope = dienste.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KommentarDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KommentarDbContext));
        var datei = scope.ServiceProvider.GetRequiredService<KommentarDatenbankOrt>().Datei;
        if (datei != ":memory:" && Path.GetDirectoryName(Path.GetFullPath(datei)) is { Length: > 0 } verzeichnis)
        {
            Directory.CreateDirectory(verzeichnis);
        }

        await db.Database.OpenConnectionAsync(abbruch);
        try
        {
            if (await db.Database.EnsureCreatedAsync(abbruch))
            {
                await db.Database.ExecuteSqlRawAsync($"PRAGMA user_version = {Schemaversion}", abbruch);
                log.LogInformation("Kommentar-Datenbank {Datei} neu angelegt (Schemaversion {Version})", datei, Schemaversion);
            }
            else
            {
                var version = (await db.Database.SqlQueryRaw<int>("PRAGMA user_version").ToListAsync(abbruch)).FirstOrDefault();
                if (version != Schemaversion)
                {
                    throw new KommentarSchemaException(
                        $"Kommentar-Datenbank {datei} hat Schemaversion {version}, erwartet {Schemaversion}. Die Datei enthält Kommentare " +
                        "von Leitung und Systemverwaltung – bitte NICHT löschen, sondern per Skript anpassen (siehe KommentarDbContext.Schemaversion).");
                }
            }

            await db.Database.SqlQueryRaw<string>("PRAGMA journal_mode=WAL").ToListAsync(abbruch);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

/// <summary>Kommentar-Datenbank passt nicht zum Modell. Anders als bei der Hauptdatenbank: Datei nicht löschen (enthält Kommentare).</summary>
public sealed class KommentarSchemaException(string meldung) : InvalidOperationException(meldung);

internal sealed class KommentarSpeicher(KommentarDbContext db) : IKommentarSpeicher
{
    public async Task<IReadOnlyList<Kommentar>> ZuDokumentAsync(string dokument, CancellationToken abbruch = default)
        => await db.Kommentare.Where(k => k.Dokument == dokument).OrderBy(k => k.Id).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Kommentar>> AlleAsync(CancellationToken abbruch = default)
        => await db.Kommentare.OrderBy(k => k.Dokument).ThenBy(k => k.Abschnitt).ThenBy(k => k.Id).ToListAsync(abbruch);

    public async Task<Kommentar?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => await db.Kommentare.FirstOrDefaultAsync(k => k.Id == id, abbruch);

    public void Hinzufuegen(Kommentar kommentar) => db.Kommentare.Add(kommentar);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
