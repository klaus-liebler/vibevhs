using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Migration „VorlagenAnredestil“: gespeicherte Vorlagen bekommen {Anrede} und den förmlichen Stil; eigene Texte bleiben.</summary>
public sealed class VorlagenAnredeMigrationTests
{
    [Fact]
    public async Task Gespeicherte_Vorlagen_bekommen_den_Platzhalter_Anrede()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var verbindung = new SqliteConnection("Data Source=:memory:");
        await verbindung.OpenAsync(ct);
        await using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(verbindung).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261115164000_MerkmalTypen", cancellationToken: ct);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Vorlagen (Schluessel, Bezeichnung, Betreff, Text, GeaendertAm, GeaendertVon) VALUES "
            + "('a', 'A', 'Betreff', 'Guten Tag {{Name}},\nText', 0, 'x'), ('b', 'B', 'Betreff', 'Hallo {{Name}}, eigener Text', 0, 'x')", ct);

        await migrator.MigrateAsync(cancellationToken: ct);
        var vorlagen = await db.Vorlagen.AsNoTracking().OrderBy(v => v.Schluessel).ToListAsync(ct);
        vorlagen[0].Text.ShouldBe("{Anrede},\nText");
        vorlagen[1].Text.ShouldBe("Hallo {Name}, eigener Text");
        vorlagen.ShouldAllBe(v => v.Anredestil == Anredestil.Foermlich);
    }
}
