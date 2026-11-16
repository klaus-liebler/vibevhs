using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VibeVHS.Application.Betrieb;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Betrieb;

/// <summary>
/// SQLite-Online-Sicherung (Backup-API, konsistent während des Betriebs) in den Ordner <c>Sicherung:Ordner</c>
/// (Standard: <c>sicherung</c> neben der Datenbank), mit SHA-256 und Wiederherstellungstest. Unter Linux kann später
/// Litestream die laufende Replikation übernehmen (Vorlage in <c>deploy/litestream.yml</c>).
/// </summary>
internal sealed class SqliteSicherung(VibeVhsDbContext db, IConfiguration konfiguration, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr) : ISicherung
{
    private static readonly string[] Kerntabellen = ["Personen", "Kurse", "Veranstaltungen", "Belegungen", "Belege", "Finanzprotokoll", "Protokoll"];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private string Datenbank => new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;

    private string Ordner
    {
        get
        {
            var o = konfiguration["Sicherung:Ordner"];
            return string.IsNullOrWhiteSpace(o) ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Datenbank))!, "sicherung") : Path.GetFullPath(o);
        }
    }

    private async Task RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !Betriebsrollen.Sicherung.Contains(r)))
        {
            throw new InvalidOperationException(Freigabe.NurFuer(wer.Rolle, Betriebsrollen.Sicherung).Begruendung);
        }
    }

    public async Task<Sicherungseintrag> SichernAsync(CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        if (Datenbank is ":memory:" or "")
        {
            throw new InvalidOperationException("Eine Datenbank im Arbeitsspeicher kann nicht gesichert werden.");
        }

        Directory.CreateDirectory(Ordner);
        var am = uhr.GetLocalNow();
        var ziel = Path.Combine(Ordner, $"vibevhs-{am:yyyyMMdd-HHmmss}.db");
        await using (var quelle = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Datenbank, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        await using (var kopie = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ziel, Pooling = false }.ToString()))
        {
            await quelle.OpenAsync(abbruch);
            await kopie.OpenAsync(abbruch);
            quelle.BackupDatabase(kopie);
        }

        var eintrag = new Sicherungseintrag(Path.GetFileName(ziel), am, new FileInfo(ziel).Length, await HashAsync(ziel, abbruch), null, null, null);
        await SchreibenAsync(eintrag, abbruch);
        eintrag = await TestenAsync(eintrag, abbruch);
        await AufraeumenAsync(abbruch);
        await protokoll.ProtokollierenAsync("Betrieb.Sicherung",
            $"Datenbank gesichert: {eintrag.Datei} ({eintrag.Bytes / 1024} KB, SHA-256 {eintrag.Sha256[..16]}…); Wiederherstellungstest: {eintrag.Pruefergebnis}",
            erfolgreich: eintrag.Wiederherstellbar == true, abbruch: abbruch);
        await db.SaveChangesAsync(abbruch);
        return eintrag;
    }

    public async Task<IReadOnlyList<Sicherungseintrag>> ListeAsync(CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        if (!Directory.Exists(Ordner))
        {
            return [];
        }

        var liste = new List<Sicherungseintrag>();
        foreach (var json in Directory.EnumerateFiles(Ordner, "vibevhs-*.json"))
        {
            if (JsonSerializer.Deserialize<Sicherungseintrag>(await File.ReadAllTextAsync(json, abbruch)) is { } e)
            {
                liste.Add(e);
            }
        }

        return liste.OrderByDescending(e => e.Am).ToList();
    }

    public async Task<Sicherungseintrag> PruefenAsync(string datei, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var eintrag = (await ListeAsync(abbruch)).FirstOrDefault(e => e.Datei == Path.GetFileName(datei))
                      ?? throw new InvalidOperationException($"Sicherung {datei} nicht gefunden.");
        eintrag = await TestenAsync(eintrag, abbruch);
        await protokoll.ProtokollierenAsync("Betrieb.Wiederherstellungstest", $"Wiederherstellungstest {eintrag.Datei}: {eintrag.Pruefergebnis}", erfolgreich: eintrag.Wiederherstellbar == true,
            abbruch: abbruch);
        await db.SaveChangesAsync(abbruch);
        return eintrag;
    }

    /// <summary>
    /// Prüfsumme vergleichen, Kopie in einen temporären Ordner „wiederherstellen“, öffnen, PRAGMA integrity_check,
    /// Schemaversion und Zeilenzahlen der Kerntabellen auslesen. Die laufende Datenbank bleibt unberührt.
    /// </summary>
    private async Task<Sicherungseintrag> TestenAsync(Sicherungseintrag e, CancellationToken abbruch)
    {
        var pfad = Path.Combine(Ordner, e.Datei);
        string ergebnis;
        bool ok;
        string? migration = null;
        if (!File.Exists(pfad))
        {
            (ok, ergebnis) = (false, "Datei fehlt");
        }
        else if (await HashAsync(pfad, abbruch) != e.Sha256)
        {
            (ok, ergebnis) = (false, "Prüfsumme stimmt nicht – Sicherung beschädigt oder verändert");
        }
        else
        {
            var test = Path.Combine(Path.GetTempPath(), $"vibevhs-wiederherstellung-{Guid.NewGuid():N}.db");
            File.Copy(pfad, test);
            try
            {
                await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = test, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                VibeVHS.Infrastructure.Datenbank.SqliteErweiterungen.Registrieren(c); // integrity_check prüft auch den Index mit Collation DIN5007_2
                await c.OpenAsync(abbruch);
                var integritaet = (await Skalar(c, "PRAGMA integrity_check", abbruch))?.ToString();
                migration = (await Skalar(c, "SELECT Version FROM Schemainfo LIMIT 1", abbruch))?.ToString();
                var zaehlung = new List<string>();
                foreach (var t in Kerntabellen)
                {
                    zaehlung.Add($"{t} {await Skalar(c, $"SELECT COUNT(*) FROM \"{t}\"", abbruch)}");
                }

                var aktuell = (await db.Database.SqlQueryRaw<string>("SELECT Version AS Value FROM Schemainfo LIMIT 1").ToListAsync(abbruch))
                    .FirstOrDefault();
                ok = integritaet == "ok" && migration == aktuell;
                ergebnis = ok ? $"ok – Integrität geprüft, Schemaversion {migration}, {string.Join(", ", zaehlung)}"
                    : integritaet != "ok" ? $"Integritätsprüfung: {integritaet}" : $"Schemaversion {migration} weicht vom laufenden Stand {aktuell} ab (ältere Version)";
            }
            catch (SqliteException ex)
            {
                (ok, ergebnis) = (false, $"Nicht lesbar: {ex.Message}");
            }
            finally
            {
                File.Delete(test);
            }
        }

        var neu = e with { Wiederherstellbar = ok, Pruefergebnis = ergebnis, Migration = migration ?? e.Migration };
        await SchreibenAsync(neu, abbruch);
        return neu;
    }

    private static async Task<object?> Skalar(SqliteConnection c, string sql, CancellationToken abbruch)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync(abbruch);
    }

    private async Task SchreibenAsync(Sicherungseintrag e, CancellationToken abbruch)
        => await File.WriteAllTextAsync(Path.Combine(Ordner, Path.ChangeExtension(e.Datei, ".json")), JsonSerializer.Serialize(e, Json), abbruch);

    private async Task AufraeumenAsync(CancellationToken abbruch)
    {
        var behalten = konfiguration.GetValue("Sicherung:Behalten", 14);
        foreach (var alt in (await ListeAsync(abbruch)).Skip(behalten))
        {
            File.Delete(Path.Combine(Ordner, alt.Datei));
            File.Delete(Path.Combine(Ordner, Path.ChangeExtension(alt.Datei, ".json")));
        }
    }

    private static async Task<string> HashAsync(string pfad, CancellationToken abbruch)
    {
        await using var s = File.OpenRead(pfad);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, abbruch));
    }
}
