using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Programm;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Programm;

internal sealed class ProgrammSpeicher(VibeVhsDbContext db) : IProgrammSpeicher
{
    public Task<Namen> LadenAsync(IEnumerable<Guid> personen, IEnumerable<Guid> orte, IEnumerable<Guid> organisationen, CancellationToken abbruch = default)
        => new Personen.Namensquelle(db).LadenAsync(personen, orte, organisationen, abbruch);

    public async Task<IReadOnlyList<Semester>> SemesterAsync(CancellationToken abbruch = default)
        => await db.Semester.AsNoTracking().OrderBy(s => s.Beginn).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Kursart>> KursartenAsync(CancellationToken abbruch = default)
        => await db.Kursarten.AsNoTracking().OrderBy(k => k.Name).ToListAsync(abbruch);

    public async Task<IReadOnlyList<KursZeile>> KurseAsync(KursFilter filter, CancellationToken abbruch = default)
    {
        var q = db.Kurse.AsNoTracking().IgnoreAutoIncludes().Where(k => (k.ArchiviertAm != null) == filter.Archivierte);
        if (filter.SemesterId is { } s)
        {
            q = q.Where(k => k.SemesterId == s);
        }

        if (filter.Fachbereich is { } fb)
        {
            q = q.Where(k => k.Fachbereich == fb);
        }

        if (filter.Status is { } st)
        {
            q = q.Where(k => k.Status == st);
        }

        if (Suchtext.Woerter(filter.Suche) is { Count: > 0 } woerter)
        {
            var treffer = await SuchtrefferAsync(q, woerter, abbruch);
            q = q.Where(k => treffer.Contains(k.Id));
        }

        // Projektion: nur die Spalten der Liste (keine vollständigen Kurse mit Richtlinie, Serie, Team …).
        var kurse = await q.OrderBy(k => k.Kursnummer).ThenBy(k => k.Id)
            .Select(k => new
            {
                k.Id, k.Kursnummer, k.Titel, k.Fachbereich, k.Status, k.Sichtbarkeit, k.Umfang, k.SemesterId, k.MaxTeilnehmende, k.ArchiviertAm,
                Leitung = k.Team.OrderByDescending(t => t.Leitung).Select(t => (Guid?)t.PersonId).FirstOrDefault(),
            })
            .ToListAsync(abbruch);
        // Termine und Orte über die Kursabfrage selbst eingeschränkt (Unterabfrage/Join statt langer Id-Listen), nur schmale Spalten.
        var kursIds = q.Select(k => k.Id);
        var termine = await db.Veranstaltungen.AsNoTracking()
            .Where(v => kursIds.Contains(v.KursId) && !v.Ausgefallen)
            .GroupBy(v => v.KursId)
            .Select(g => new { KursId = g.Key, Erster = g.Min(v => v.Beginn), Letzter = g.Max(v => v.Beginn), Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.KursId, abbruch);
        // Orte des ersten Termins je Kurs: alle (Termin, Ort)-Paare als Join, Auswahl des ersten Termins im Speicher.
        var paare = await db.Veranstaltungen.AsNoTracking().Where(v => kursIds.Contains(v.KursId))
            .SelectMany(v => v.Orte, (v, o) => new { v.KursId, v.Id, v.Beginn, o.OrtId })
            .ToListAsync(abbruch);
        var ersteOrte = paare.GroupBy(x => x.KursId).ToDictionary(g => g.Key, g =>
        {
            var erster = g.OrderBy(x => x.Beginn).ThenBy(x => x.Id).First().Id;
            return g.Where(x => x.Id == erster).Select(x => x.OrtId).ToList();
        });
        var namen = await LadenAsync(kurse.Where(k => k.Leitung is not null).Select(k => k.Leitung!.Value), ersteOrte.Values.SelectMany(o => o), [], abbruch);
        var orte = ersteOrte.ToDictionary(x => x.Key, x => string.Join(" + ", x.Value.Select(namen.Ort)));

        return kurse.Select(k =>
        {
            termine.TryGetValue(k.Id, out var t);
            return new KursZeile(k.Id, k.Kursnummer, k.Titel, k.Fachbereich, k.Status, k.Sichtbarkeit, k.Umfang, k.SemesterId,
                t?.Erster, t?.Letzter, t?.Anzahl ?? 0, orte.GetValueOrDefault(k.Id), k.Leitung is { } l ? namen.Person(l) : null, k.MaxTeilnehmende, k.ArchiviertAm is not null);
        }).ToList();
    }

    /// <summary>
    /// Volltextsuche über Kursnummer und Titel ohne gespeicherten Suchtext: die (kurzen) Spalten werden geladen und in C# normalisiert
    /// (gleiche Normalisierung wie bei Personen: Umlaute, Akzente, Groß-/Kleinschreibung).
    /// </summary>
    internal static async Task<List<Guid>> SuchtrefferAsync(IQueryable<Kurs> kurse, IReadOnlyCollection<string> woerter, CancellationToken abbruch)
        => (await kurse.Select(k => new { k.Id, k.Kursnummer, k.Titel }).ToListAsync(abbruch))
            .Where(k => Suchtext.Normalisieren($"{k.Kursnummer} {k.Titel}") is var text && woerter.All(w => text.Contains(w, StringComparison.Ordinal)))
            .Select(k => k.Id).ToList();

    /// <summary>Wie <see cref="SuchtrefferAsync(IQueryable{Kurs}, IReadOnlyCollection{string}, CancellationToken)"/> für Orte (Name, Kurzname, Straße, Stadt).</summary>
    internal static async Task<List<Guid>> SuchtrefferAsync(IQueryable<Ort> orte, IReadOnlyCollection<string> woerter, CancellationToken abbruch)
        => (await orte.Select(o => new { o.Id, o.Name, o.Kurzname, o.Strasse, o.Stadt }).ToListAsync(abbruch))
            .Where(o => Suchtext.Normalisieren($"{o.Name} {o.Kurzname} {o.Strasse} {o.Stadt}") is var text && woerter.All(w => text.Contains(w, StringComparison.Ordinal)))
            .Select(o => o.Id).ToList();

    public Task<Kurs?> KursLadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Kurse.Include(k => k.Veranstaltungen).FirstOrDefaultAsync(k => k.Id == id, abbruch);

    public async Task<IReadOnlyList<Kurs>> KurseMitTerminenAsync(Guid semesterId, CancellationToken abbruch = default)
        => await db.Kurse.Include(k => k.Veranstaltungen).Where(k => k.SemesterId == semesterId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Kurs>> UnbefristeteKurseAsync(CancellationToken abbruch = default)
        => await db.Kurse.Include(k => k.Veranstaltungen).Where(k => k.Umfang == Kursumfang.Unbefristet && k.Status != Kursstatus.Abgesagt).ToListAsync(abbruch);

    public Task<bool> KursnummerVergebenAsync(string nummer, CancellationToken abbruch = default)
        => db.Kurse.AnyAsync(k => k.Kursnummer == nummer, abbruch);

    public async Task<Ortsverzeichnis> OrtsverzeichnisAsync(CancellationToken abbruch = default)
        => new(await db.Orte.AsNoTracking().ToListAsync(abbruch), await db.Merkmale.AsNoTracking().ToListAsync(abbruch),
            await db.Kursarten.AsNoTracking().ToListAsync(abbruch));

    public Task<Ort?> OrtLadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Orte.FirstOrDefaultAsync(o => o.Id == id, abbruch);

    public async Task<Kalender> KalenderAsync(CancellationToken abbruch = default) => new(await FerienAsync(abbruch));

    public async Task<IReadOnlyList<Ferienzeitraum>> FerienAsync(CancellationToken abbruch = default)
        => await db.Ferien.AsNoTracking().OrderBy(f => f.Von).ToListAsync(abbruch);

    public async Task<IReadOnlyList<PlanTermin>> TermineAsync(DateTime von, DateTime bis, CancellationToken abbruch = default)
    {
        // Projektion statt vollständiger Termin-Objekte (deutlich schneller): Termine mit Kursdaten, Orte je Termin in einer zweiten Abfrage.
        var liste = await db.Veranstaltungen.AsNoTracking()
            .Where(v => v.Beginn < bis && v.Ende > von)
            .Join(db.Kurse.IgnoreAutoIncludes().Where(k => k.Status != Kursstatus.Abgesagt), v => v.KursId, k => k.Id,
                (v, k) => new { v.Id, v.KursId, v.Beginn, v.Ende, v.DozentId, v.Ausgefallen, k.Titel, k.Kursnummer, k.Fachbereich })
            .ToListAsync(abbruch);
        // Orte der Termine als Join mit derselben Einschränkung (statt einer langen Id-Liste).
        var orte = (await db.Veranstaltungen.AsNoTracking().Where(v => v.Beginn < bis && v.Ende > von)
                .SelectMany(v => v.Orte, (v, o) => new { VeranstaltungId = v.Id, o.OrtId }).ToListAsync(abbruch))
            .ToLookup(o => o.VeranstaltungId, o => o.OrtId);
        var namen = await LadenAsync(liste.Where(x => x.DozentId is not null).Select(x => x.DozentId!.Value), orte.SelectMany(g => g), [], abbruch);
        return liste.Select(x =>
            {
                var termin = new Termin(x.Id, x.KursId, x.Titel, x.Beginn, x.Ende, orte[x.Id].ToList(), x.DozentId, x.Ausgefallen);
                return new PlanTermin(termin, x.Kursnummer, termin.OrtIds.Select(namen.Ort).ToList(),
                    x.DozentId is { } d ? namen.Person(d, DateOnly.FromDateTime(x.Beginn)) : string.Empty, x.Fachbereich);
            })
            .OrderBy(t => t.Termin.Beginn)
            .ToList();
    }

    public async Task<IReadOnlyList<Kurs>> KurseMitBeginnZwischenAsync(DateTime von, DateTime bis, CancellationToken abbruch = default)
    {
        var ids = await db.Veranstaltungen.AsNoTracking().Where(v => v.Beginn >= von && v.Beginn < bis)
            .Select(v => v.KursId).Distinct().ToListAsync(abbruch);
        var kurse = await db.Kurse.AsNoTracking().Include(k => k.Veranstaltungen)
            .Where(k => ids.Contains(k.Id) && k.ArchiviertAm == null).ToListAsync(abbruch);

        // Abgesagte Kurse haben keine stattfindenden Termine mehr: dann zählt der erste geplante.
        return kurse.Where(k => (k.ErsterTermin ?? k.Veranstaltungen.Select(v => (DateTime?)v.Beginn).Min()) is { } b && b >= von && b < bis).ToList();
    }

    public void Hinzufuegen(object eintrag)
    {
        if (db.Entry(eintrag).State == EntityState.Detached)
        {
            db.Add(eintrag);
        }
    }

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}

/// <summary>Globale Suche: Personen (Volltext), Kurse (Nummer/Titel), Orte (Name/Adresse).</summary>
internal sealed class GlobaleSuche(VibeVhsDbContext db, IPersonenAbfrage personen) : IGlobaleSuche
{
    public async Task<IReadOnlyList<Suchtreffer>> SuchenAsync(string text, int jeTyp = 5, CancellationToken abbruch = default)
    {
        var woerter = Suchtext.Woerter(text);
        if (woerter.Count == 0)
        {
            return [];
        }

        var treffer = new List<Suchtreffer>();
        foreach (var p in await personen.SeiteAsync(new PersonenFilter(text), null, jeTyp, abbruch))
        {
            treffer.Add(new Suchtreffer(Treffertyp.Person, p.Anzeigename,
                string.Join(" · ", new[] { p.Geburtsdatum?.ToString("dd.MM.yyyy"), p.Wohnort }.Where(x => x is not null)), $"personen/{p.Id}"));
        }

        var kursTreffer = await ProgrammSpeicher.SuchtrefferAsync(db.Kurse.AsNoTracking().IgnoreAutoIncludes(), woerter, abbruch);
        var ortTreffer = await ProgrammSpeicher.SuchtrefferAsync(db.Orte.AsNoTracking().IgnoreAutoIncludes(), woerter, abbruch);
        var kurse = db.Kurse.AsNoTracking().IgnoreAutoIncludes().Where(k => kursTreffer.Contains(k.Id));
        var orte = db.Orte.AsNoTracking().IgnoreAutoIncludes().Where(o => ortTreffer.Contains(o.Id));

        foreach (var k in await kurse.OrderByDescending(k => k.Kursnummer).ThenBy(k => k.Id).Take(jeTyp).Select(k => new { k.Id, k.Kursnummer, k.Titel }).ToListAsync(abbruch))
        {
            treffer.Add(new Suchtreffer(Treffertyp.Kurs, $"{k.Kursnummer} {k.Titel}", null, $"kurse/{k.Id}"));
        }

        foreach (var o in await orte.OrderBy(o => o.Name).ThenBy(o => o.Id).Take(jeTyp).Select(o => new { o.Id, o.Name, o.Kurzname, o.Stadt, o.Art }).ToListAsync(abbruch))
        {
            treffer.Add(new Suchtreffer(Treffertyp.Ort, o.Kurzname is null ? o.Name : $"{o.Kurzname} {o.Name}",
                o.Stadt ?? o.Art.ToString(), $"orte?id={o.Id}"));
        }

        return treffer;
    }
}
