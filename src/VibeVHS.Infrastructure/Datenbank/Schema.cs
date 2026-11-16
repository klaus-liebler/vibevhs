using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>
/// Datenbankschema ohne Migrationen (Entwicklungsphase, nur Testdaten): Die Datenbank entsteht beim ersten Start per
/// <c>EnsureCreated</c> aus dem Modell, danach werden die lesbaren Sichten <c>v_…</c> angelegt und die Schemaversion in der Tabelle
/// <c>Schemainfo</c> vermerkt. Die Version ist eine Prüfsumme über das erzeugte Schema – ändert sich das Modell, passt eine vorhandene
/// Datei nicht mehr und die Anwendung meldet das klar, statt mit unverständlichen Fehlern abzubrechen.
/// Vor dem ersten Produktivbetrieb werden wieder Migrationen eingeführt (siehe docs/betrieb.md).
/// </summary>
public static class Schema
{
    /// <summary>Prüfsumme über Tabellen, Indizes und Sichten des aktuellen Modells.</summary>
    public static string Version(VibeVhsDbContext db)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(db.Database.GenerateCreateScript() + string.Join("\n", Sichten(db.Model)))))[..16];

    /// <summary>Legt eine leere Datenbank an (Tabellen, Sichten, Schemaversion) oder prüft, ob eine vorhandene zum Modell passt.</summary>
    /// <returns><c>true</c>, wenn die Datenbank neu angelegt wurde.</returns>
    public static async Task<bool> AnlegenOderPruefenAsync(VibeVhsDbContext db, string bezeichnung, CancellationToken abbruch = default)
    {
        var soll = Version(db);
        if (await db.Database.EnsureCreatedAsync(abbruch))
        {
            foreach (var sicht in Sichten(db.Model))
            {
                await db.Database.ExecuteSqlRawAsync(sicht, abbruch);
            }

            await db.Database.ExecuteSqlRawAsync("CREATE TABLE Schemainfo (Version TEXT NOT NULL, AngelegtAm TEXT NOT NULL)", abbruch);
            await db.Database.ExecuteSqlAsync($"INSERT INTO Schemainfo (Version, AngelegtAm) VALUES ({soll}, {DateTimeOffset.UtcNow.ToString("O")})", abbruch);
            return true;
        }

        // Keine Schemainfo: Datei aus der Zeit mit Migrationen oder fremde Datei → veraltet (ohne Fehlermeldungen im Log).
        var mitInfo = (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Schemainfo'")
            .ToListAsync(abbruch)).Single() > 0;
        var ist = mitInfo ? (await db.Database.SqlQueryRaw<string>("SELECT Version AS Value FROM Schemainfo").ToListAsync(abbruch)).FirstOrDefault() : null;

        if (ist != soll)
        {
            throw new DatenbankschemaVeraltetException(bezeichnung, ist, soll);
        }

        return false;
    }

    /// <summary>
    /// Lesbare Sichten je Tabelle (<c>v_Tabelle</c>) für die Prüfung im DB-Browser: Ids als GUID-Text, Tage als JJJJ-MM-TT,
    /// Zeitpunkte und Termine als UTC-Text; bei Versionstabellen zusätzlich Zeitpunkt, Person und Grund des Änderungsvorgangs.
    /// </summary>
    public static IReadOnlyList<string> Sichten(IModel modell)
    {
        var sichten = new List<string>();
        foreach (var g in modell.GetEntityTypes().Where(e => e.GetTableName() is not null).GroupBy(e => e.GetTableName()!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var spalten = g.SelectMany(e => e.GetProperties()).GroupBy(p => p.GetColumnName()).Select(x => x.First())
                .OrderBy(p => p.IsPrimaryKey() ? 0 : 1).ThenBy(p => p.GetColumnName(), StringComparer.Ordinal).ToList();
            var ausdruecke = spalten.Select(p => $"{Lesbar(p, $"t.\"{p.GetColumnName()}\"")} AS \"{p.GetColumnName()}\"").ToList();
            var verbunden = "";
            if (spalten.Any(p => p.GetColumnName() == "ErfasstDurchVorgangId"))
            {
                verbunden = " LEFT JOIN \"Aenderungsvorgaenge\" e ON e.\"Id\" = t.\"ErfasstDurchVorgangId\" LEFT JOIN \"Aenderungsvorgaenge\" r ON r.\"Id\" = t.\"ErsetztDurchVorgangId\"";
                ausdruecke.Add($"{Zeitpunkt("e.\"Am\"")} AS \"ErfasstAm\"");
                ausdruecke.Add("e.\"Von\" AS \"ErfasstVon\"");
                ausdruecke.Add("e.\"Grund\" AS \"Grund\"");
                ausdruecke.Add($"{Zeitpunkt("r.\"Am\"")} AS \"ErsetztAm\"");
            }

            sichten.Add($"CREATE VIEW \"v_{g.Key}\" AS SELECT {string.Join(", ", ausdruecke)} FROM \"{g.Key}\" t{verbunden}");
        }

        return sichten;
    }

    private static string Lesbar(IProperty p, string s)
    {
        var typ = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
        return typ == typeof(Guid)
            ? $"lower(substr(hex({s}),1,8)||'-'||substr(hex({s}),9,4)||'-'||substr(hex({s}),13,4)||'-'||substr(hex({s}),17,4)||'-'||substr(hex({s}),21,12))"
            : typ == typeof(DateOnly)
                ? $"CASE WHEN {s} IS NULL THEN NULL ELSE printf('%04d-%02d-%02d', {s} / 10000, {s} / 100 % 100, {s} % 100) END"
                : typ == typeof(DateTimeOffset) || typ == typeof(DateTime)
                    ? Zeitpunkt(s)
                    : s;
    }

    private static string Zeitpunkt(string s) => $"CASE WHEN {s} IS NULL THEN NULL ELSE strftime('%Y-%m-%d %H:%M:%f', {s} / 1000.0, 'unixepoch') || ' UTC' END";
}

/// <summary>Die vorhandene Datenbankdatei passt nicht zum aktuellen Modell (Entwicklungsphase ohne Migrationen).</summary>
public sealed class DatenbankschemaVeraltetException(string datei, string? ist, string soll)
    : InvalidOperationException($"Datenbankschema veraltet ({datei}: Version {ist ?? "unbekannt"}, erwartet {soll}). " +
                                "Bitte die Datenbankdatei löschen oder beiseitelegen und die Anwendung neu starten – sie wird dann neu aus Start- und Demodaten aufgebaut.")
{
    public string Datei { get; } = datei;
}
