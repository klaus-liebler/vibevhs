using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Datenschutz;

public sealed record PersonAktivitaet(Guid Id, string Anzeigename, DateTimeOffset LetzteAktivitaet, Datenschutzstatus Status);

public sealed record KursEnde(Guid Id, string Kursnummer, string Titel, DateOnly? Ende, bool Archiviert);

public interface IDatenschutzSpeicher
{
    Task<IReadOnlyList<Aufbewahrungsfrist>> FristenAsync(CancellationToken abbruch = default);

    Task<Aufbewahrungsfrist?> FristLadenAsync(string schluessel, CancellationToken abbruch = default);

    /// <summary>Vorgänge, die die Aufbewahrung von Daten der Person begründen (z. B. Honorare als Dozent/in).</summary>
    Task<IReadOnlyList<Aufbewahrungsgrund>> GruendeAsync(Guid personId, CancellationToken abbruch = default);

    /// <summary>Personen (ohne Mitarbeitende mit Konto), deren letzte Aktivität vor <paramref name="vor"/> liegt.</summary>
    Task<IReadOnlyList<PersonAktivitaet>> InaktivePersonenAsync(DateTimeOffset vor, CancellationToken abbruch = default);

    Task<IReadOnlyList<KursEnde>> KurseEndeVorAsync(DateOnly vor, CancellationToken abbruch = default);

    Task<IReadOnlyList<Semester>> SemesterEndeVorAsync(DateOnly vor, CancellationToken abbruch = default);

    Task<IReadOnlyList<Kurs>> KurseLadenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default);

    Task<IReadOnlyList<Kurs>> KurseDesSemestersAsync(Guid semesterId, CancellationToken abbruch = default);

    Task<Semester?> SemesterLadenAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Kurs>> KurseMitDozentAsync(Guid personId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Person>> PersonenMitVollmachtFuerAsync(Guid bevollmaechtigterId, CancellationToken abbruch = default);

    Task<IReadOnlyList<AusgehendeNachricht>> NachrichtenAsync(Guid personId, CancellationToken abbruch = default);

    Task<IReadOnlyList<AuditEintrag>> ProtokollZuAsync(Guid personId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Loeschlauf>> LoeschlaeufeAsync(CancellationToken abbruch = default);

    Task<Loeschlauf?> LoeschlaufLadenAsync(Guid id, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Auskunft nach Art. 15 DSGVO (als JSON und PDF ausgegeben).</summary>
public sealed record Auskunft(
    string Einrichtung,
    DateTimeOffset ErstelltAm,
    string ErstelltVon,
    Guid PersonId,
    string Name,
    string Datenschutzstatus,
    AuskunftStammdaten Stammdaten,
    IReadOnlyList<Personen.HistorienEintrag> Historie,
    IReadOnlyList<AuskunftKurs> KurseAlsDozent,
    IReadOnlyList<AuskunftNachricht> Nachrichten,
    IReadOnlyList<string> Empfaenger,
    IReadOnlyList<AuskunftFrist> Aufbewahrung,
    IReadOnlyList<AuskunftProtokoll> Verarbeitungen,
    IReadOnlyList<string> Dokumente,
    IReadOnlyList<string> Hinweise);

public sealed record AuskunftStammdaten(string? Titel, string? Geburtsdatum, string Rollen, string? Sprache, IReadOnlyList<string> Adressen,
    IReadOnlyList<string> Rufnummern, IReadOnlyList<string> EMails, IReadOnlyList<string> Organisationen, IReadOnlyList<string> Vollmachten,
    IReadOnlyList<string> Einwilligungen);

public sealed record AuskunftKurs(string Kursnummer, string Titel, int Termine, string? Zeitraum);

public sealed record AuskunftNachricht(DateTimeOffset Datum, string Kanal, string Betreff, string Status);

public sealed record AuskunftFrist(string Datenart, string Grund, string Fristende, string Rechtsgrundlage);

public sealed record AuskunftProtokoll(DateTimeOffset Zeitpunkt, string Aktion, string Bearbeiter);

public interface IAuskunftPdf
{
    byte[] Erzeugen(Auskunft auskunft);
}
