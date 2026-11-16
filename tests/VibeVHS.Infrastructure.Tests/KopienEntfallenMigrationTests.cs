using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Migration „KopienEntfallenPersonen“: die Kopie-Spalten der Personentabelle fallen weg. Werte, die nur dort standen (Geburtsdatum,
/// Sprache, Rollen), werden vorher zu Merkmalen; vorhandene Merkmale bleiben unverändert. Der Sortierschlüssel der Namensversionen
/// wird beim Start nachgetragen.
/// </summary>
public sealed class KopienEntfallenMigrationTests
{
    [Fact]
    public async Task Werte_nur_in_Kopiespalten_werden_Merkmale_und_Kontonamen_kommen_aus_der_Person()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var verbindung = new SqliteConnection("Data Source=:memory:");
        await verbindung.OpenAsync(ct);
        await using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(verbindung).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261116202319_Namensvorsatz", cancellationToken: ct); // Stand vorher

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await PersonAltAsync(verbindung, a, "Anna", "Berg", "von", "1980-05-04", "tr", 3, ct);
        await PersonAltAsync(verbindung, b, "Kai", "Ohne", "", null, "de", 0, ct);
        // b hat schon ein Sprach-Merkmal (führend) – es darf nicht doppelt werden.
        await Sql(verbindung, $"""
            INSERT INTO "PersonMerkmale" ("Id","PersonId","Art","Inhalt","GueltigVon","GueltigBis","ErfasstAm","ErfasstVon","Erfassungsart","Grund","ErsetztAm")
            VALUES ('{Guid.NewGuid().ToString().ToUpperInvariant()}','{b.ToString().ToUpperInvariant()}','Sprache','en','2020-01-01',NULL,0,'x','Ersterfassung','',NULL);
            """, ct);

        // Konten: mit Person (Name entfällt, kommt aus der Person) und Funktionskonto ohne Person (Name bleibt).
        await Sql(verbindung, $"""
            INSERT INTO "Benutzerkonten" ("Id","Aktiv","Fachbereiche","Login","Name","PersonId","Rollen")
            VALUES ('{Guid.NewGuid().ToString().ToUpperInvariant()}', 1, '[]', 'anna', 'Anna von Berg', '{a.ToString().ToUpperInvariant()}', '["Teilnehmerservice"]'),
                   ('{Guid.NewGuid().ToString().ToUpperInvariant()}', 1, '[]', 'kasse', 'Kasse Rathaus', NULL, '["Buchhaltung"]');
            """, ct);

        await migrator.MigrateAsync(cancellationToken: ct);

        await using var neu = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(verbindung).Options);
        var merkmale = await neu.Set<PersonMerkmal>().AsNoTracking()
            .Select(m => new { PersonId = EF.Property<Guid>(m, "PersonId"), m.Art, m.Inhalt, m.GueltigVon }).ToListAsync(ct);
        var vonA = merkmale.Where(m => m.PersonId == a).ToDictionary(m => m.Art, m => m.Inhalt);
        vonA[MerkmalArt.Geburtsdatum].ShouldBe("1980-05-04");
        vonA[MerkmalArt.Sprache].ShouldBe("tr");
        vonA[MerkmalArt.Rollen].ShouldBe("3");
        merkmale.Where(m => m.PersonId == a).ShouldAllBe(m => m.GueltigVon == new DateOnly(2020, 1, 1)); // ab erstem Namen
        merkmale.Where(m => m.PersonId == b).Select(m => (m.Art, m.Inhalt)).ShouldBe([(MerkmalArt.Sprache, "en")]); // keine Rolle 0, kein Doppel

        // Der Sortierschlüssel (Etappe 1) ist inzwischen wieder entfallen (Sortierung per Collation DIN5007_2, Migration SortierungPerCollation).

        await using (var c = verbindung.CreateCommand())
        {
            c.CommandText = """SELECT "Login", "Name" FROM "Benutzerkonten" ORDER BY "Login";""";
            await using var r = await c.ExecuteReaderAsync(ct);
            var konten = new List<(string, string?)>();
            while (await r.ReadAsync(ct))
            {
                konten.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
            }

            konten.ShouldBe([("anna", null), ("kasse", "Kasse Rathaus")]);
        }
    }

    private static async Task PersonAltAsync(SqliteConnection v, Guid id, string vorname, string nachname, string vorsatz, string? geburt, string sprache,
        int rollen, CancellationToken ct)
    {
        var gid = id.ToString().ToUpperInvariant();
        var geb = geburt is null ? "NULL" : $"'{geburt}'";
        await Sql(v, $"""
            INSERT INTO "Personen" ("Id","AngelegtAm","Anzeigename","Datenschutz","Rollen","Sortiername","Sortierschluessel","Sprache","Status","Fachbereiche","Geburtsdatum")
            VALUES ('{gid}', 0, '{vorname} {nachname}', 'Aktiv', {rollen}, '{nachname}, {vorname}', '{gid}', '{sprache}', 'Aktiv', '[]', {geb});
            INSERT INTO "PersonNamen" ("Id","PersonId","Vorname","Nachname","Vorsatz","Zusatz","GueltigVon","GueltigBis","ErfasstAm","ErfasstVon","Erfassungsart","Grund","ErsetztAm")
            VALUES ('{Guid.NewGuid().ToString().ToUpperInvariant()}','{gid}','{vorname}','{nachname}','{vorsatz}','','2020-01-01',NULL,0,'x','Ersterfassung','',NULL);
            """, ct);
    }

    private static async Task Sql(SqliteConnection v, string sql, CancellationToken ct)
    {
        await using var c = v.CreateCommand();
        c.CommandText = sql;
        await c.ExecuteNonQueryAsync(ct);
    }
}
