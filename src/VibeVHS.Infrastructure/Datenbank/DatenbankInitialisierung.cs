using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VibeVHS.Infrastructure.Datenbank;

public static class DatenbankInitialisierung
{
    /// <summary>
    /// Legt die Datenbanken aller Mandanten bei Bedarf neu an bzw. prüft ihre Schemaversion (siehe <see cref="Schema"/>), danach die
    /// Kommentar-Datenbank. <c>Datenbank:BeiAbweichung = Beiseitelegen</c> benennt eine veraltete Datenbank um und legt sie neu an (NFA-27);
    /// Standard ist „Abbrechen“ mit klarer Meldung.
    /// </summary>
    public static async Task InitialisierenAsync(IServiceProvider dienste, CancellationToken abbruch = default)
    {
        var konfiguration = dienste.GetRequiredService<IConfiguration>();
        var beiseitelegen = string.Equals(konfiguration["Datenbank:BeiAbweichung"], "Beiseitelegen", StringComparison.OrdinalIgnoreCase);
        foreach (var mandant in dienste.GetRequiredService<Application.Betrieb.Mandanten>().Alle)
        {
            await using var scope = dienste.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<Application.Betrieb.Mandantenkontext>().Setzen(mandant);
            var db = scope.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatenbankInitialisierung));
            var name = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database;
            if (await Schema.AnlegenOderPruefenAsync(db, beiseitelegen, abbruch))
            {
                log.LogInformation("Datenbank {Datenbank} (Mandant {Mandant}) neu angelegt (Schemaversion {Version})", name, mandant.Kennung, Schema.Version(db));
            }
            else
            {
                log.LogInformation("Datenbank {Datenbank} (Mandant {Mandant}) bereit", name, mandant.Kennung);
            }
        }

        await Dokumentation.KommentarDbContext.InitialisierenAsync(dienste, abbruch);
    }

    /// <summary>
    /// Aktualisiert die Planerstatistik (ANALYZE) der Datenbank des aktuellen Mandanten – nach Massenladen (Demodaten, Import), damit
    /// PostgreSQL nicht wie für leere Tabellen plant, bis autovacuum nachzieht.
    /// </summary>
    public static async Task StatistikAktualisierenAsync(IServiceProvider dienste, CancellationToken abbruch = default)
    {
        await using var scope = dienste.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Database.ExecuteSqlRawAsync("ANALYZE", abbruch);
    }
}
