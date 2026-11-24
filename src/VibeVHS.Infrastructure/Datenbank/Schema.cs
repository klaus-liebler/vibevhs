using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>
/// Datenbankschema ohne Migrationen (Entwicklungsphase, nur Testdaten): Die Datenbank entsteht beim ersten Start per
/// <c>EnsureCreated</c> aus dem Modell, danach legt <see cref="Zusatzschema"/> an, was EF nicht aus dem Modell erzeugt (SQL-Funktionen für die
/// Suche, Trigramm-Indizes, lesbare Sichten <c>v_…</c>), und die Schemaversion wird in der Tabelle <c>Schemainfo</c> vermerkt. Die Version
/// ist eine Prüfsumme über das erzeugte Schema – ändert sich das Modell, passt eine vorhandene Datenbank nicht mehr und die Anwendung meldet
/// das klar (oder legt sie mit <c>Datenbank:BeiAbweichung = Beiseitelegen</c> unter neuem Namen beiseite und beginnt neu, NFA-27).
/// Vor dem ersten Produktivbetrieb werden wieder Migrationen eingeführt (siehe docs/betrieb.md).
/// </summary>
public static class Schema
{
    /// <summary>So viele beiseitegelegte Datenbanken (<c>…_alt_JJJJMMTT_HHMMSS</c>) bleiben erhalten, ältere werden gelöscht.</summary>
    public const int BeiseiteBehalten = 3;

