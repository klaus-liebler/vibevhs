using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using VibeVHS.Application.Betrieb;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Hintergrund;

namespace VibeVHS.Infrastructure.Betrieb;

/// <summary>Konfiguration „Schulung“ (QA-68).</summary>
public sealed class Schulungsoptionen
{
    public bool Aktiv { get; set; }

    /// <summary>Name der Instanz (i01 … i20); steht in der Spielzeitleiste und im Cookie-Namen.</summary>
    public string Instanz { get; set; } = "i01";

    /// <summary>Präfix der Vorlage-Datenbanken.</summary>
    public string Vorlagen { get; set; } = "vibevhs_vorlage";

    /// <summary>Marke, aus der eine fehlende Instanzdatenbank kopiert wird.</summary>
    public string Startmarke { get; set; } = "ausgangslage";

    /// <summary>Leitstandschlüssel der Kursleitung (Kopf „X-Schulung-Schluessel“).</summary>
    public string? Schluessel { get; set; }

    /// <summary>Statt zu starten: Vorlagen aller Sprungmarken bauen und beenden.</summary>
    public bool VorlagenBauen { get; set; }

    /// <summary>Angemeldete Personen dürfen die Spielzeit ihrer Instanz steuern (sonst nur die Kursleitung).</summary>
    public bool TeilnehmerSteuern { get; set; } = true;

    public static Schulungsoptionen Aus(IConfiguration konfiguration)
    {
        var o = new Schulungsoptionen();
        konfiguration.GetSection("Schulung").Bind(o);
        return o;
    }
}

public sealed record Spielzeitstand(string Instanz, DateTimeOffset Spielzeit, double Tempo, double LetztesTempo, long Weltstand, bool Wartung, string? Marke,
    bool TeilnehmerSteuern, string? LetzterFehler);

/// <summary>Datenbanken des Schulungsbetriebs im PostgreSQL-Server: nur gekennzeichnete Instanzen werden gelöscht (R2).</summary>
public sealed class Schulungsdatenbanken(Application.Betrieb.Mandanten mandanten)
{
    public const string Instanzkennung = "VibeVHS-Schulungsinstanz";
    public const string Vorlagenkennung = "VibeVHS-Vorlage";

    private NpgsqlConnectionStringBuilder Verbindung => new(mandanten.Standard.Verbindung);

    /// <summary>Name der Instanzdatenbank (aus der Verbindungszeichenfolge).</summary>
    public string Datenbank => Verbindung.Database!;

    public Task<bool> VorhandenAsync(string name, CancellationToken abbruch = default)
        => Schema.DatenbankVorhandenAsync(new NpgsqlConnectionStringBuilder(Verbindung.ConnectionString) { Database = name }, abbruch);

    public async Task<string?> KommentarAsync(string name, CancellationToken abbruch = default)
    {
        await using var con = Schema.Verwaltungsverbindung(Verbindung);
        await con.OpenAsync(abbruch);
        await using var cmd = new NpgsqlCommand("SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = @n", con);
        cmd.Parameters.AddWithValue("n", name);
        return await cmd.ExecuteScalarAsync(abbruch) as string;
    }

