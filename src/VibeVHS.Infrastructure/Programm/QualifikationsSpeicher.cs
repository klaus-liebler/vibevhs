using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Programm;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Programm;

internal sealed class QualifikationsSpeicher(VibeVhsDbContext db) : IQualifikationsSpeicher
{
    public async Task<IReadOnlyDictionary<string, Merkmal>> KatalogAsync(CancellationToken abbruch = default)
        => await db.Merkmale.AsNoTracking().ToDictionaryAsync(m => m.Schluessel, abbruch);

    public async Task<IReadOnlyList<Qualifikationsausnahme>> AusnahmenAsync(Guid kursId, CancellationToken abbruch = default)
        => await db.Set<Qualifikationsausnahme>().AsNoTracking().Where(a => a.KursId == kursId).OrderBy(a => a.ErteiltAm).ToListAsync(abbruch);

    public Task<Kursart?> KursartLadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Kursarten.FirstOrDefaultAsync(k => k.Id == id, abbruch);

    public async Task<IReadOnlyList<Kurs>> KurseDerKursartAsync(Guid kursartId, CancellationToken abbruch = default)
        => await db.Kurse.Where(k => k.KursartId == kursartId && k.ArchiviertAm == null).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Kurs>> KurseMitTerminenZwischenAsync(DateTime von, DateTime bis, CancellationToken abbruch = default)
    {
        var ids = await db.Veranstaltungen.AsNoTracking().Where(v => v.Beginn >= von && v.Beginn < bis && v.DozentId != null)
            .Select(v => v.KursId).Distinct().ToListAsync(abbruch);
        return await db.Kurse.AsNoTracking().Include(k => k.Veranstaltungen)
            .Where(k => ids.Contains(k.Id) && k.ArchiviertAm == null && k.Status != Kursstatus.Abgesagt).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Person>> PersonenMitAblaufendenQualifikationenAsync(DateOnly bis, CancellationToken abbruch = default)
    {
        var ids = await db.Set<PersonQualifikation>().AsNoTracking()
            .Where(q => q.GueltigBis != null && q.GueltigBis <= bis.AddDays(1))
            .Select(q => EF.Property<Guid>(q, "PersonId")).Distinct().ToListAsync(abbruch);
        var mitFehlendemNachweis = await db.Set<PersonQualifikation>().AsNoTracking().Where(q => q.Nachweis == null)
            .Select(q => EF.Property<Guid>(q, "PersonId")).Distinct().ToListAsync(abbruch);
        var alle = ids.Union(mitFehlendemNachweis).ToList();
        return await db.Personen.AsNoTracking().Where(p => alle.Contains(p.Id)).ToListAsync(abbruch);
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
