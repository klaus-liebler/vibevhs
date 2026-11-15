using VibeVHS.Domain.Portal;

namespace VibeVHS.Application.Portal;

public interface IPortalSpeicher
{
    Task<Datei?> DateiAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Kursunterlage>> UnterlagenAsync(Guid kursId, CancellationToken abbruch = default);

    Task<Kursunterlage?> UnterlageAsync(Guid id, CancellationToken abbruch = default);

    Task<Kursunterlage?> UnterlageMitTokenAsync(string tokenHash, CancellationToken abbruch = default);

    Task<IReadOnlyList<Portalantrag>> AntraegeAsync(Guid? personId = null, bool nurOffene = false, CancellationToken abbruch = default);

    Task<Portalantrag?> AntragAsync(Guid id, CancellationToken abbruch = default);

    /// <summary>Nicht archivierte Kurse, in denen die Person im Team ist oder einen Termin leitet (mit Terminen, verfolgt).</summary>
    Task<IReadOnlyList<Domain.Programm.Kurs>> KurseDerPersonAsync(Guid personId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Domain.Personen.Person>> PersonenMitEMailAsync(string eMail, CancellationToken abbruch = default);

    Task<Sicherheit.Benutzerkonto?> KontoZurPersonAsync(Guid personId, CancellationToken abbruch = default);

    /// <summary>Personen, die der angegebenen Person (irgendwann) eine Buchungsvollmacht erteilt haben.</summary>
    Task<IReadOnlyList<Domain.Personen.Person>> PersonenMitVollmachtFuerAsync(Guid bevollmaechtigterId, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Virenprüfung hochgeladener Dateien (Port; vorerst Mock, später z. B. ClamAV der Kommune).</summary>
public interface IVirenpruefung
{
    /// <returns>null = unbedenklich, sonst Befund.</returns>
    Task<string?> PruefenAsync(string dateiname, byte[] inhalt, CancellationToken abbruch = default);
}

/// <summary>Fehler im Portal mit HTTP-Status (403 fremder Kurs, 422 Eingabe …).</summary>
public sealed class PortalFehler(string meldung, int status = 422) : Exception(meldung)
{
    public int Status { get; } = status;
}

public sealed record PortalTermin(
    Guid VeranstaltungId,
    Guid KursId,
    string Kursnummer,
    string Kurstitel,
    int Nummer,
    DateTime Beginn,
    DateTime Ende,
    string Orte,
    string? Wegbeschreibung,
    bool Ausgefallen,
    string? Aenderung,
    bool ListeAbgeschlossen,
    int Teilnehmende);

public sealed record PortalKurs(Guid Id, string Kursnummer, string Titel, int Fachbereich, int Termine, int Teilnehmende, DateTime? Beginn, DateTime? Ende, bool Leitung);

public sealed record PortalUnterlage(Guid Id, string Titel, string Dateiname, long Groesse, DateTimeOffset HochgeladenAm, int Abrufe, bool Zurueckgezogen);

public sealed record PortalAbrechnung(Guid BelegId, string? Nummer, DateOnly Belegdatum, long BetragCent, bool Festgeschrieben, bool Storniert, bool PdfVorhanden,
    IReadOnlyList<string> Positionen, Portalantrag? OffenerEinwand);

public sealed record PortalDaten(
    string Name,
    string? EMail,
    string? Adresse,
    string? Strasse,
    string? Postleitzahl,
    string? Ort,
    string? Bankverbindung,
    string Beschaeftigung,
    IReadOnlyList<(string Name, string? Wert, DateOnly Von, DateOnly? Bis)> Qualifikationen,
    IReadOnlyList<Portalantrag> Antraege);

/// <summary>Grenzen für Uploads.</summary>
public static class Uploadregeln
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, string> Erlaubt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".txt"] = "text/plain",
        [".mp3"] = "audio/mpeg",
    };

    public static string Hinweis => $"Erlaubt: {string.Join(", ", Erlaubt.Keys.Select(k => k.TrimStart('.').ToUpperInvariant()).Distinct())}, höchstens {MaxBytes / 1024 / 1024} MB.";

    /// <summary>Prüft Endung, Größe und Dateisignatur (PDF/PNG/JPEG/ZIP-Formate); liefert den Inhaltstyp.</summary>
    public static string Pruefen(string dateiname, byte[] inhalt)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var endung = Path.GetExtension(dateiname ?? "");
        if (!Erlaubt.TryGetValue(endung, out var typ))
        {
            throw new PortalFehler($"Dateityp „{endung}“ ist nicht erlaubt. {Hinweis}");
        }

        if (inhalt.Length == 0 || inhalt.LongLength > MaxBytes)
        {
            throw new PortalFehler($"Die Datei ist leer oder zu groß. {Hinweis}");
        }

        bool Beginnt(params byte[] sig) => inhalt.Length >= sig.Length && inhalt.AsSpan(0, sig.Length).SequenceEqual(sig);
        var passt = endung.ToLowerInvariant() switch
        {
            ".pdf" => Beginnt(0x25, 0x50, 0x44, 0x46),
            ".png" => Beginnt(0x89, 0x50, 0x4E, 0x47),
            ".jpg" or ".jpeg" => Beginnt(0xFF, 0xD8, 0xFF),
            ".docx" or ".xlsx" or ".pptx" or ".odt" => Beginnt(0x50, 0x4B, 0x03, 0x04),
            _ => true,
        };
        if (!passt)
        {
            throw new PortalFehler($"Der Inhalt passt nicht zur Dateiendung „{endung}“.");
        }

        return typ;
    }
}
