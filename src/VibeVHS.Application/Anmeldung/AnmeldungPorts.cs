using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Vorlagen;

namespace VibeVHS.Application.Anmeldung;

/// <summary>Belegung einer Person mit Kursangaben (für die Personenseite).</summary>
public sealed record PersonBelegung(Belegung Belegung, string Kurstitel, DateTime? Beginn, bool KursAbgesagt);

public interface IAnmeldungSpeicher
{
    /// <summary>Alle Belegungen eines Kurses mit Ereignissen, verfolgt (für Änderungen).</summary>
    Task<IReadOnlyList<Belegung>> BelegungenDesKursesAsync(Guid kursId, CancellationToken abbruch = default);

    Task<IReadOnlyList<PersonBelegung>> BelegungenDerPersonAsync(Guid personId, CancellationToken abbruch = default);

    /// <summary>Alle Belegungen einer Person, verfolgt (z. B. für die Anonymisierung).</summary>
    Task<IReadOnlyList<Belegung>> BelegungenZuPersonAsync(Guid personId, CancellationToken abbruch = default);

    Task<Belegung?> BelegungLadenAsync(Guid id, CancellationToken abbruch = default);

    /// <summary>Belegte Plätze und Wartende je Kurs.</summary>
    Task<IReadOnlyDictionary<Guid, (int Belegt, int Wartend)>> BelegungszahlenAsync(IEnumerable<Guid> kursIds, CancellationToken abbruch = default);

    /// <summary>Offene Belegungen einer Person je Kurs (für die Buchungsprüfung in der Kursauswahl).</summary>
    Task<IReadOnlyDictionary<Guid, Belegungsstatus>> OffeneBelegungenAsync(Guid personId, CancellationToken abbruch = default);

    /// <summary>Nicht archivierte Kurse der Semester mit Veranstaltungen (ohne Verfolgung).</summary>
    Task<IReadOnlyList<Kurs>> KurseZurAuswahlAsync(IEnumerable<Guid> semesterIds, CancellationToken abbruch = default);

    Task<IReadOnlyList<Ermaessigung>> ErmaessigungenAsync(Guid personId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Belegung>> AbgelaufeneAngeboteAsync(DateTimeOffset jetzt, CancellationToken abbruch = default);

    Task<IReadOnlyList<Belegung>> KontingenteMitAblaufVorAsync(DateOnly tag, CancellationToken abbruch = default);

    /// <summary>Kurse, in denen jemand auf der Warteliste steht.</summary>
    Task<IReadOnlyList<Guid>> KurseMitWartelisteAsync(CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

public interface IVorlagenSpeicher
{
    Task<IReadOnlyList<Textvorlage>> VorlagenAsync(CancellationToken abbruch = default);

    Task<Textvorlage?> VorlageAsync(string schluessel, CancellationToken abbruch = default);

    void Hinzufuegen(Textvorlage vorlage);

    Task SpeichernAsync(CancellationToken abbruch = default);
}
