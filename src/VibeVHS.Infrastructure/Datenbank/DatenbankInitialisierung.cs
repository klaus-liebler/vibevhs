using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VibeVHS.Infrastructure.Datenbank;

public static class DatenbankInitialisierung
{
    /// <summary>
    /// Legt das Datenverzeichnis an, legt die Datenbank bei Bedarf neu an (bzw. prüft die Schemaversion, siehe <see cref="Schema"/>) und
    /// schaltet SQLite in den WAL-Modus
    /// (paralleles Lesen während geschrieben wird; die Einstellung bleibt in der Datei gespeichert).
    /// </summary>
    public static async Task InitialisierenAsync(IServiceProvider dienste, CancellationToken abbruch = default)
    {
        await using var scope = dienste.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatenbankInitialisierung));

        var verbindung = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        var datei = Path.GetFullPath(verbindung.DataSource);
        var verzeichnis = Path.GetDirectoryName(datei);
        if (!string.IsNullOrEmpty(verzeichnis))
        {
            Directory.CreateDirectory(verzeichnis);
        }

        if (await Schema.AnlegenOderPruefenAsync(db, datei, abbruch))
        {
            log.LogInformation("Datenbank {Datei} neu angelegt (Schemaversion {Version})", datei, Schema.Version(db));
        }

        var modus = await db.Database.SqlQueryRaw<string>("PRAGMA journal_mode=WAL").ToListAsync(abbruch);
        log.LogInformation("Datenbank {Datei} bereit, Journal-Modus {Modus}", datei, modus.FirstOrDefault());

        await Dokumentation.KommentarDbContext.InitialisierenAsync(dienste, abbruch);
    }
}
