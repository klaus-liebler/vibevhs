using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

/// <summary>Filter der Personenliste. Zusammengeführte Dubletten sind ausgeblendet, außer man filtert danach.</summary>
public sealed record PersonenFilter(
    string? Suche = null,
    Personenrolle? Rolle = null,
    Personenstatus? Status = null,
    int? Fachbereich = null,
    bool NurDublettenHinweise = false,
    Domain.Datenschutz.Datenschutzstatus? Datenschutz = null)
{
    public bool IstLeer => string.IsNullOrWhiteSpace(Suche) && Rolle is null && Status is null && Fachbereich is null && !NurDublettenHinweise && Datenschutz is null;
}

/// <summary>Eine Zeile der Personenliste (Stand heute, aus den Versionen berechnet) mit ihrer Position für das Weiterblättern.</summary>
public sealed record PersonZeile(
    Guid Id,
    string Anzeigename,
    Seitenschluessel Seite,
    DateOnly? Geburtsdatum,
    string? Wohnort,
    string? EMail,
    string Sprache,
    Personenrolle Rollen,
    Personenstatus Status,
    IReadOnlyList<int> Fachbereiche);

/// <summary>
/// Position in der sortierten Liste für die Keyset-Pagination (statt Seitennummer und OFFSET): Nachname und Vorname (verglichen nach
/// DIN 5007-2, Collation DIN5007_2) und die Id als Hex der 16 Bytes (eindeutiger Gleichstandsbrecher).
/// </summary>
public sealed record Seitenschluessel(string Nachname, string Vorname, string IdHex)
{
    public static Seitenschluessel Von(PersonZeile zeile) => zeile.Seite;

    /// <summary>Reihenfolge wie in der Datenbank (für Tests und Sortierung im Speicher).</summary>
    public static int Vergleichen(Seitenschluessel a, Seitenschluessel b)
    {
        var n = Domain.Gemeinsam.Din5007.Vergleichen(a.Nachname, b.Nachname);
        if (n != 0)
        {
            return n;
        }

        var v = Domain.Gemeinsam.Din5007.Vergleichen(a.Vorname, b.Vorname);
        return v != 0 ? v : string.CompareOrdinal(a.IdHex, b.IdHex);
    }
}

/// <summary>Trefferzahlen für die Filter-Chips: je Wert, mit allen übrigen gesetzten Filtern.</summary>
public sealed record Filterzahlen(
    int Gesamt,
    IReadOnlyDictionary<Personenrolle, int> Rollen,
    IReadOnlyDictionary<Personenstatus, int> Status,
    IReadOnlyDictionary<int, int> Fachbereiche,
    int DublettenHinweise,
    IReadOnlyDictionary<Domain.Datenschutz.Datenschutzstatus, int>? Datenschutz = null);

/// <summary>Lesende Abfragen über viele Personen (Liste, Zählen, Export, Massenaktionen).</summary>
public interface IPersonenAbfrage
{
    /// <summary>Nächster Block nach <paramref name="nach"/> (oder der erste), sortiert nach Name.</summary>
    Task<IReadOnlyList<PersonZeile>> SeiteAsync(PersonenFilter filter, Seitenschluessel? nach, int anzahl, CancellationToken abbruch = default);

    Task<Filterzahlen> ZaehlenAsync(PersonenFilter filter, CancellationToken abbruch = default);

    /// <summary>Alle Treffer (für Export und Massenaktionen), in Listenreihenfolge.</summary>
    IAsyncEnumerable<PersonZeile> AlleAsync(PersonenFilter filter, CancellationToken abbruch = default);

    /// <summary>Rundschreiben: Treffer, davon mit E-Mail-Adresse, davon mit Einwilligung für den Zweck – vollständig in SQL gezählt.</summary>
    Task<RundschreibenVorschau> RundschreibenZaehlenAsync(PersonenFilter filter, Kontaktzweck zweck, CancellationToken abbruch = default);

    /// <summary>Rundschreiben: nur die Empfänger (E-Mail und ggf. Einwilligung), vollständig geladen, paketweise.</summary>
    IAsyncEnumerable<Person> RundschreibenEmpfaengerAsync(PersonenFilter filter, Kontaktzweck zweck, CancellationToken abbruch = default);
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
