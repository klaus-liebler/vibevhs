using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Hilfe (nur auf Anforderung, VIBEVHS_SPALTENLISTE=Pfad): Spalten je Speicherformat aus dem Modell, für feste Listen in Migrationen.</summary>
public sealed class SpaltenlisteErzeugen
{
    [Fact]
    public void Spaltenliste()
    {
        if (Environment.GetEnvironmentVariable("VIBEVHS_SPALTENLISTE") is not { Length: > 0 } pfad)
        {
            return;
        }

        using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(new SqliteConnection("Data Source=:memory:")).Options);
        var zeilen = new List<string>();
        foreach (var e in db.Model.GetEntityTypes().Where(e => e.GetTableName() is not null).OrderBy(e => e.GetTableName(), StringComparer.Ordinal))
        {
            foreach (var p in e.GetProperties().OrderBy(p => p.GetColumnName(), StringComparer.Ordinal))
            {
                var typ = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                var art = typ == typeof(Guid) ? "Id" : typ == typeof(DateOnly) ? "Tag" : typ == typeof(DateTimeOffset) ? "Zeitpunkt" : typ == typeof(DateTime) ? "Termin" : null;
                if (art is not null)
                {
                    zeilen.Add($"(\"{e.GetTableName()}\", \"{p.GetColumnName()}\", Art.{art}),");
                }
            }
        }

        File.WriteAllLines(pfad, zeilen.Distinct());
    }
}
