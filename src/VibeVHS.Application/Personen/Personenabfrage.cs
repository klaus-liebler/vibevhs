using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

/// <summary>Filter der Personenliste. Zusammengeführte Dubletten sind ausgeblendet, außer man filtert danach.</summary>
public sealed record PersonenFilter(
    string? Suche = null,
    Personenrolle? Rolle = null,
    Personenstatus? Status = null,
    int? Fachbereich = null,
    bool NurDublettenHinweise = false)
{
    public bool IstLeer => string.IsNullOrWhiteSpace(Suche) && Rolle is null && Status is null && Fachbereich is null && !NurDublettenHinweise;
}

/// <summary>Eine Zeile der Personenliste (aus den Zwischenstands-Spalten, ohne die Historie zu laden).</summary>
public sealed record PersonZeile(
    Guid Id,
    string Anzeigename,
    string Sortierschluessel,
    DateOnly? Geburtsdatum,
    string? Wohnort,
    string? EMail,
    string Sprache,
    Personenrolle Rollen,
    Personenstatus Status,
    IReadOnlyList<int> Fachbereiche);

/// <summary>Position in der sortierten Liste für die Keyset-Pagination (statt Seitennummer und OFFSET).</summary>
public sealed record Seitenschluessel(string Wert)
{
    public static Seitenschluessel Von(PersonZeile zeile) => new(zeile.Sortierschluessel);
}

/// <summary>Trefferzahlen für die Filter-Chips: je Wert, mit allen übrigen gesetzten Filtern.</summary>
public sealed record Filterzahlen(
    int Gesamt,
    IReadOnlyDictionary<Personenrolle, int> Rollen,
    IReadOnlyDictionary<Personenstatus, int> Status,
    IReadOnlyDictionary<int, int> Fachbereiche,
    int DublettenHinweise);

/// <summary>Lesende Abfragen über viele Personen (Liste, Zählen, Export, Massenaktionen).</summary>
public interface IPersonenAbfrage
{
    /// <summary>Nächster Block nach <paramref name="nach"/> (oder der erste), sortiert nach Name.</summary>
    Task<IReadOnlyList<PersonZeile>> SeiteAsync(PersonenFilter filter, Seitenschluessel? nach, int anzahl, CancellationToken abbruch = default);

    Task<Filterzahlen> ZaehlenAsync(PersonenFilter filter, CancellationToken abbruch = default);

    /// <summary>Alle Treffer (für Export und Massenaktionen), in Listenreihenfolge.</summary>
    IAsyncEnumerable<PersonZeile> AlleAsync(PersonenFilter filter, CancellationToken abbruch = default);
}

/// <summary>Excel-Export einer Trefferliste.</summary>
public interface IPersonenExport
{
    Task<byte[]> ExcelAsync(PersonenFilter filter, CancellationToken abbruch = default);
}

public interface IOrganisationen
{
    Task<IReadOnlyList<Organisation>> AlleAsync(CancellationToken abbruch = default);

    Task<Organisation?> LadenAsync(Guid id, CancellationToken abbruch = default);
}
