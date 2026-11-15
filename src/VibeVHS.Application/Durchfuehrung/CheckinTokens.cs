using VibeVHS.Domain.Durchfuehrung;

namespace VibeVHS.Application.Durchfuehrung;

public enum Tokencode
{
    Gueltig,
    Ungueltig,
    NochNichtGueltig,
    Abgelaufen,
    FalscherRaum,
}

public sealed record Tokenpruefung(Tokencode Code, Guid? VeranstaltungId, Guid? OrtId, string Text)
{
    public bool Gueltig => Code == Tokencode.Gueltig;
}

/// <summary>
/// Termin-Codes für den QR-Aushang je Veranstaltung und Ort: zufälliger Verweis (keine Daten im Code), nur im Zeitfenster gültig.
/// </summary>
public sealed class Termincodes(IZugangstokenSpeicher speicher, TimeProvider uhr)
{
    /// <summary>Liefert den Code für Veranstaltung, Ort und Zeitfenster (einmal erzeugt, danach derselbe).</summary>
    public async Task<string> CodeAsync(Guid veranstaltungId, Guid ortId, DateTimeOffset gueltigVon, DateTimeOffset gueltigBis, CancellationToken abbruch = default)
    {
        if (await speicher.TermincodeFuerAsync(veranstaltungId, ortId, abbruch) is { } vorhanden && vorhanden.GueltigVon == gueltigVon && vorhanden.GueltigBis == gueltigBis)
        {
            return vorhanden.Code;
        }

        var neu = new Termincode(veranstaltungId, ortId, gueltigVon, gueltigBis, uhr.GetUtcNow());
        speicher.Hinzufuegen(neu);
        await speicher.SpeichernAsync(abbruch);
        return neu.Code;
    }

    /// <summary>Prüft Code und Zeitfenster; mit erwarteter Veranstaltung/Ort auch, ob der Code zum Raum gehört.</summary>
    public async Task<Tokenpruefung> PruefenAsync(string? code, Guid? erwarteteVeranstaltung = null, Guid? erwarteterOrt = null, CancellationToken abbruch = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40 || await speicher.TermincodeAsync(code.Trim(), abbruch) is not { } t)
        {
            return new(Tokencode.Ungueltig, null, null, "Dieser QR-Code ist ungültig.");
        }

        var jetzt = uhr.GetUtcNow();
        if (jetzt < t.GueltigVon)
        {
            return new(Tokencode.NochNichtGueltig, t.VeranstaltungId, t.OrtId, $"Dieser Code gilt erst ab {t.GueltigVon.ToLocalTime():dd.MM. HH:mm} Uhr.");
        }

        if (jetzt > t.GueltigBis)
        {
            return new(Tokencode.Abgelaufen, t.VeranstaltungId, t.OrtId, $"Dieser Code ist seit {t.GueltigBis.ToLocalTime():dd.MM. HH:mm} Uhr abgelaufen.");
        }

        if ((erwarteteVeranstaltung is { } ev && ev != t.VeranstaltungId) || (erwarteterOrt is { } eo && eo != t.OrtId))
        {
            return new(Tokencode.FalscherRaum, t.VeranstaltungId, t.OrtId, "Dieser Code gehört zu einem anderen Raum oder einer anderen Veranstaltung.");
        }

        return new(Tokencode.Gueltig, t.VeranstaltungId, t.OrtId, "gültig");
    }

    /// <summary>Liest den Code aus einem gescannten Inhalt (ganze Adresse …/checkin/CODE oder nur der Code).</summary>
    public static string? AusScan(string? inhalt)
    {
        if (string.IsNullOrWhiteSpace(inhalt))
        {
            return null;
        }

        var s = inhalt.Trim();
        var i = s.IndexOf("/checkin/", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            s = s[(i + "/checkin/".Length)..];
        }

        s = s.Split('?', '#')[0].Trim('/');
        return s.Contains('/', StringComparison.Ordinal) ? null : s;
    }
}
