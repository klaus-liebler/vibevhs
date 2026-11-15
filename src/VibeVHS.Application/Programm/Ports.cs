using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

/// <summary>Zeile der Kursliste (ohne alle Veranstaltungen zu laden).</summary>
public sealed record KursZeile(
    Guid Id,
    string Kursnummer,
    string Titel,
    int Fachbereich,
    Kursstatus Status,
    Sichtbarkeit Sichtbarkeit,
    Kursumfang Umfang,
    Guid SemesterId,
    DateTime? ErsterTermin,
    DateTime? LetzterTermin,
    int Termine,
    string? Ort,
    string? Leitung,
    int MaxTeilnehmende,
    bool Archiviert = false);

public sealed record KursFilter(Guid? SemesterId = null, int? Fachbereich = null, Kursstatus? Status = null, string? Suche = null, bool Archivierte = false);

/// <summary>Termin mit Kurs- und Ortsangaben für Raumplan und Konfliktprüfung.</summary>
public sealed record PlanTermin(Termin Termin, string Kursnummer, IReadOnlyList<string> OrtNamen, string Dozent, int Fachbereich);

public interface IProgrammSpeicher
{
    Task<IReadOnlyList<Semester>> SemesterAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Kursart>> KursartenAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<KursZeile>> KurseAsync(KursFilter filter, CancellationToken abbruch = default);

    /// <summary>Kurs mit Veranstaltungen und Entgelten, verfolgt (für Änderungen).</summary>
    Task<Kurs?> KursLadenAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Kurs>> KurseMitTerminenAsync(Guid semesterId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Kurs>> UnbefristeteKurseAsync(CancellationToken abbruch = default);

    Task<bool> KursnummerVergebenAsync(string nummer, CancellationToken abbruch = default);

    Task<Ortsverzeichnis> OrtsverzeichnisAsync(CancellationToken abbruch = default);

    /// <summary>Orte verfolgt (für Merkmaländerungen).</summary>
    Task<Ort?> OrtLadenAsync(Guid id, CancellationToken abbruch = default);

    Task<Kalender> KalenderAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Ferienzeitraum>> FerienAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<PlanTermin>> TermineAsync(DateTime von, DateTime bis, CancellationToken abbruch = default);

    Task<bool> AufgabeExistiertAsync(string schluessel, CancellationToken abbruch = default);

    Task<IReadOnlyList<Aufgabe>> OffeneAufgabenAsync(string? gruppe, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

public enum Treffertyp
{
    Person,
    Kurs,
    Ort,
}

public sealed record Suchtreffer(Treffertyp Typ, string Titel, string? Zusatz, string Ziel);

/// <summary>Globale Suche in der Kopfzeile: Personen, Kurse, Orte.</summary>
public interface IGlobaleSuche
{
    Task<IReadOnlyList<Suchtreffer>> SuchenAsync(string text, int jeTyp = 5, CancellationToken abbruch = default);
}
