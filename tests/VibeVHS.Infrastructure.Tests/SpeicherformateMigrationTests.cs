using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Integration;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Migrationen „Speicherformate“ + „SpeicherformateUmwandeln“: Ids (Text → 16 Byte), Tage (Text → JJJJMMTT), Zeitpunkte (EF-Binär → UTC-ms)
/// und Termin-Uhrzeiten (Ortszeit-Text → UTC-ms, Sommerzeit Europe/Berlin) werden verlustfrei umgewandelt; Fremdschlüssel bleiben gültig.
/// </summary>
public sealed class SpeicherformateMigrationTests
{
    private static object AltBinaer(DateTimeOffset d) => new DateTimeOffsetToBinaryConverter().ConvertToProvider(d)!;

    [Fact]
    public async Task Bestandsdaten_werden_verlustfrei_umgewandelt()
    {
        var ct = TestContext.Current.CancellationToken;
        Zeitzone.Festlegen("Europe/Berlin");
        await using var verbindung = new SqliteConnection("Data Source=:memory:");
        await verbindung.OpenAsync(ct);
        await using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(verbindung).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261116212554_KontonameLeeren", cancellationToken: ct); // Stand vorher

        var person = Guid.NewGuid();
        var name = Guid.NewGuid();
        var angelegt = new DateTimeOffset(2026, 3, 29, 1, 59, 59, 123, TimeSpan.FromHours(1)); // kurz vor der Umstellung, MEZ
        var erfasst = new DateTimeOffset(2026, 7, 1, 12, 0, 0, 7, TimeSpan.FromHours(2));      // Sommer, MESZ
        await Sql(verbindung, """
            INSERT INTO "Personen" ("Id","AngelegtAm","Datenschutz","Status","Fachbereiche") VALUES ($p, $angelegt, 'Aktiv', 'Aktiv', '[]');
            INSERT INTO "PersonNamen" ("Id","PersonId","Vorname","Nachname","Vorsatz","Zusatz","Sortierschluessel","GueltigVon","GueltigBis","ErfasstAm","ErfasstVon","Erfassungsart","Grund","ErsetztAm")
            VALUES ($n, $p, 'Eva', 'Muster', '', '', 'muster eva', '2020-02-29', '2026-12-31', $erfasst, 'x', 'Ersterfassung', '', NULL);
            """, ct, ("$p", person.ToString().ToUpperInvariant()), ("$n", name.ToString().ToUpperInvariant()),
            ("$angelegt", AltBinaer(angelegt)), ("$erfasst", AltBinaer(erfasst)));
        // Termine (Ortszeit-Text) rund um die Umstellungen 2026: 29.03. (Beginn MESZ) und 25.10. (Ende MESZ).
        var termine = new[] { new DateTime(2026, 3, 28, 18, 0, 0), new DateTime(2026, 3, 30, 18, 0, 0), new DateTime(2026, 10, 24, 18, 0, 0), new DateTime(2026, 10, 26, 18, 0, 0) };
        foreach (var t in termine)
        {
            await Sql(verbindung, """INSERT INTO "Pruefungstermine" ("Id","Art","Beginn","GemeldetAm","KursId","Ort") VALUES ($id, 'Dtz', $b, NULL, NULL, 'Raum');""", ct,
                ("$id", Guid.NewGuid().ToString().ToUpperInvariant()), ("$b", t.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)));
        }

        await migrator.MigrateAsync(cancellationToken: ct);

        // Rohwerte: Id = 16 Byte Big Endian, Tag = JJJJMMTT, Zeitpunkt = UTC-ms, Termin = UTC-ms (18:00 MESZ = 16:00 UTC, 18:00 MEZ = 17:00 UTC).
        (await Wert(verbindung, """SELECT hex("Id") FROM "Personen";""", ct)).ShouldBe(Convert.ToHexString(person.ToByteArray(true)));
        (await Wert(verbindung, """SELECT "GueltigVon" || '/' || "GueltigBis" FROM "PersonNamen";""", ct)).ShouldBe("20200229/20261231");
        (await Wert(verbindung, """SELECT "ErfasstAm" FROM "PersonNamen";""", ct)).ShouldBe(erfasst.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        (await Wert(verbindung, """SELECT "AngelegtAm" FROM "Personen";""", ct)).ShouldBe(angelegt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        var utc = (await Werte(verbindung, """SELECT "Beginn" FROM "Pruefungstermine" ORDER BY "Beginn";""", ct))
            .Select(x => DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).UtcDateTime.Hour).ToList();
        utc.ShouldBe([17, 16, 16, 17]);
        (await Werte(verbindung, "PRAGMA foreign_key_check;", ct)).ShouldBeEmpty();

        // Über EF gelesen: dieselben fachlichen Werte wie vorher.
        await using var neu = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(verbindung).Options);
        var n = await neu.Set<PersonName>().AsNoTracking().SingleAsync(ct);
        (n.Id, n.GueltigVon, n.GueltigBis, n.ErfasstAm).ShouldBe((name, new DateOnly(2020, 2, 29), (DateOnly?)new DateOnly(2026, 12, 31), erfasst));
        (await neu.Personen.AsNoTracking().Select(p => p.Id).SingleAsync(ct)).ShouldBe(person);
        (await neu.Set<Pruefungstermin>().AsNoTracking().OrderBy(t => t.Beginn).Select(t => t.Beginn).ToListAsync(ct)).ShouldBe(termine);
    }

    private static async Task Sql(SqliteConnection v, string sql, CancellationToken ct, params (string, object)[] parameter)
    {
        await using var c = v.CreateCommand();
        c.CommandText = sql;
        foreach (var (k, w) in parameter)
        {
            c.Parameters.AddWithValue(k, w);
        }

        await c.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string?> Wert(SqliteConnection v, string sql, CancellationToken ct) => (await Werte(v, sql, ct)).SingleOrDefault();

    private static async Task<List<string>> Werte(SqliteConnection v, string sql, CancellationToken ct)
    {
        await using var c = v.CreateCommand();
        c.CommandText = sql;
        await using var r = await c.ExecuteReaderAsync(ct);
        var liste = new List<string>();
        while (await r.ReadAsync(ct))
        {
            liste.Add(Convert.ToString(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return liste;
    }
}
