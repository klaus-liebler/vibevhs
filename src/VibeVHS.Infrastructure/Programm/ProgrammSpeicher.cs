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
    public async Task<IReadOnlyList<Semester>> SemesterAsync(CancellationToken abbruch = default)
        => await db.Semester.AsNoTracking().OrderBy(s => s.Beginn).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Kursart>> KursartenAsync(CancellationToken abbruch = default)
        => await db.Kursarten.AsNoTracking().OrderBy(k => k.Name).ToListAsync(abbruch);

    public async Task<IReadOnlyList<KursZeile>> KurseAsync(KursFilter filter, CancellationToken abbruch = default)
    {
        var q = db.Kurse.AsNoTracking().IgnoreAutoIncludes();
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

        foreach (var wort in Suchtext.Woerter(filter.Suche))
        {
            q = q.Where(k => k.Suchtext.Contains(wort));
        }

        var kurse = await q.OrderBy(k => k.Kursnummer).ToListAsync(abbruch);
        var ids = kurse.Select(k => k.Id).ToList();
        var termine = await db.Veranstaltungen.AsNoTracking()
            .Where(v => ids.Contains(v.KursId) && !v.Ausgefallen)
            .GroupBy(v => v.KursId)
            .Select(g => new { KursId = g.Key, Erster = g.Min(v => v.Beginn), Letzter = g.Max(v => v.Beginn), Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.KursId, abbruch);
        var ersteOrte = await db.Veranstaltungen.AsNoTracking()
            .Where(v => ids.Contains(v.KursId))
            .GroupBy(v => v.KursId)
            .Select(g => g.OrderBy(v => v.Beginn).First())
            .ToListAsync(abbruch);
        var orte = ersteOrte.ToDictionary(v => v.KursId, v => string.Join(" + ", v.Orte.Select(o => o.OrtName)));

        return kurse.Select(k =>
        {
            termine.TryGetValue(k.Id, out var t);
            return new KursZeile(k.Id, k.Kursnummer, k.Titel, k.Fachbereich, k.Status, k.Sichtbarkeit, k.Umfang, k.SemesterId,
                t?.Erster, t?.Letzter, t?.Anzahl ?? 0, orte.GetValueOrDefault(k.Id), k.Leitung?.Name, k.MaxTeilnehmende);
        }).ToList();
    }

    public Task<Kurs?> KursLadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Kurse.Include(k => k.Veranstaltungen).FirstOrDefaultAsync(k => k.Id == id, abbruch);

    public async Task<IReadOnlyList<Kurs>> KurseMitTerminenAsync(Guid semesterId, CancellationToken abbruch = default)
        => await db.Kurse.Include(k => k.Veranstaltungen).Where(k => k.SemesterId == semesterId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Kurs>> UnbefristeteKurseAsync(CancellationToken abbruch = default)
        => await db.Kurse.Include(k => k.Veranstaltungen).Where(k => k.Umfang == Kursumfang.Unbefristet && k.Status != Kursstatus.Abgesagt).ToListAsync(abbruch);

    public Task<bool> KursnummerVergebenAsync(string nummer, CancellationToken abbruch = default)
        => db.Kurse.AnyAsync(k => k.Kursnummer == nummer, abbruch);

    public async Task<Ortsverzeichnis> OrtsverzeichnisAsync(CancellationToken abbruch = default)
        => new(await db.Orte.AsNoTracking().ToListAsync(abbruch), await db.Merkmale.AsNoTracking().ToListAsync(abbruch));

    public Task<Ort?> OrtLadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Orte.FirstOrDefaultAsync(o => o.Id == id, abbruch);

    public async Task<Kalender> KalenderAsync(CancellationToken abbruch = default) => new(await FerienAsync(abbruch));

    public async Task<IReadOnlyList<Ferienzeitraum>> FerienAsync(CancellationToken abbruch = default)
        => await db.Ferien.AsNoTracking().OrderBy(f => f.Von).ToListAsync(abbruch);

    public async Task<IReadOnlyList<PlanTermin>> TermineAsync(DateTime von, DateTime bis, CancellationToken abbruch = default)
    {
        var liste = await db.Veranstaltungen.AsNoTracking()
            .Where(v => v.Beginn < bis && v.Ende > von)
            .Join(db.Kurse.IgnoreAutoIncludes().Where(k => k.Status != Kursstatus.Abgesagt), v => v.KursId, k => k.Id,
                (v, k) => new { v, k.Titel, k.Kursnummer, k.Fachbereich })
            .ToListAsync(abbruch);
        return liste.Select(x => new PlanTermin(Termin.Von(x.v, x.Titel), x.Kursnummer, x.v.Orte.Select(o => o.OrtName).ToList(), x.v.DozentName, x.Fachbereich))
            .OrderBy(t => t.Termin.Beginn)
            .ToList();
    }

    public Task<bool> AufgabeExistiertAsync(string schluessel, CancellationToken abbruch = default)
        => db.Aufgaben.AnyAsync(a => a.Schluessel == schluessel, abbruch);

    public async Task<IReadOnlyList<Aufgabe>> OffeneAufgabenAsync(string? gruppe, CancellationToken abbruch = default)
        => await db.Aufgaben.AsNoTracking().Where(a => a.ErledigtAm == null && (gruppe == null || a.Gruppe == gruppe))
            .OrderBy(a => a.Faellig).ToListAsync(abbruch);

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

        var kurse = db.Kurse.AsNoTracking().IgnoreAutoIncludes();
        var orte = db.Orte.AsNoTracking().IgnoreAutoIncludes();
        foreach (var w in woerter)
        {
            kurse = kurse.Where(k => k.Suchtext.Contains(w));
            orte = orte.Where(o => o.Suchtext.Contains(w));
        }

        foreach (var k in await kurse.OrderByDescending(k => k.Kursnummer).Take(jeTyp).Select(k => new { k.Id, k.Kursnummer, k.Titel }).ToListAsync(abbruch))
        {
            treffer.Add(new Suchtreffer(Treffertyp.Kurs, $"{k.Kursnummer} {k.Titel}", null, $"kurse/{k.Id}"));
        }

        foreach (var o in await orte.OrderBy(o => o.Name).Take(jeTyp).Select(o => new { o.Id, o.Name, o.Kurzname, o.Stadt, o.Art }).ToListAsync(abbruch))
        {
            treffer.Add(new Suchtreffer(Treffertyp.Ort, o.Kurzname is null ? o.Name : $"{o.Kurzname} {o.Name}",
                o.Stadt ?? o.Art.ToString(), $"orte?id={o.Id}"));
        }

        return treffer;
    }
}
