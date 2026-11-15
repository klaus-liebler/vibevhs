using System.Globalization;
using System.Text;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Anmeldung;

/// <summary>Erzeugt eine iCalendar-Datei (RFC 5545) mit den Terminen eines Kurses.</summary>
public static class Kalenderdatei
{
    private static readonly TimeZoneInfo Zeitzone = ZeitzoneFinden();

    public static string Erzeugen(Kurs kurs, IEnumerable<Veranstaltung> termine, string einrichtung, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(kurs);
        var sb = new StringBuilder();
        Zeile(sb, "BEGIN:VCALENDAR");
        Zeile(sb, "VERSION:2.0");
        Zeile(sb, "PRODID:-//VibeVHS//Kursanmeldung//DE");
        Zeile(sb, "CALSCALE:GREGORIAN");
        Zeile(sb, "METHOD:PUBLISH");
        Zeile(sb, "X-WR-CALNAME:" + Text($"{kurs.Kursnummer} {kurs.Titel}"));
        foreach (var v in termine.Where(v => !v.Ausgefallen).OrderBy(v => v.Beginn))
        {
            Zeile(sb, "BEGIN:VEVENT");
            Zeile(sb, $"UID:{v.Id:N}@vibevhs");
            Zeile(sb, "DTSTAMP:" + Utc(jetzt.UtcDateTime));
            Zeile(sb, "DTSTART:" + Utc(Lokal(v.Beginn)));
            Zeile(sb, "DTEND:" + Utc(Lokal(v.Ende)));
            Zeile(sb, "SUMMARY:" + Text($"{kurs.Titel} ({kurs.Kursnummer}), Termin {v.Nummer}"));
            Zeile(sb, "LOCATION:" + Text(string.Join(" + ", v.Orte.Select(o => o.OrtName))));
            Zeile(sb, "DESCRIPTION:" + Text($"{einrichtung}{(string.IsNullOrEmpty(v.DozentName) ? "" : $", Leitung: {v.DozentName}")}"));
            Zeile(sb, "END:VEVENT");
        }

        Zeile(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>Kalender über mehrere Kurse (Kalender-Abo der Kursleitung); ausgefallene Termine erscheinen als abgesagt.</summary>
    public static string Erzeugen(string name, IEnumerable<(Kurs Kurs, Veranstaltung Termin, string? Hinweis)> termine, string einrichtung, DateTimeOffset jetzt)
    {
        var sb = new StringBuilder();
        Zeile(sb, "BEGIN:VCALENDAR");
        Zeile(sb, "VERSION:2.0");
        Zeile(sb, "PRODID:-//VibeVHS//Dozierendenportal//DE");
        Zeile(sb, "CALSCALE:GREGORIAN");
        Zeile(sb, "METHOD:PUBLISH");
        Zeile(sb, "X-WR-CALNAME:" + Text(name));
        Zeile(sb, "REFRESH-INTERVAL;VALUE=DURATION:PT4H");
        foreach (var (kurs, v, hinweis) in termine.OrderBy(t => t.Termin.Beginn))
        {
            Zeile(sb, "BEGIN:VEVENT");
            Zeile(sb, $"UID:{v.Id:N}@vibevhs");
            Zeile(sb, "DTSTAMP:" + Utc(jetzt.UtcDateTime));
            Zeile(sb, "DTSTART:" + Utc(Lokal(v.Beginn)));
            Zeile(sb, "DTEND:" + Utc(Lokal(v.Ende)));
            Zeile(sb, "SUMMARY:" + Text($"{(v.Ausgefallen ? "ENTFÄLLT: " : "")}{kurs.Titel} ({kurs.Kursnummer}), Termin {v.Nummer}"));
            Zeile(sb, "LOCATION:" + Text(string.Join(" + ", v.Orte.Select(o => o.OrtName))));
            Zeile(sb, "DESCRIPTION:" + Text(hinweis is null ? einrichtung : einrichtung + "\n" + hinweis));
            if (v.Ausgefallen)
            {
                Zeile(sb, "STATUS:CANCELLED");
            }

            Zeile(sb, "END:VEVENT");
        }

        Zeile(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    private static DateTime Lokal(DateTime wanduhr) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(wanduhr, DateTimeKind.Unspecified), Zeitzone);

    private static string Utc(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Text(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>Zeilen über 75 Zeichen werden nach RFC 5545 gefaltet.</summary>
    private static void Zeile(StringBuilder sb, string zeile)
    {
        while (zeile.Length > 74)
        {
            sb.Append(zeile, 0, 74).Append("\r\n ");
            zeile = zeile[74..];
        }

        sb.Append(zeile).Append("\r\n");
    }

    private static TimeZoneInfo ZeitzoneFinden()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }
}
