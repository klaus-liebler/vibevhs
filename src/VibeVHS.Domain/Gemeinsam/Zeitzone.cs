namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Zentrale Anzeige- und Eingabezeitzone. In der Datenbank stehen alle Zeitpunkte in UTC (Millisekunden seit 1970); Termine werden als
/// Ortszeit („dienstags 18:00“) geplant und je Termin über diese Zeitzone nach UTC umgerechnet – so bleiben sie nach der Zeitumstellung
/// um 18:00 Ortszeit. Einstellung „Zeitzone“ (z. B. Europe/Berlin); ohne Einstellung gilt die Systemzeitzone des Servers.
/// Später je Mandant (siehe docs/mandanten.md).
/// </summary>
public static class Zeitzone
{
    private static TimeZoneInfo _aktuell = TimeZoneInfo.Local;

    public static TimeZoneInfo Aktuell => _aktuell;

    /// <summary>Beim Start aus der Einstellung setzen (IANA- oder Windows-Kennung); leer = Systemzeitzone.</summary>
    public static void Festlegen(string? kennung)
        => _aktuell = string.IsNullOrWhiteSpace(kennung) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(kennung.Trim());

    /// <summary>Ortszeit (Wanduhr) → UTC. In der Lücke der Sommerzeitumstellung (02:00–03:00) zählt die Zeit nach der Umstellung.</summary>
    public static DateTime NachUtc(DateTime ortszeit)
    {
        var w = DateTime.SpecifyKind(ortszeit, DateTimeKind.Unspecified);
        if (_aktuell.IsInvalidTime(w))
        {
            w = w.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(w, _aktuell);
    }

    /// <summary>UTC → Ortszeit (Wanduhr, ohne Kennzeichen).</summary>
    public static DateTime AusUtc(DateTime utc)
        => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _aktuell), DateTimeKind.Unspecified);

    /// <summary>Zeitpunkt in der Anzeigezeitzone (für Datum, Uhrzeit, „heute“).</summary>
    public static DateTimeOffset Ortszeit(DateTimeOffset zeitpunkt) => TimeZoneInfo.ConvertTime(zeitpunkt, _aktuell);
}
