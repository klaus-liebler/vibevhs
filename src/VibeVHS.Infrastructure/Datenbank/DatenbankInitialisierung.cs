using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VibeVHS.Infrastructure.Datenbank;

public static class DatenbankInitialisierung
{
    /// <summary>
    /// Legt das Datenverzeichnis an, spielt Migrationen ein und schaltet SQLite in den WAL-Modus
    /// (paralleles Lesen wÃ¤hrend geschrieben wird; die Einstellung bleibt in der Datei gespeichert).
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

        await db.Database.MigrateAsync(abbruch);
        var modus = await db.Database.SqlQueryRaw<string>("PRAGMA journal_mode=WAL").ToListAsync(abbruch);
        await SortierschluesselNachtragenAsync(db, log, abbruch);
        await Personen.PersonenSuchindex.AufbauenFallsNoetigAsync(db, log, abbruch);
        log.LogInformation("Datenbank {Datei} bereit, Journal-Modus {Modus}", datei, modus.FirstOrDefault());
    }

    /// <summary>
    /// FÃ¼llt den abgeleiteten SortierschlÃ¼ssel der Namensversionen nach, die vor seiner EinfÃ¼hrung erfasst wurden (Normalisierung nach
    /// DIN 5007-2 geschieht in C#, nicht in SQL). Danach ist nichts mehr leer; neue Versionen bringen ihn aus dem Konstruktor mit.
    /// </summary>
    internal static async Task SortierschluesselNachtragenAsync(VibeVhsDbContext db, ILogger log, CancellationToken abbruch = default)
    {
        var leer = await db.Set<Domain.Personen.PersonName>().AsNoTracking().Where(n => n.Sortierschluessel == "").OrderBy(n => n.Id).ToListAsync(abbruch);
        if (leer.Count == 0)
        {
            return;
        }

        log.LogInformation("SortierschlÃ¼ssel fÃ¼r {Anzahl} Namensversionen werden nachgetragen", leer.Count);
        await using var tx = await db.Database.BeginTransactionAsync(abbruch);
        foreach (var n in leer)
        {
            var schluessel = Domain.Gemeinsam.Suchtext.Normalisieren(n.Sortierform);
            await db.Set<Domain.Personen.PersonName>().Where(x => x.Id == n.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Sortierschluessel, schluessel), abbruch);
        }

        await tx.CommitAsync(abbruch);
    }
}
