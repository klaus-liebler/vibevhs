using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VibeVHS.Application.Dokumentation;
using VibeVHS.Domain.Dokumentation;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Dokumentation;

/// <summary>Verbindung der Kommentar-Datenbank und optional eine alte SQLite-Datei (kommentare.db), die einmal übernommen wird.</summary>
public sealed record KommentarDatenbankOrt(string Verbindung, string? Altdatei = null)
{
    public string Datenbank => new Npgsql.NpgsqlConnectionStringBuilder(Verbindung).Database ?? string.Empty;
}

/// <summary>
/// Eigene, kleine Datenbank für Kommentare zu Spezifikation und Doku. Bewusst getrennt von <see cref="VibeVhsDbContext"/>:
/// Die Hauptdatenbank wird in der Entwicklungsphase bei Schemaänderungen beiseitegelegt und neu angelegt (deploy.ps1 -DbNeu), Kommentare
/// sollen das überstehen. Gleiche Speicherformate wie die Hauptdatenbank (uuid, timestamptz in UTC).
/// </summary>
public sealed class KommentarDbContext(DbContextOptions<KommentarDbContext> optionen) : DbContext(optionen)
{
    /// <summary>
    /// Schemaversion (Tabelle „Schemainfo“). Bei jeder Änderung am Modell erhöhen und die vorhandene Datenbank per Skript
    /// anpassen – die Kommentare dürfen nicht verloren gehen.
    /// </summary>
    public const int Schemaversion = 1;

    public DbSet<Kommentar> Kommentare => Set<Kommentar>();

    protected override void ConfigureConventions(ModelConfigurationBuilder konfiguration)
        => konfiguration.Properties<DateTimeOffset>().HaveConversion<ZeitpunktInUtc>();

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

    /// <summary>
    /// Legt die Datenbank bei Bedarf an und prüft die Schemaversion. Ist sie leer und gibt es eine alte Kommentar-Datei aus der SQLite-Zeit
    /// (<see cref="KommentarDatenbankOrt.Altdatei"/>), werden deren Kommentare einmal übernommen; die Datei bekommt die Endung „.uebernommen“.
    /// </summary>
    public static async Task InitialisierenAsync(IServiceProvider dienste, CancellationToken abbruch = default)
    {
        await using var scope = dienste.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KommentarDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KommentarDbContext));
        var ort = scope.ServiceProvider.GetRequiredService<KommentarDatenbankOrt>();
        var verbindung = new Npgsql.NpgsqlConnectionStringBuilder(ort.Verbindung);
        var neu = !await Schema.DatenbankVorhandenAsync(verbindung, abbruch)
                  || await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"Kommentare\"') IS NULL AS \"Value\"").SingleAsync(abbruch);
        if (neu)
        {
            await db.Database.EnsureCreatedAsync(abbruch);
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"Schemainfo\" (\"Version\" integer NOT NULL)", abbruch);
            await db.Database.ExecuteSqlAsync($"INSERT INTO \"Schemainfo\" (\"Version\") VALUES ({Schemaversion})", abbruch);
            log.LogInformation("Kommentar-Datenbank {Datenbank} neu angelegt (Schemaversion {Version})", ort.Datenbank, Schemaversion);
        }
        else
        {
            var version = (await db.Database.SqlQueryRaw<int>("SELECT \"Version\" AS \"Value\" FROM \"Schemainfo\" LIMIT 1").ToListAsync(abbruch)).FirstOrDefault();
            if (version != Schemaversion)
            {
                throw new KommentarSchemaException(
                    $"Kommentar-Datenbank {ort.Datenbank} hat Schemaversion {version}, erwartet {Schemaversion}. Sie enthält Kommentare " +
                    "von Leitung und Systemverwaltung – bitte NICHT löschen, sondern per Skript anpassen (siehe KommentarDbContext.Schemaversion).");
            }
        }

        if (ort.Altdatei is { } alt && File.Exists(alt) && !await db.Kommentare.AnyAsync(abbruch))
        {
            var anzahl = await AltdateiUebernehmenAsync(db, alt, abbruch);
            File.Move(alt, alt + ".uebernommen", overwrite: true);
            log.LogInformation("{Anzahl} Kommentare aus {Datei} übernommen", anzahl, alt);
        }
    }

    /// <summary>Liest die Kommentare einer alten SQLite-Datei (Ids 16 Byte Big Endian, Zeitpunkte UTC-ms) und schreibt sie unverändert.</summary>
    internal static async Task<int> AltdateiUebernehmenAsync(KommentarDbContext db, string datei, CancellationToken abbruch)
    {
        await using var alt = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = datei, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await alt.OpenAsync(abbruch);
        await using var cmd = alt.CreateCommand();
        cmd.CommandText = "SELECT Id, Dokument, Abschnitt, AbschnittTitel, Text, VerfasstVonKennung, VerfasstVonName, VerfasstVonRolle, Am, " +
                          "AntwortAuf, Erledigt, ErledigtVonName, ErledigtAm FROM Kommentare";
        await using var r = await cmd.ExecuteReaderAsync(abbruch);
        var anzahl = 0;
        static Guid Id(object o) => new((byte[])o, bigEndian: true);
        static DateTimeOffset Zeit(object o) => DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(o, System.Globalization.CultureInfo.InvariantCulture));
        while (await r.ReadAsync(abbruch))
        {
            object? Wert(int i) => r.IsDBNull(i) ? null : r.GetValue(i);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "Kommentare" ("Id", "Dokument", "Abschnitt", "AbschnittTitel", "Text", "VerfasstVonKennung", "VerfasstVonName",
                  "VerfasstVonRolle", "Am", "AntwortAuf", "Erledigt", "ErledigtVonName", "ErledigtAm")
                VALUES ({Id(r.GetValue(0))}, {r.GetString(1)}, {r.GetString(2)}, {r.GetString(3)}, {r.GetString(4)}, {r.GetString(5)}, {r.GetString(6)},
                  {(string?)Wert(7)}, {Zeit(r.GetValue(8))}, {(Wert(9) is { } a ? Id(a) : (Guid?)null)}, {Convert.ToInt64(r.GetValue(10), System.Globalization.CultureInfo.InvariantCulture) != 0},
                  {(string?)Wert(11)}, {(Wert(12) is { } e ? Zeit(e) : (DateTimeOffset?)null)})
                """, abbruch);
            anzahl++;
        }

        return anzahl;
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
