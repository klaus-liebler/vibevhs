using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VibeVHS.Application.Betrieb;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Betrieb;

/// <summary>Ordner der Sicherungen (Standard: <c>data/sicherung</c> im Inhaltsverzeichnis der Anwendung).</summary>
public sealed record SicherungsOrt(string Ordner);

/// <summary>
/// PostgreSQL-Sicherung mit <c>pg_dump</c> (Archivformat, konsistent im laufenden Betrieb) in den Ordner <see cref="SicherungsOrt"/>, mit
/// SHA-256 und echtem Wiederherstellungstest: <c>pg_restore</c> in eine Wegwerf-Datenbank auf demselben Server, Schemaversion und
/// Zeilenzahlen der Kerntabellen lesen, Wegwerf-Datenbank löschen. Die laufende Datenbank bleibt unberührt.
/// </summary>
/// <remarks>
/// Werkzeuge: <c>Sicherung:Werkzeug = docker</c> ruft pg_dump/pg_restore im Datenbank-Container auf (<c>Sicherung:Container</c>, Standard
/// „vibevhs-db-1“; Verbindung dort über den lokalen Socket), sonst die Programme aus dem Suchpfad bzw. <c>Sicherung:PgBin</c> mit Server,
/// Port und Passwort aus der Verbindungszeichenfolge. Die Kommentar-Datenbank wird jeweils mitgesichert („kommentare-….dump“).
/// </remarks>
internal sealed class PostgresSicherung(VibeVhsDbContext db, IConfiguration konfiguration, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr,
    SicherungsOrt ort, Dokumentation.KommentarDatenbankOrt? kommentare = null) : ISicherung
{
    private static readonly string[] Kerntabellen = ["Personen", "Kurse", "Veranstaltungen", "Belegungen", "Belege", "Finanzprotokoll", "Protokoll"];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private string Ordner => ort.Ordner;

    private NpgsqlConnectionStringBuilder Verbindung => new(db.Database.GetConnectionString());

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
        Directory.CreateDirectory(Ordner);
        var am = uhr.GetLocalNow();
        var ziel = Path.Combine(Ordner, $"vibevhs-{am:yyyyMMdd-HHmmss}.dump");
        await DumpAsync(Verbindung, ziel, abbruch);

        var kommentarSicherung = await KommentareSichernAsync(ziel, abbruch);
        var eintrag = new Sicherungseintrag(Path.GetFileName(ziel), am, new FileInfo(ziel).Length, await HashAsync(ziel, abbruch), null, null, null);
        await SchreibenAsync(eintrag, abbruch);
        eintrag = await TestenAsync(eintrag, abbruch);
        await AufraeumenAsync(abbruch);
        await protokoll.ProtokollierenAsync("Betrieb.Sicherung",
            $"Datenbank gesichert: {eintrag.Datei} ({eintrag.Bytes / 1024} KB, SHA-256 {eintrag.Sha256[..16]}…); Wiederherstellungstest: {eintrag.Pruefergebnis}"
            + (kommentarSicherung is null ? string.Empty : $"; Kommentare: {kommentarSicherung}"),
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
    /// Prüfsumme vergleichen, Sicherung in eine Wegwerf-Datenbank zurückspielen, Schemaversion und Zeilenzahlen der Kerntabellen auslesen,
    /// Wegwerf-Datenbank löschen.
    /// </summary>
    private async Task<Sicherungseintrag> TestenAsync(Sicherungseintrag e, CancellationToken abbruch)
    {
        var pfad = Path.Combine(Ordner, e.Datei);
        string ergebnis;
        bool ok;
        string? version = null;
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
            var test = new NpgsqlConnectionStringBuilder(Verbindung.ConnectionString) { Database = $"{Verbindung.Database}_pruefung_{Guid.NewGuid():N}"[..50], Pooling = false };
            try
            {
                await using (var admin = Schema.Verwaltungsverbindung(Verbindung))
                {
                    await admin.OpenAsync(abbruch);
                    await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{test.Database}\" TEMPLATE template0", admin);
                    await cmd.ExecuteNonQueryAsync(abbruch);
                }

                await RestoreAsync(test, pfad, abbruch);
                await using var c = new NpgsqlConnection(test.ConnectionString);
                await c.OpenAsync(abbruch);
                version = (await Skalar(c, "SELECT \"Version\" FROM \"Schemainfo\" LIMIT 1", abbruch))?.ToString();
                var zaehlung = new List<string>();
                foreach (var t in Kerntabellen)
                {
                    zaehlung.Add($"{t} {await Skalar(c, $"SELECT count(*) FROM \"{t}\"", abbruch)}");
                }

                var aktuell = await Schema.GespeicherteVersionAsync(db, abbruch);
                ok = version == aktuell;
                ergebnis = ok ? $"ok – zurückgespielt und gelesen, Schemaversion {version}, {string.Join(", ", zaehlung)}"
                    : $"Schemaversion {version} weicht vom laufenden Stand {aktuell} ab (ältere Version)";
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                (ok, ergebnis) = (false, $"Nicht wiederherstellbar: {ex.Message}");
            }
            finally
            {
                await using var admin = Schema.Verwaltungsverbindung(Verbindung);
                await admin.OpenAsync(CancellationToken.None);
                await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{test.Database}\" WITH (FORCE)", admin);
                await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }

        var neu = e with { Wiederherstellbar = ok, Pruefergebnis = ergebnis, Migration = version ?? e.Migration };
        await SchreibenAsync(neu, abbruch);
        return neu;
    }

    private static async Task<object?> Skalar(NpgsqlConnection c, string sql, CancellationToken abbruch)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
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
            File.Delete(Path.Combine(Ordner, KommentarDatei(alt.Datei)));
        }
    }

    /// <summary>„vibevhs-20261117-013000.dump“ → „kommentare-20261117-013000.dump“</summary>
    internal static string KommentarDatei(string sicherung) => "kommentare-" + Path.GetFileName(sicherung)["vibevhs-".Length..];

    /// <summary>Sichert die Kommentar-Datenbank neben die Hauptsicherung; null, wenn es keine gibt.</summary>
    private async Task<string?> KommentareSichernAsync(string hauptsicherung, CancellationToken abbruch)
    {
        if (kommentare is null || !await Schema.DatenbankVorhandenAsync(new NpgsqlConnectionStringBuilder(kommentare.Verbindung), abbruch))
        {
            return null;
        }

        var ziel = Path.Combine(Path.GetDirectoryName(hauptsicherung)!, KommentarDatei(hauptsicherung));
        await DumpAsync(new NpgsqlConnectionStringBuilder(kommentare.Verbindung), ziel, abbruch);
        return $"{Path.GetFileName(ziel)} (SHA-256 {(await HashAsync(ziel, abbruch))[..16]}…)";
    }

    private Task DumpAsync(NpgsqlConnectionStringBuilder quelle, string ziel, CancellationToken abbruch)
        => WerkzeugAsync("pg_dump", quelle, ["--format=custom", "--no-owner", "--no-privileges"], eingabe: null, ausgabe: ziel, abbruch);

    private Task RestoreAsync(NpgsqlConnectionStringBuilder ziel, string datei, CancellationToken abbruch)
        => WerkzeugAsync("pg_restore", ziel, ["--no-owner", "--no-privileges", "--exit-on-error"], eingabe: datei, ausgabe: null, abbruch);

    /// <summary>Startet pg_dump/pg_restore (im Container oder lokal); Ein- und Ausgabe als Datenstrom, Fehlertext aus stderr.</summary>
    private async Task WerkzeugAsync(string programm, NpgsqlConnectionStringBuilder verbindung, string[] argumente, string? eingabe, string? ausgabe,
        CancellationToken abbruch)
    {
        var start = new ProcessStartInfo { RedirectStandardError = true, RedirectStandardOutput = ausgabe is not null, RedirectStandardInput = eingabe is not null, UseShellExecute = false };
        if (string.Equals(konfiguration["Sicherung:Werkzeug"], "docker", StringComparison.OrdinalIgnoreCase))
        {
            start.FileName = "docker";
            foreach (var a in new[] { "exec", "-i", konfiguration["Sicherung:Container"] ?? "vibevhs-db-1", programm, "-U", verbindung.Username!, "-d", verbindung.Database! })
            {
                start.ArgumentList.Add(a);
            }
        }
        else
        {
            start.FileName = konfiguration["Sicherung:PgBin"] is { Length: > 0 } bin ? Path.Combine(bin, programm) : programm;
            foreach (var a in new[] { "-h", verbindung.Host!, "-p", verbindung.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "-U", verbindung.Username!, "-d", verbindung.Database! })
            {
                start.ArgumentList.Add(a);
            }

            start.Environment["PGPASSWORD"] = verbindung.Password ?? string.Empty;
        }

        foreach (var a in argumente)
        {
            start.ArgumentList.Add(a);
        }

        using var prozess = Process.Start(start) ?? throw new InvalidOperationException($"{programm} konnte nicht gestartet werden.");
        var fehler = prozess.StandardError.ReadToEndAsync(abbruch);
        if (ausgabe is not null)
        {
            await using var datei = File.Create(ausgabe);
            await prozess.StandardOutput.BaseStream.CopyToAsync(datei, abbruch);
        }

        if (eingabe is not null)
        {
            await using (var datei = File.OpenRead(eingabe))
            {
                await datei.CopyToAsync(prozess.StandardInput.BaseStream, abbruch);
            }

            prozess.StandardInput.Close();
        }

        await prozess.WaitForExitAsync(abbruch);
        if (prozess.ExitCode != 0)
        {
            if (ausgabe is not null)
            {
                File.Delete(ausgabe);
            }

            throw new InvalidOperationException($"{programm} meldet Fehler {prozess.ExitCode}: {(await fehler).Trim()}");
        }
    }

    private static async Task<string> HashAsync(string pfad, CancellationToken abbruch)
    {
        await using var s = File.OpenRead(pfad);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, abbruch));
    }
}
