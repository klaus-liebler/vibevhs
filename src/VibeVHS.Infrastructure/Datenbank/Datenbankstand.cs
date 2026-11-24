using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VibeVHS.Infrastructure.Dokumentation;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>Was ein Start dieser Version mit einer Datenbank tun würde.</summary>
public enum Standart
{
    /// <summary>Datenbank fehlt oder ist leer: wird beim Start angelegt (bei der Hauptdatenbank mit Start- und Demodaten).</summary>
    Neu,

    /// <summary>Schema passt zu dieser Version.</summary>
    Aktuell,

    /// <summary>Schema weicht ab, es gibt aber ausstehende EF-Migrationen, die der Start einspielt.</summary>
    Migrierbar,

    /// <summary>Schema weicht ab, keine Migration: der Start bricht ab („Datenbankschema veraltet“), nur ein Neuaufbau hilft.</summary>
    Veraltet,

    /// <summary>Server nicht erreichbar oder Anmeldung abgelehnt.</summary>
    KeineVerbindung,
}

/// <param name="Rolle">„haupt:&lt;mandant&gt;“ oder „kommentare“.</param>
public sealed record Datenbankzustand(string Rolle, string Datenbank, Standart Stand, string? Ist, string Soll, string Hinweis);

/// <summary>
/// Prüft vor dem Ausrollen (ausrollen.sh, <c>VibeVHS.Web --datenbankstand</c>), was die NEUE Version mit den vorhandenen Datenbanken tun würde –
/// ohne etwas zu ändern. So bricht das Ausrollen ab, bevor die laufende Version gestoppt wird, und sagt, was zu tun ist.
/// </summary>
public static class Datenbankstand
{
    /// <summary>Exitcodes für Skripte: 0 = startbereit, 3 = Neuaufbau nötig (Hauptdatenbank veraltet), 4 = Kommentar-Datenbank veraltet, 5 = keine Verbindung.</summary>
    public static int Exitcode(IReadOnlyList<Datenbankzustand> zustaende) =>
        zustaende.Any(z => z.Stand == Standart.KeineVerbindung) ? 5
        : zustaende.Any(z => z.Stand == Standart.Veraltet && z.Rolle == "kommentare") ? 4
        : zustaende.Any(z => z.Stand == Standart.Veraltet) ? 3
        : 0;

    public static async Task<IReadOnlyList<Datenbankzustand>> PruefenAsync(IServiceProvider dienste, CancellationToken abbruch = default)
    {
        var ergebnis = new List<Datenbankzustand>();
        foreach (var mandant in dienste.GetRequiredService<Application.Betrieb.Mandanten>().Alle)
        {
            await using var scope = dienste.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<Application.Betrieb.Mandantenkontext>().Setzen(mandant);
            var db = scope.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            ergebnis.Add(await HauptAsync($"haupt:{mandant.Kennung}", db, abbruch));
        }

        await using (var scope = dienste.CreateAsyncScope())
        {
            var ort = scope.ServiceProvider.GetRequiredService<KommentarDatenbankOrt>();
            ergebnis.Add(await KommentareAsync(scope.ServiceProvider.GetRequiredService<KommentarDbContext>(), ort, abbruch));
        }

        return ergebnis;
    }

    public static async Task<Datenbankzustand> HauptAsync(string rolle, VibeVhsDbContext db, CancellationToken abbruch = default)
    {
        var soll = Schema.Version(db);
        var verbindung = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var name = verbindung.Database ?? "?";
        try
        {
            if (!await Schema.DatenbankVorhandenAsync(verbindung, abbruch))
            {
                return new(rolle, name, Standart.Neu, null, soll, "fehlt – wird beim Start angelegt");
            }

            var ist = await Schema.GespeicherteVersionAsync(db, abbruch);
            if (ist == soll)
            {
                return new(rolle, name, Standart.Aktuell, ist, soll, "passt");
            }

            var tabellen = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_tables WHERE schemaname = 'public'").SingleAsync(abbruch);
            if (ist is null && tabellen == 0)
            {
                return new(rolle, name, Standart.Neu, null, soll, "leer – wird beim Start angelegt");
            }

            // Ab der Einführung von EF-Migrationen (vor dem Produktivbetrieb): ausstehende Migrationen spielt der Start ein.
            if (db.Database.GetMigrations().Any())
            {
                var offen = (await db.Database.GetPendingMigrationsAsync(abbruch)).ToList();
                if (offen.Count > 0)
                {
                    return new(rolle, name, Standart.Migrierbar, ist, soll, $"{offen.Count} Migration(en): {string.Join(", ", offen)}");
                }
            }

            return new(rolle, name, Standart.Veraltet, ist, soll, "Schema geändert, keine Migration – Neuaufbau nötig (deploy.ps1 -DbNeu)");
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return new(rolle, name, Standart.KeineVerbindung, null, soll, ex.Message);
        }
    }

    private static async Task<Datenbankzustand> KommentareAsync(KommentarDbContext db, KommentarDatenbankOrt ort, CancellationToken abbruch)
    {
        var soll = KommentarDbContext.Schemaversion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (!await Schema.DatenbankVorhandenAsync(new NpgsqlConnectionStringBuilder(ort.Verbindung), abbruch)
                || await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"Kommentare\"') IS NULL AS \"Value\"").SingleAsync(abbruch))
            {
                var alt = ort.Altdatei is { } a && File.Exists(a) ? $" (übernimmt {a})" : "";
                return new("kommentare", ort.Datenbank, Standart.Neu, null, soll, "fehlt – wird beim Start angelegt" + alt);
            }

            var ist = (await db.Database.SqlQueryRaw<int>("SELECT \"Version\" AS \"Value\" FROM \"Schemainfo\" LIMIT 1").ToListAsync(abbruch)).FirstOrDefault()
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ist == soll
                ? new("kommentare", ort.Datenbank, Standart.Aktuell, ist, soll, "passt")
                : new("kommentare", ort.Datenbank, Standart.Veraltet, ist, soll, "enthält echte Kommentare – NICHT neu aufbauen, per Skript anpassen");
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return new("kommentare", ort.Datenbank, Standart.KeineVerbindung, null, soll, ex.Message);
        }
    }
}
