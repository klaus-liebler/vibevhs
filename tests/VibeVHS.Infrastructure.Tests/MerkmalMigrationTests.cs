using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Migration „MerkmalTypen“: bestehende Merkmale werden Ja/Nein, solche mit festgelegten Werten Auswahl.</summary>
public sealed class MerkmalMigrationTests
{
    [Fact]
    public async Task Bestehende_Merkmale_bekommen_JaNein_oder_Auswahl()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var verbindung = new SqliteConnection("Data Source=:memory:");
        await verbindung.OpenAsync(ct);
        await using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(verbindung).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261115132800_EinrichtungLogo", cancellationToken: ct); // Stand vor den Werttypen

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Merkmale (Schluessel, Name, Gruppe, Vererbbar, NachweisPflicht, Bezug, Gesetzlich, Werte, VorlaufTage, Mehrfach) VALUES "
            + "('beamer','Beamer','Ausstattung',0,0,'Ort',0,'[]',60,0), "
            + "('bamf-lk','BAMF-Lehrkraftzulassung','Qualifikation',0,1,'Person',1,'[\"direkt\",\"mit Auflagen\"]',90,0)", ct);

        await migrator.MigrateAsync(cancellationToken: ct);
        var typen = await db.Merkmale.AsNoTracking().ToDictionaryAsync(m => m.Schluessel, m => m.Typ, ct);
        typen["beamer"].ShouldBe(Merkmalstyp.JaNein);
        typen["bamf-lk"].ShouldBe(Merkmalstyp.Auswahl);
        (await db.Merkmale.AsNoTracking().SingleAsync(m => m.Schluessel == "bamf-lk", ct)).InaktiveWerte.ShouldBeEmpty();
    }
}