    /// <summary>Kopie einer Vorlage als neue Datenbank mit Kommentar (Kennzeichnung).</summary>
    public async Task KopierenAsync(string vorlage, string ziel, string kommentar, CancellationToken abbruch = default)
    {
        NpgsqlConnection.ClearAllPools();
        await using var con = Schema.Verwaltungsverbindung(Verbindung);
        await con.OpenAsync(abbruch);
        await AusfuehrenAsync(con, $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{vorlage}' AND pid <> pg_backend_pid()", abbruch);
        await AusfuehrenAsync(con, $"CREATE DATABASE \"{ziel}\" TEMPLATE \"{vorlage}\"", abbruch);
        await AusfuehrenAsync(con, $"COMMENT ON DATABASE \"{ziel}\" IS '{kommentar.Replace("'", "''", StringComparison.Ordinal)}'", abbruch);
    }

    /// <summary>Löscht eine Instanzdatenbank – nur, wenn sie als Schulungsinstanz gekennzeichnet ist (R2).</summary>
    public async Task InstanzLoeschenAsync(string name, CancellationToken abbruch = default)
    {
        var kommentar = await KommentarAsync(name, abbruch) ?? string.Empty;
        if (!kommentar.StartsWith(Instanzkennung, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Die Datenbank „{name}“ ist nicht als Schulungsinstanz gekennzeichnet – sie wird nicht gelöscht.");
        }

        await LoeschenAsync(name, abbruch);
    }

    internal async Task LoeschenAsync(string name, CancellationToken abbruch)
    {
        NpgsqlConnection.ClearAllPools();
        await using var con = Schema.Verwaltungsverbindung(Verbindung);
        await con.OpenAsync(abbruch);
        await AusfuehrenAsync(con, $"ALTER DATABASE \"{name}\" WITH IS_TEMPLATE false", abbruch, fehlerIgnorieren: true);
        await AusfuehrenAsync(con, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", abbruch);
    }

    internal async Task AlsVorlageMarkierenAsync(string name, string kommentar, CancellationToken abbruch)
    {
        await using var con = Schema.Verwaltungsverbindung(Verbindung);
        await con.OpenAsync(abbruch);
        await AusfuehrenAsync(con, $"ALTER DATABASE \"{name}\" WITH IS_TEMPLATE true", abbruch);
        await AusfuehrenAsync(con, $"COMMENT ON DATABASE \"{name}\" IS '{kommentar.Replace("'", "''", StringComparison.Ordinal)}'", abbruch);
    }

    private static async Task AusfuehrenAsync(NpgsqlConnection con, string sql, CancellationToken abbruch, bool fehlerIgnorieren = false)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(sql, con) { CommandTimeout = 300 };
            await cmd.ExecuteNonQueryAsync(abbruch);
        }
        catch (PostgresException) when (fehlerIgnorieren)
        {
            // z. B. Datenbank fehlt
        }
    }
}

/// <summary>
/// Schulungsbetrieb einer Instanz (QA-68, aus SchoolCraft FK-14): Spielzeit steuern, zu Sprungmarken springen (Datenbank aus der Vorlage
/// kopieren), Vorlagen bauen. Läuft nur mit erfundenen Daten.
/// </summary>
public sealed class Schulungsbetrieb(Schulungsoptionen optionen, Spielzeit uhr, Schulungsdatenbanken datenbanken, IServiceProvider dienste,
    ILogger<Schulungsbetrieb> log)
{
    private readonly SemaphoreSlim sprung = new(1, 1);

    public Schulungsoptionen Optionen => optionen;

    public Spielzeit Uhr => uhr;

    public string? Marke { get; private set; }

    public string? LetzterFehler { get; private set; }

    /// <summary>Bezugstag der Vorlagen (Montag der Bauwoche), aus dem Kommentar der Vorlage.</summary>
    public DateOnly? Bezugstag { get; private set; }

    public Spielzeitstand Stand => new(optionen.Instanz, uhr.GetUtcNow(), uhr.Tempo, uhr.LetztesTempo, uhr.Weltstand, uhr.Wartung, Marke,
        optionen.TeilnehmerSteuern, LetzterFehler);

    /// <summary>Zeitpunkt einer Marke (Ortszeit des Bezugstags + Versatz).</summary>
    public DateTimeOffset Zeitpunkt(Sprungmarke marke, DateOnly bezugstag)
    {
        var lokal = bezugstag.ToDateTime(TimeOnly.MinValue) + marke.Versatz;
        return new DateTimeOffset(lokal, uhr.LocalTimeZone.GetUtcOffset(lokal));
    }

    /// <summary>Beim Start: fehlende Instanzdatenbank aus der Startmarke kopieren, vorhandene auf Kennzeichnung prüfen (R2), Spielzeit setzen.</summary>
    public async Task VorbereitenAsync(CancellationToken abbruch = default)
    {
        var marke = Schulungswoche.Marke(optionen.Startmarke)
            ?? throw new InvalidOperationException($"Unbekannte Startmarke „{optionen.Startmarke}“ (bekannt: {string.Join(", ", Schulungswoche.Marken.Select(m => m.Id))}).");
        var name = datenbanken.Datenbank;
        if (await datenbanken.VorhandenAsync(name, abbruch))
        {
            var kommentar = await datenbanken.KommentarAsync(name, abbruch) ?? string.Empty;
            if (!kommentar.StartsWith(Schulungsdatenbanken.Instanzkennung, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Schulungsbetrieb verweigert: Die Datenbank „{name}“ ist nicht als Schulungsinstanz gekennzeichnet. Bitte eine eigene Instanzdatenbank angeben.");
            }

            marke = Schulungswoche.Marke(Wert(kommentar, "marke")) ?? marke;
            Bezugstag = DateOnly.TryParse(Wert(kommentar, "bezug"), System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : null;
        }
        else
        {
            await VorlageKopierenAsync(marke, name, abbruch);
        }

        Marke = marke.Id;
        uhr.NeuerWeltstand(Zeitpunkt(marke, Bezugstag ?? Schulungswoche.Bezugstag(DateOnly.FromDateTime(DateTime.Today))));
        log.LogInformation("Schulungsinstanz {Instanz} auf {Datenbank}, Spielzeit {Zeit} ({Marke}), angehalten", optionen.Instanz, name, uhr.GetLocalNow(), marke.Id);
    }

    /// <summary>Zu einer Sprungmarke springen: Wartung, Datenbank aus der Vorlage kopieren, Spielzeit auf die Marke (angehalten), neuer Weltstand.</summary>
    public async Task SpringenAsync(string markeId, CancellationToken abbruch = default)
    {
        var marke = Schulungswoche.Marke(markeId) ?? throw new InvalidOperationException($"Die Sprungmarke „{markeId}“ gibt es nicht.");
        await sprung.WaitAsync(abbruch);
        uhr.WartungSetzen(true);
        try
        {
            if (!await datenbanken.VorhandenAsync(marke.Vorlage(optionen.Vorlagen), abbruch))
            {
                throw new InvalidOperationException($"Die Vorlage „{marke.Vorlage(optionen.Vorlagen)}“ fehlt. Bitte die Vorlagen bauen: tools/instanzen.ps1 vorlagen");
            }

            await Task.Delay(300, abbruch); // laufende Anfragen ausklingen lassen
            await datenbanken.InstanzLoeschenAsync(datenbanken.Datenbank, abbruch);
            await VorlageKopierenAsync(marke, datenbanken.Datenbank, abbruch);
            Marke = marke.Id;
            LetzterFehler = null;
            uhr.NeuerWeltstand(Zeitpunkt(marke, Bezugstag!.Value));
            log.LogInformation("Instanz {Instanz} zu {Marke} gesprungen", optionen.Instanz, marke.Id);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LetzterFehler = e.Message;
            log.LogError(e, "Sprung zu {Marke} gescheitert", markeId);
            throw;
        }
        finally
        {
            uhr.WartungSetzen(false);
            sprung.Release();
        }
    }

    public void TeilnehmerSteuernSetzen(bool erlaubt)
    {
        optionen.TeilnehmerSteuern = erlaubt;
        uhr.TempoSetzen(uhr.Tempo); // Leiste aktualisieren
    }

    private async Task VorlageKopierenAsync(Sprungmarke marke, string ziel, CancellationToken abbruch)
    {
        var vorlage = marke.Vorlage(optionen.Vorlagen);
        var kommentar = await datenbanken.KommentarAsync(vorlage, abbruch)
                        ?? throw new InvalidOperationException($"Die Vorlage „{vorlage}“ fehlt. Bitte die Vorlagen bauen: tools/instanzen.ps1 vorlagen");
        string aktuell;
        await using (var bereich = dienste.CreateAsyncScope())
        {
            aktuell = Schema.Version(bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>());
        }

        if (Wert(kommentar, "schema") != aktuell)
        {
            // Vorlagen gehören zum Softwarestand (R4): lieber klar abbrechen als still eine veraltete Kopie nutzen.
            throw new InvalidOperationException($"Die Vorlage „{vorlage}“ passt nicht zum Softwarestand (Schema {Wert(kommentar, "schema")}, erwartet {aktuell}). " +
                                                "Bitte die Vorlagen neu bauen: tools/instanzen.ps1 vorlagen");
        }

        Bezugstag = DateOnly.Parse(Wert(kommentar, "bezug")!, System.Globalization.CultureInfo.InvariantCulture);
        await datenbanken.KopierenAsync(vorlage, ziel,
            $"{Schulungsdatenbanken.Instanzkennung}; instanz={optionen.Instanz}; marke={marke.Id}; bezug={Bezugstag:yyyy-MM-dd}", abbruch);
    }

    private static string? Wert(string kommentar, string schluessel)
        => kommentar.Split(';').Select(t => t.Trim()).FirstOrDefault(t => t.StartsWith(schluessel + "=", StringComparison.Ordinal))?[(schluessel.Length + 1)..];

    /// <summary>
    /// Vorlagen bauen (QA-68): Spielzeit auf Montag 7:30 dieser Woche, Datenbank anlegen und mit Start- und Demodaten füllen
    /// (<paramref name="fuellen"/>), dann je Marke die Spielzeit vorlaufen lassen (zeitgesteuerte Läufe einmal je fälligem Zeitpunkt) und
    /// die Datenbank als Vorlage kopieren.
    /// </summary>
    public async Task VorlagenBauenAsync(Func<IServiceProvider, Task> fuellen, CancellationToken abbruch = default)
    {
        var bezug = Schulungswoche.Bezugstag(DateOnly.FromDateTime(DateTime.Today));
        var bau = datenbanken.Datenbank;
        string schemaversion;
        await using (var bereich = dienste.CreateAsyncScope())
        {
            schemaversion = Schema.Version(bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>());
        }

        await datenbanken.LoeschenAsync(bau, abbruch);
        var erste = Schulungswoche.Marken[0];
        uhr.Stellen(Zeitpunkt(erste, bezug));
        await DatenbankInitialisierung.InitialisierenAsync(dienste, abbruch);
        await fuellen(dienste);
        var vorher = uhr.GetUtcNow();
        foreach (var marke in Schulungswoche.Marken)
        {
            var ziel = Zeitpunkt(marke, bezug);
            if (ziel > vorher)
            {
                // in Stunden-Schritten, damit stündliche Läufe je Stunde einmal laufen
                for (var t = vorher; t < ziel;)
                {
                    var naechster = t.AddHours(1) < ziel ? t.AddHours(1) : ziel;
                    uhr.Stellen(naechster);
                    await Spielzeitplaner.NachholenAsync(dienste, t, naechster, uhr.LocalTimeZone, log, abbruch);
                    t = naechster;
                }

                vorher = ziel;
            }

            var vorlage = marke.Vorlage(optionen.Vorlagen);
            await datenbanken.LoeschenAsync(vorlage, abbruch);
            await datenbanken.KopierenAsync(bau, vorlage, string.Empty, abbruch);
            await datenbanken.AlsVorlageMarkierenAsync(vorlage,
                $"{Schulungsdatenbanken.Vorlagenkennung}; marke={marke.Id}; bezug={bezug:yyyy-MM-dd}; schema={schemaversion}", abbruch);
            log.LogInformation("Vorlage {Vorlage} gebaut ({Zeit:ddd dd.MM. HH:mm})", vorlage, Zeitpunkt(marke, bezug).LocalDateTime);
        }

        await datenbanken.LoeschenAsync(bau, abbruch);
    }
}
