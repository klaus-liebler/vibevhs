using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Migration „TitelUndGeschlechtVersioniert“: Titel- und Geschlechtsspalte werden zu versionierten Merkmalen, nichts geht verloren.</summary>
public sealed class TitelGeschlechtMigrationTests
{
    [Fact]
    public async Task Titel_und_Geschlecht_werden_Merkmale_mit_Bestandserhalt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var verbindung = new SqliteConnection("Data Source=:memory:");
        await verbindung.OpenAsync(ct);
        await using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(verbindung).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261116193609_VorlagenAnredestil", cancellationToken: ct); // Stand vorher

        // Bestand wie bisher: Person mit Titelspalte und Statistik-Geschlecht, eine ohne, eine mit schon vorhandenem Geschlechts-Merkmal.
        var jetzt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(1));
        var a = Person.Anlegen("Eva", "Muster", new DateOnly(2020, 3, 1), jetzt.AddYears(-6));
        var b = Person.Anlegen("Kai", "Ohne", new DateOnly(2021, 1, 1), jetzt.AddYears(-5));
        var c = Person.Anlegen("Kim", "Merkmal", new DateOnly(2022, 1, 1), jetzt.AddYears(-4));
        c.AngabeAendern(MerkmalArt.Geschlecht, "divers", new DateOnly(2022, 1, 1), VibeVHS.Domain.Gemeinsam.Erfassung.Erst(jetzt.AddYears(-4)), korrektur: false);
        // Altes Schema: Titel und Geschlecht standen in Spalten (das heutige Modell kennt StatistikGeschlecht nicht mehr) – daher per SQL.
        await InsertAltAsync(verbindung, a, "Dr.", "weiblich", ct);
        await InsertAltAsync(verbindung, b, null, "nicht erfasst", ct);
        await InsertAltAsync(verbindung, c, null, "divers", ct);

        await migrator.MigrateAsync(cancellationToken: ct);

        await using var neu = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(verbindung).Options);
        var personen = await neu.Personen.AsNoTracking().OrderBy(p => p.Sortierschluessel).ToListAsync(ct);
        var eva = personen.Single(p => p.Id == a.Id);
        var stand = eva.StandAm(new DateOnly(2026, 11, 16), jetzt);
        (stand.Titel, stand.Geschlecht).ShouldBe(("Dr.", "weiblich"));
        eva.Merkmale.Single(m => m.Art == MerkmalArt.Titel).GueltigVon.ShouldBe(new DateOnly(2020, 3, 1));
        personen.Single(p => p.Id == b.Id).Merkmale.ShouldNotContain(m => m.Art == MerkmalArt.Titel || m.Art == MerkmalArt.Geschlecht);
        personen.Single(p => p.Id == c.Id).Merkmale.Count(m => m.Art == MerkmalArt.Geschlecht).ShouldBe(1); // nicht doppelt
    }

    private static object Binaer(DateTimeOffset d)
        => new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter().ConvertToProvider(d)!;

    /// <summary>Legt die Person im alten Schema an (mit Spalten Titel und StatistikGeschlecht), inklusive Name und Merkmalen.</summary>
    private static async Task InsertAltAsync(SqliteConnection v, Person p, string? titel, string geschlecht, CancellationToken ct)
    {
        await using var t = v.CreateCommand();
        t.CommandText = """
            INSERT INTO "Personen" ("Id","AngelegtAm","Anzeigename","Datenschutz","Rollen","Sortiername","Sortierschluessel","Sprache","Status","StatistikGeschlecht","Titel","Fachbereiche")
            VALUES ($id, $am, $anzeige, 'Aktiv', 0, $sort, $schluessel, 'de', 'Aktiv', $g, $titel, '[]');
            """;
        t.Parameters.AddWithValue("$id", p.Id.ToString().ToUpperInvariant());
        t.Parameters.AddWithValue("$am", Binaer(p.AngelegtAm));
        t.Parameters.AddWithValue("$anzeige", p.Anzeigename);
        t.Parameters.AddWithValue("$sort", p.Sortiername);
        t.Parameters.AddWithValue("$schluessel", p.Sortierschluessel);
        t.Parameters.AddWithValue("$g", geschlecht);
        t.Parameters.AddWithValue("$titel", (object?)titel ?? DBNull.Value);
        await t.ExecuteNonQueryAsync(ct);
        foreach (var n in p.Namen)
        {
            await using var c = v.CreateCommand();
            c.CommandText = """
                INSERT INTO "PersonNamen" ("Id","PersonId","Vorname","Nachname","GueltigVon","GueltigBis","ErfasstAm","ErfasstVon","Erfassungsart","Grund","ErsetztAm")
                VALUES ($id,$p,$v,$n,$von,NULL,$am,'x','Ersterfassung','',NULL);
                """;
            c.Parameters.AddWithValue("$id", n.Id.ToString().ToUpperInvariant());
            c.Parameters.AddWithValue("$p", p.Id.ToString().ToUpperInvariant());
            c.Parameters.AddWithValue("$v", n.Vorname);
            c.Parameters.AddWithValue("$n", n.Nachname);
            c.Parameters.AddWithValue("$von", n.GueltigVon.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            c.Parameters.AddWithValue("$am", Binaer(n.ErfasstAm));
            await c.ExecuteNonQueryAsync(ct);
        }

        foreach (var m in p.Merkmale)
        {
            await using var c = v.CreateCommand();
            c.CommandText = """
                INSERT INTO "PersonMerkmale" ("Id","PersonId","Art","Inhalt","GueltigVon","GueltigBis","ErfasstAm","ErfasstVon","Erfassungsart","Grund","ErsetztAm")
                VALUES ($id,$p,$art,$inhalt,$von,NULL,$am,'x','Ersterfassung','',NULL);
                """;
            c.Parameters.AddWithValue("$id", m.Id.ToString().ToUpperInvariant());
            c.Parameters.AddWithValue("$p", p.Id.ToString().ToUpperInvariant());
            c.Parameters.AddWithValue("$art", m.Art.ToString());
            c.Parameters.AddWithValue("$inhalt", m.Inhalt);
            c.Parameters.AddWithValue("$von", m.GueltigVon.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            c.Parameters.AddWithValue("$am", Binaer(m.ErfasstAm));
            await c.ExecuteNonQueryAsync(ct);
        }
    }
}
