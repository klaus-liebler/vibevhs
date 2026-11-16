using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

/// <summary>
/// Namen zur Laufzeit statt Namenskopien in Kursen und Terminen: Personen (Name mit Titel zum jeweiligen Tag, aus den Versionen),
/// Orte und Organisationen. Ein <see cref="Namen"/>-Stand wird für eine Seite bzw. einen Vorgang einmal gebündelt geladen (kein N+1).
/// </summary>
public sealed class Namen
{
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<Namensversion>> _personen;
    private readonly IReadOnlyDictionary<Guid, string> _orte;
    private readonly IReadOnlyDictionary<Guid, string> _organisationen;
    private readonly DateOnly _heute;

    public Namen(IReadOnlyDictionary<Guid, IReadOnlyList<Namensversion>> personen, IReadOnlyDictionary<Guid, string> orte,
        IReadOnlyDictionary<Guid, string> organisationen, DateOnly heute)
    {
        _personen = personen;
        _orte = orte;
        _organisationen = organisationen;
        _heute = heute;
    }

    public static Namen Leer { get; } = new(new Dictionary<Guid, IReadOnlyList<Namensversion>>(), new Dictionary<Guid, string>(),
        new Dictionary<Guid, string>(), DateOnly.MinValue);

    /// <summary>Name (mit Titel) der Person am Tag; vor dem ersten bzw. nach dem letzten bekannten Namen der nächstliegende.</summary>
    public string Person(Guid id, DateOnly tag)
    {
        if (!_personen.TryGetValue(id, out var versionen) || versionen.Count == 0)
        {
            return "?";
        }

        var v = versionen.FirstOrDefault(x => x.Von <= tag && (x.Bis is null || x.Bis > tag))
                ?? (tag < versionen[0].Von ? versionen[0] : versionen[^1]);
        return v.Name;
    }

    /// <summary>Name heute.</summary>
    public string Person(Guid id) => Person(id, _heute);

    /// <summary>Lehrkraft eines Termins: Name zum Termindatum (leer ohne Lehrkraft).</summary>
    public string Dozent(Veranstaltung v) => v.DozentId is { } d ? Person(d, v.Datum) : string.Empty;

    /// <summary>Lehrkraft im Team: Name zum letzten Termin, bei laufenden und künftigen Kursen der von heute.</summary>
    public string Team(Kurs kurs, KursDozent d)
    {
        var letzter = kurs.Veranstaltungen.Count == 0 ? _heute : DateOnly.FromDateTime(kurs.Veranstaltungen.Max(v => v.Beginn));
        return Person(d.PersonId, letzter < _heute ? letzter : _heute);
    }

    public string Ort(Guid id) => _orte.GetValueOrDefault(id, "?");

    /// <summary>Orte eines Termins, „Raum A + Online“.</summary>
    public string Orte(IEnumerable<VeranstaltungsOrt> orte) => string.Join(" + ", orte.Select(o => Ort(o.OrtId)));

    public string? Organisation(Guid? id) => id is { } i ? _organisationen.GetValueOrDefault(i, "?") : null;
}

/// <summary>Ein Abschnitt der Namensgeschichte einer Person (fachliche Gültigkeit, halboffen), Name mit Titel.</summary>
public sealed record Namensversion(DateOnly Von, DateOnly? Bis, string Name);

/// <summary>Lädt Namen gebündelt aus den Versionen bzw. Stammdaten.</summary>
public interface INamensquelle
{
    Task<Namen> LadenAsync(IEnumerable<Guid> personen, IEnumerable<Guid> orte, IEnumerable<Guid> organisationen, CancellationToken abbruch = default);
}

public static class NamensquelleErweiterungen
{
    /// <summary>Alle Namen, die Kurse mit ihren Terminen anzeigen (Team, Lehrkräfte der Termine, Orte, Auftraggeber).</summary>
    public static Task<Namen> FuerKurseAsync(this INamensquelle quelle, IEnumerable<Kurs> kurse, CancellationToken abbruch = default)
    {
        var liste = kurse.ToList();
        return quelle.LadenAsync(
            liste.SelectMany(k => k.Team.Select(t => t.PersonId).Concat(k.Veranstaltungen.Where(v => v.DozentId is not null).Select(v => v.DozentId!.Value))),
            liste.SelectMany(k => k.Veranstaltungen.SelectMany(v => v.Orte.Select(o => o.OrtId)).Concat(k.Serie?.OrtIds ?? [])),
            liste.Where(k => k.AuftraggeberId is not null).Select(k => k.AuftraggeberId!.Value),
            abbruch);
    }

    /// <summary>Namen für einzelne Termine (Lehrkraft, Orte).</summary>
    public static Task<Namen> FuerTermineAsync(this INamensquelle quelle, IEnumerable<Veranstaltung> termine, CancellationToken abbruch = default)
    {
        var liste = termine.ToList();
        return quelle.LadenAsync(liste.Where(v => v.DozentId is not null).Select(v => v.DozentId!.Value),
            liste.SelectMany(v => v.Orte.Select(o => o.OrtId)), [], abbruch);
    }

    public static Task<Namen> PersonenAsync(this INamensquelle quelle, IEnumerable<Guid> personen, CancellationToken abbruch = default)
        => quelle.LadenAsync(personen, [], [], abbruch);
}
