using Microsoft.EntityFrameworkCore;
using Npgsql;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Tests.Gemeinsam;

/// <summary>Vorlage und Wegwerf-Datenbanken für Tests (auch von den Web-Tests genutzt, siehe ShopTestHost).</summary>
internal static class Wegwerfdatenbanken
{
    public static readonly string Basisverbindung = Environment.GetEnvironmentVariable("VIBEVHS_TEST_DB")
        ?? "Host=127.0.0.1;Port=5441;Username=vibevhs;Password=entwicklung";

    private static readonly SemaphoreSlim Sperre = new(1, 1);
    private static string? _vorlage;

    public static DbContextOptionsBuilder<VibeVhsDbContext> Optionen(string verbindung)
        => new DbContextOptionsBuilder<VibeVhsDbContext>().UseNpgsql(verbindung, s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

    public static string Mit(string datenbank, bool pooling = true)
        => new NpgsqlConnectionStringBuilder(Basisverbindung) { Database = datenbank, Pooling = pooling }.ConnectionString;

    /// <summary>Legt eine Kopie der Vorlage an und gibt ihre Verbindungszeichenfolge zurück.</summary>
    public static async Task<string> NeuAsync(string praefix)
    {
        var vorlage = await VorlageAsync();
        var name = $"{praefix}_{Guid.NewGuid():N}"[..Math.Min(60, praefix.Length + 33)];
        await VerwaltenAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{vorlage}\"");
        return Mit(name);
    }

    /// <summary>Leere Datenbank ohne Schema (für Tests der Anlage selbst).</summary>
    public static async Task<string> LeerAsync(string praefix)
    {
        var name = $"{praefix}_{Guid.NewGuid():N}"[..Math.Min(60, praefix.Length + 33)];
        await VerwaltenAsync($"CREATE DATABASE \"{name}\" TEMPLATE template0");
        return Mit(name);
    }

    public static async Task LoeschenAsync(string verbindung)
    {
        await using (var con = new NpgsqlConnection(verbindung))
        {
            NpgsqlConnection.ClearPool(con);
        }

        await VerwaltenAsync($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(verbindung).Database}\" WITH (FORCE)");
    }

    private static async Task<string> VorlageAsync()
    {
        if (_vorlage is not null)
        {
            return _vorlage;
        }

        await Sperre.WaitAsync();
        try
        {
            if (_vorlage is not null)
            {
                return _vorlage;
            }

            string version;
            await using (var probe = new VibeVhsDbContext(Optionen(Mit("postgres", pooling: false)).Options))
            {
                version = Schema.Version(probe).ToLowerInvariant();
            }

            var name = $"vibevhs_testvorlage_{version}";
            // Mehrere Testprozesse gleichzeitig: Sperre auf dem Server, damit die Vorlage nur einmal gebaut wird.
            await using var admin = new NpgsqlConnection(Mit("postgres", pooling: false));
            await admin.OpenAsync();
            await Ausfuehren(admin, "SELECT pg_advisory_lock(4815)");
            try
            {
                await using var pruefen = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", admin);
                pruefen.Parameters.AddWithValue("n", name);
                if (await pruefen.ExecuteScalarAsync() is null)
                {
                    var bau = name + "_bau";
                    await Ausfuehren(admin, $"DROP DATABASE IF EXISTS \"{bau}\" WITH (FORCE)");
                    await using (var db = new VibeVhsDbContext(Optionen(Mit(bau, pooling: false)).Options))
                    {
                        await Schema.AnlegenOderPruefenAsync(db);
                    }

                    await Ausfuehren(admin, $"ALTER DATABASE \"{bau}\" RENAME TO \"{name}\"");
                    await Ausfuehren(admin, $"ALTER DATABASE \"{name}\" WITH IS_TEMPLATE true");
                    // Alte Vorlagen früherer Schemaversionen aufräumen.
                    await using var alt = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datname LIKE 'vibevhs\\_testvorlage\\_%' AND datname <> @n", admin);
                    alt.Parameters.AddWithValue("n", name);
                    var alte = new List<string>();
                    await using (var r = await alt.ExecuteReaderAsync())
                    {
                        while (await r.ReadAsync())
                        {
                            alte.Add(r.GetString(0));
                        }
                    }

                    foreach (var a in alte)
                    {
                        await Ausfuehren(admin, $"ALTER DATABASE \"{a}\" WITH IS_TEMPLATE false");
                        await Ausfuehren(admin, $"DROP DATABASE IF EXISTS \"{a}\" WITH (FORCE)");
                    }
                }
            }
            finally
            {
                await Ausfuehren(admin, "SELECT pg_advisory_unlock(4815)");
            }

            return _vorlage = name;
        }
        finally
        {
            Sperre.Release();
        }
    }

    private static async Task VerwaltenAsync(string sql)
    {
        await using var admin = new NpgsqlConnection(Mit("postgres", pooling: false));
        await admin.OpenAsync();
        await Ausfuehren(admin, sql);
    }

    private static async Task Ausfuehren(NpgsqlConnection con, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, con) { CommandTimeout = 300 };
        await cmd.ExecuteNonQueryAsync();
    }
}
