using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Durchfuehrung;

internal sealed class UnterrichtsappSpeicher(VibeVhsDbContext db) : IUnterrichtsappSpeicher
{
    public async Task<IReadOnlyList<Unterrichtsapp>> AppsAsync(CancellationToken abbruch = default)
        => await db.Set<Unterrichtsapp>().OrderBy(a => a.Name).ThenBy(a => a.Id).ToListAsync(abbruch);

    public Task<Unterrichtsapp?> AppAsync(Guid id, CancellationToken abbruch = default)
        => db.Set<Unterrichtsapp>().FirstOrDefaultAsync(a => a.Id == id, abbruch);

    public async Task<IReadOnlyList<Appeinsatz>> EinsaetzeAsync(IReadOnlyCollection<Guid> kursIds, CancellationToken abbruch = default)
        => await db.Set<Appeinsatz>().AsNoTracking().Where(e => kursIds.Contains(e.KursId)).OrderBy(e => e.AngelegtAm).ThenBy(e => e.Id).ToListAsync(abbruch);

    public Task<Appeinsatz?> EinsatzAsync(Guid id, CancellationToken abbruch = default)
        => db.Set<Appeinsatz>().FirstOrDefaultAsync(e => e.Id == id, abbruch);

    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
