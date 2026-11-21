using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Protokoll;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Zeitbezug der Demodaten: Sie sind auf den <see cref="Bezugstag"/> (Montag, 16.11.2026) zugeschnitten und werden beim Anlegen um
/// ganze Wochen auf das aktuelle Datum der Uhr verschoben – Wochentage, Abstände, Fristen und Fälligkeiten bleiben gleich, eine Demo
/// in zwei Wochen sieht also aus wie heute. Semesternamen, Kürzel (F/H) und Kursnummern-Präfixe werden aus der verschobenen Lage
/// abgeleitet. Die Verschiebung wird beim ersten Anlegen protokolliert („Demodaten.Bezug“), damit später nachgereichte Demodaten
/// dieselbe Verschiebung nutzen. Schulferien bleiben echte Daten (amtliche Ferienordnung) und werden nicht verschoben.
/// </summary>
internal sealed partial class Demozeit
{
    /// <summary>Tag, auf den die Demodaten inhaltlich zugeschnitten sind.</summary>
    public static readonly DateOnly Bezugstag = new(2026, 11, 16);

    private const string Aktion = "Demodaten.Bezug";

    /// <summary>Halbjahr des Bezugstags: Herbst 2026 („26H“) als Ordinalzahl Jahr·2 + (Herbst ? 1 : 0).</summary>
    private const int BezugsHalbjahr = 2026 * 2 + 1;

    public Demozeit(int tage)
    {
        if (tage % 7 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tage), "Die Verschiebung muss aus ganzen Wochen bestehen.");
        }

        Tage = tage;
        // Das Demo-Herbstsemester (24.08.2026–31.01.2027) heißt nach der Jahreszeit seiner verschobenen Mitte.
        HalbjahrVerschiebung = Halbjahr(new DateOnly(2026, 11, 12).AddDays(tage)) - BezugsHalbjahr;
    }

    /// <summary>Verschiebung in Tagen (ganze Wochen).</summary>
    public int Tage { get; }

    /// <summary>Um wie viele Halbjahre sich Semesternamen und Kürzel verschieben.</summary>
    public int HalbjahrVerschiebung { get; }

    /// <summary>Ganze Wochen vom Bezugstag bis heute (abgerundet), damit die Wochentage erhalten bleiben.</summary>
    public static Demozeit Fuer(DateOnly heute)
    {
        var differenz = heute.DayNumber - Bezugstag.DayNumber;
        return new Demozeit(differenz - (((differenz % 7) + 7) % 7));
    }

    /// <summary>
    /// Verschiebung für diese Datenbank: die beim ersten Anlegen protokollierte; ohne Eintrag 0, wenn schon Demodaten aus der Zeit vor
    /// dieser Regel bestehen (Semester vorhanden), sonst neu aus der Uhr berechnet und protokolliert.
    /// </summary>
    public static async Task<Demozeit> LadenAsync(VibeVhsDbContext db, TimeProvider uhr)
    {
        var eintrag = await db.Protokoll.AsNoTracking().Where(e => e.Aktion == Aktion).OrderBy(e => e.Zeitpunkt).ThenBy(e => e.Id)
            .Select(e => e.Beschreibung).FirstOrDefaultAsync();
        if (eintrag is not null && TageMuster().Match(eintrag) is { Success: true } m)
        {
            return new Demozeit(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        if (await db.Semester.AnyAsync())
        {
            return new Demozeit(0);
        }

        var z = Fuer(DateOnly.FromDateTime(uhr.GetLocalNow().Date));
        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, Aktion, null, null,
            $"Demodaten relativ zum Tagesdatum: Verschiebung {z.Tage} Tage gegenüber dem Bezugstag {Bezugstag:dd.MM.yyyy}"));
        await db.SaveChangesAsync();
        return z;
    }

    public DateOnly D(int jahr, int monat, int tag) => new DateOnly(jahr, monat, tag).AddDays(Tage);

    public DateOnly D(DateOnly datum) => datum.AddDays(Tage);

    public DateTime DT(int jahr, int monat, int tag, int stunde, int minute = 0) => new DateTime(jahr, monat, tag, stunde, minute, 0).AddDays(Tage);

    /// <summary>Zeitpunkt in deutscher Ortszeit; der Versatz (MEZ/MESZ) ergibt sich aus dem verschobenen Datum.</summary>
    public DateTimeOffset DTO(int jahr, int monat, int tag, int stunde, int minute = 0)
    {
        var ortszeit = DT(jahr, monat, tag, stunde, minute);
        return new DateTimeOffset(ortszeit, Zeitzone.GetUtcOffset(ortszeit));
    }

    /// <summary>ISO-Datum aus den Kursplänen („2026-11-26“), verschoben.</summary>
    public DateOnly D(string iso) => D(DateOnly.Parse(iso, CultureInfo.InvariantCulture));

    /// <summary>Semesterkürzel verschieben: „26H“ → z. B. „27F“.</summary>
    public string Kuerzel(string alt)
    {
        var (jahr, herbst) = (2000 + int.Parse(alt[..2], CultureInfo.InvariantCulture), alt[2] == 'H');
        var neu = jahr * 2 + (herbst ? 1 : 0) + HalbjahrVerschiebung;
        return $"{neu / 2 % 100:00}{(neu % 2 == 1 ? 'H' : 'F')}";
    }

    /// <summary>Semestername zum (verschobenen) Kürzel: „Herbst 2026“, „Frühjahr 2027“.</summary>
    public static string Semestername(string kuerzel) => $"{(kuerzel[2] == 'H' ? "Herbst" : "Frühjahr")} 20{kuerzel[..2]}";

    /// <summary>Kursnummer mit verschobenem Semester-Präfix: „26H-501“ → z. B. „27F-501“.</summary>
    public string Nr(string alt) => $"{Kuerzel(alt[..3])}{alt[3..]}";

    /// <summary>Umkehrung von <see cref="Nr"/>: verschobene Kursnummer → Kursnummer der Vorlage (für Fallunterscheidungen).</summary>
    public string Alt(string neu)
    {
        foreach (var alt in new[] { "25F", "25H", "26F", "26H", "27F", "27H" })
        {
            if (neu.StartsWith(Kuerzel(alt), StringComparison.Ordinal))
            {
                return $"{alt}{neu[3..]}";
            }
        }

        return neu;
    }

    /// <summary>Halbjahr eines Datums: Februar–Juli = Frühjahr, August–Januar = Herbst (Januar zählt zum Herbst des Vorjahrs).</summary>
    internal static int Halbjahr(DateOnly d) => d.Month switch
    {
        1 => (d.Year - 1) * 2 + 1,
        >= 2 and <= 7 => d.Year * 2,
        _ => d.Year * 2 + 1,
    };

    private static readonly TimeZoneInfo Zeitzone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [GeneratedRegex(@"Verschiebung (-?\d+) Tage")]
    private static partial Regex TageMuster();
}