    /// <summary>Prüfsumme über Tabellen, Indizes, Funktionen und Sichten des aktuellen Modells.</summary>
    public static string Version(VibeVhsDbContext db)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(db.Database.GenerateCreateScript() + string.Join("\n", Zusatzschema.Anweisungen(db.Model)))))[..16];

    /// <summary>Legt eine leere Datenbank an (Tabellen, Zusatzschema, Schemaversion) oder prüft, ob eine vorhandene zum Modell passt.</summary>
    /// <param name="beiseitelegen">Passt eine vorhandene Datenbank nicht, wird sie umbenannt statt abzubrechen (Entwicklung, Testsystem).</param>
    /// <returns><c>true</c>, wenn die Datenbank neu angelegt wurde.</returns>
    public static async Task<bool> AnlegenOderPruefenAsync(VibeVhsDbContext db, bool beiseitelegen = false, CancellationToken abbruch = default)
    {
        var soll = Version(db);
        var verbindung = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var name = verbindung.Database ?? throw new InvalidOperationException("Die Verbindungszeichenfolge nennt keine Datenbank (Database=…).");
        if (await DatenbankVorhandenAsync(verbindung, abbruch))
        {
            var ist = await GespeicherteVersionAsync(db, abbruch);
            if (ist == soll)
            {
                return false;
            }

            var leer = ist is null && await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM pg_tables WHERE schemaname = 'public'").SingleAsync(abbruch) == 0;
            if (!leer)
            {
                if (!beiseitelegen)
                {
                    throw new DatenbankschemaVeraltetException(name, ist, soll);
                }

                await BeiseitelegenAsync(verbindung, abbruch);
            }
        }

        await db.Database.EnsureCreatedAsync(abbruch);
        foreach (var sql in Zusatzschema.Anweisungen(db.Model))
        {
            await db.Database.ExecuteSqlRawAsync(sql, abbruch);
        }

        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"Schemainfo\" (\"Version\" text NOT NULL, \"AngelegtAm\" timestamptz NOT NULL DEFAULT now())", abbruch);
        await db.Database.ExecuteSqlAsync($"INSERT INTO \"Schemainfo\" (\"Version\") VALUES ({soll})", abbruch);
        return true;
    }

    /// <summary>Schemaversion einer Datenbank (null, wenn sie keine Schemainfo hat).</summary>
    public static async Task<string?> GespeicherteVersionAsync(DbContext db, CancellationToken abbruch = default)
    {
        var vorhanden = await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"Schemainfo\"') IS NOT NULL AS \"Value\"").SingleAsync(abbruch);
        return vorhanden
            ? (await db.Database.SqlQueryRaw<string>("SELECT \"Version\" AS \"Value\" FROM \"Schemainfo\" LIMIT 1").ToListAsync(abbruch)).FirstOrDefault()
            : null;
    }

    /// <summary>Verbindung zur Verwaltungsdatenbank „postgres“ desselben Servers (Anlegen, Umbenennen, Löschen ganzer Datenbanken).</summary>
    public static NpgsqlConnection Verwaltungsverbindung(NpgsqlConnectionStringBuilder verbindung)
        => new(new NpgsqlConnectionStringBuilder(verbindung.ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString);

    public static async Task<bool> DatenbankVorhandenAsync(NpgsqlConnectionStringBuilder verbindung, CancellationToken abbruch = default)
    {
        await using var con = Verwaltungsverbindung(verbindung);
        await con.OpenAsync(abbruch);
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", con);
        cmd.Parameters.AddWithValue("n", verbindung.Database!);
        return await cmd.ExecuteScalarAsync(abbruch) is not null;
    }

    /// <summary>
    /// Benennt die Datenbank in <c>name_alt_JJJJMMTT_HHMMSS</c> um (NFA-27: beiseitelegen statt löschen) und löscht ältere beiseitegelegte
    /// Datenbanken über <see cref="BeiseiteBehalten"/> hinaus. Offene Verbindungen auf die Datenbank werden vorher beendet.
    /// </summary>
    public static async Task<string> BeiseitelegenAsync(NpgsqlConnectionStringBuilder verbindung, CancellationToken abbruch = default)
    {
        var name = verbindung.Database!;
        var neu = $"{name}_alt_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        NpgsqlConnection.ClearAllPools();
        await using var con = Verwaltungsverbindung(verbindung);
        await con.OpenAsync(abbruch);
        await AusfuehrenAsync(con, $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}' AND pid <> pg_backend_pid()", abbruch);
        await AusfuehrenAsync(con, $"ALTER DATABASE \"{name}\" RENAME TO \"{neu}\"", abbruch);
        var alte = new List<string>();
        await using (var cmd = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datname LIKE @m ORDER BY datname DESC", con))
        {
            cmd.Parameters.AddWithValue("m", name.Replace("_", "\\_", StringComparison.Ordinal) + "\\_alt\\_%");
            await using var r = await cmd.ExecuteReaderAsync(abbruch);
            while (await r.ReadAsync(abbruch))
            {
                alte.Add(r.GetString(0));
            }
        }

        foreach (var alt in alte.Skip(BeiseiteBehalten))
        {
            await AusfuehrenAsync(con, $"DROP DATABASE IF EXISTS \"{alt}\" WITH (FORCE)", abbruch);
        }

        return neu;
    }

    private static async Task AusfuehrenAsync(NpgsqlConnection con, string sql, CancellationToken abbruch)
    {
        await using var cmd = new NpgsqlCommand(sql, con);
        await cmd.ExecuteNonQueryAsync(abbruch);
    }
}

/// <summary>
/// SQL, das EF nicht aus dem Modell erzeugt: Suchfunktionen (gleiche Normalisierung wie <see cref="Domain.Gemeinsam.Suchtext"/>),
/// Trigramm-Indizes (pg_trgm) für die Suche nach Wortanfängen und die unscharfe Suche, lesbare Sichten <c>v_…</c>.
/// Alle Funktionen sind schemaqualifiziert, damit Indizes auch beim Wiederherstellen einer Sicherung (leerer search_path) gebaut werden.
/// </summary>
public static class Zusatzschema
{
    /// <summary>Tabellen und Ausdrücke, die einen Trigramm-Index für die Suche bekommen (Name, Titel, Wohnort, E-Mail, Orte, Kurse).</summary>
    private static readonly (string Tabelle, string Ausdruck)[] Suchindizes =
    [
        ("PersonNamen", "public.namensuchtext(\"Vorname\", \"Vorsatz\", \"Nachname\", \"Zusatz\")"),
        ("PersonMerkmale", "public.suchwoerter(\"Inhalt\")"),
        ("PersonAdressen", "public.suchwoerter(\"Ort\")"),
        ("PersonEMails", "public.suchwoerter(\"Adresse\")"),
    ];

    public static IEnumerable<string> Anweisungen(IModel modell)
    {
        // suchtext(x): wie Suchtext.Normalisieren – klein, ß/ı/ł/ø/æ/œ/đ umschreiben, Akzente weg (unaccent), Satzzeichen trennen Wörter,
        // ä/ae, ö/oe, ü/ue gleich, Leerraum zusammenfassen. IMMUTABLE, damit Indizes darauf möglich sind (unaccent mit festem Wörterbuch).
        yield return """
            CREATE OR REPLACE FUNCTION public.suchtext(t text) RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
              SELECT coalesce(btrim(regexp_replace(
                replace(replace(replace(
                  regexp_replace(
                    public.unaccent('public.unaccent'::regdictionary,
                      replace(replace(replace(translate(lower(t), 'ıłøđ', 'ilod'), 'ß', 'ss'), 'æ', 'ae'), 'œ', 'oe')),
                    '[^[:alnum:]@.]+', ' ', 'g'),
                  'ae', 'a'), 'oe', 'o'), 'ue', 'u'),
                ' +', ' ', 'g')), '')
            $$;
            """;
        // Wortanfang-Suche: führendes Leerzeichen, damit „LIKE '% wort%'“ nur Wortanfänge trifft.
        yield return """
            CREATE OR REPLACE FUNCTION public.suchwoerter(t text) RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
              SELECT ' ' || public.suchtext(t)
            $$;
            """;
        yield return """
            CREATE OR REPLACE FUNCTION public.namensuchtext(vorname text, vorsatz text, nachname text, zusatz text) RETURNS text
              LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
              SELECT ' ' || public.suchtext(concat_ws(' ', vorname, vorsatz, nachname, zusatz))
            $$;
            """;
        // Seitenschlüssel der Personenliste: Id als Großbuchstaben-Hex ohne Bindestriche (gleiche Reihenfolge wie die uuid).
        yield return """
            CREATE OR REPLACE FUNCTION public.idhex(id uuid) RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
              SELECT replace(upper(id::text), '-', '')
            $$;
            """;

        foreach (var (tabelle, ausdruck) in Suchindizes)
        {
            yield return $"CREATE INDEX \"IX_{tabelle}_Suche\" ON \"{tabelle}\" USING gin ({ausdruck} gin_trgm_ops);";
        }

        foreach (var sicht in Sichten(modell))
        {
            yield return sicht;
        }
    }

    /// <summary>
    /// Lesbare Sichten je Tabelle (<c>v_Tabelle</c>) für die Prüfung im DB-Werkzeug: bei Versionstabellen zusätzlich Zeitpunkt, Person und
    /// Grund des Änderungsvorgangs; offenes Ende (9999-12-31) als NULL.
    /// </summary>
    public static IReadOnlyList<string> Sichten(IModel modell)
    {
        var sichten = new List<string>();
        foreach (var tabelle in modell.GetRelationalModel().Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var spalten = tabelle.Columns.Select(c => c.Name).Distinct()
                .OrderBy(c => tabelle.PrimaryKey?.Columns.Any(k => k.Name == c) == true ? 0 : 1).ThenBy(c => c, StringComparer.Ordinal).ToList();
            var ausdruecke = spalten.Select(c => c == "GueltigBis" ? "nullif(t.\"GueltigBis\", DATE '9999-12-31') AS \"GueltigBis\"" : $"t.\"{c}\"").ToList();
            var verbunden = "";
            if (spalten.Contains("ErfasstDurchVorgangId"))
            {
                verbunden = " LEFT JOIN \"Aenderungsvorgaenge\" e ON e.\"Id\" = t.\"ErfasstDurchVorgangId\" LEFT JOIN \"Aenderungsvorgaenge\" r ON r.\"Id\" = t.\"ErsetztDurchVorgangId\"";
                ausdruecke.Add("e.\"Am\" AS \"ErfasstAm\"");
                ausdruecke.Add("e.\"Von\" AS \"ErfasstVon\"");
                ausdruecke.Add("e.\"Grund\" AS \"Grund\"");
                ausdruecke.Add("r.\"Am\" AS \"ErsetztAm\"");
            }

            sichten.Add($"CREATE VIEW \"v_{tabelle.Name}\" AS SELECT {string.Join(", ", ausdruecke)} FROM \"{tabelle.Name}\" t{verbunden}");
        }

        return sichten;
    }
}

/// <summary>Die vorhandene Datenbank passt nicht zum aktuellen Modell (Entwicklungsphase ohne Migrationen).</summary>
public sealed class DatenbankschemaVeraltetException(string datenbank, string? ist, string soll)
    : InvalidOperationException($"Datenbankschema veraltet (Datenbank „{datenbank}“: Version {ist ?? "unbekannt"}, erwartet {soll}). " +
                                "Bitte die Datenbank beiseitelegen (Datenbank:BeiAbweichung = Beiseitelegen oder deploy.ps1 -DbNeu) und die Anwendung neu starten – " +
                                "sie wird dann neu aus Start- und Demodaten aufgebaut.")
{
    public string Datenbank { get; } = datenbank;
}
