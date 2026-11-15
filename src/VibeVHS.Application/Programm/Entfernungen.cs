using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

/// <summary>Koordinaten zu einer Postleitzahl. In Stufe 1 ein Mock mit einer Tabelle der Region; später ein Geodienst.</summary>
public interface IGeokodierer
{
    (double Breite, double Laenge)? Postleitzahl(string? plz);
}

/// <summary>
/// Entfernung vom Wohnort einer Person (Postleitzahl der Briefanschrift) zu einem Kursort: Luftlinie mal Umwegfaktor
/// (Mock für Fahrtkosten von Kursleitungen und Teilnehmenden).
/// </summary>
public sealed class Entfernungen(IProgrammSpeicher programm, IGeokodierer geo, TimeProvider uhr)
{
    /// <summary>Liefert eine Funktion Ort → einfache Entfernung in km (oder leer, wenn eine Seite keine Koordinaten hat).</summary>
    public async Task<Func<Guid?, decimal?>> FuerPersonAsync(Person person, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(person);
        var jetzt = uhr.GetLocalNow();
        var stand = person.StandAm(DateOnly.FromDateTime(jetzt.Date), jetzt);
        var plz = (stand.BriefAn ?? stand.Adressen.FirstOrDefault())?.Postleitzahl;
        var start = geo.Postleitzahl(plz);
        if (start is null)
        {
            return _ => null;
        }

        var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
        return ortId => ortId is { } id && verzeichnis.Koordinaten(id) is { } ziel
            ? Geo.StrasseKm(start.Value.Breite, start.Value.Laenge, ziel.Breite, ziel.Laenge)
            : null;
    }
}
