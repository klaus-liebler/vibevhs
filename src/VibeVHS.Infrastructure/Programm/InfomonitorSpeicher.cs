using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Programm;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Programm;

internal sealed class InfomonitorSpeicher(VibeVhsDbContext db) : IInfomonitorSpeicher
{
    public async Task<IReadOnlyList<Infomonitor>> AlleAsync(CancellationToken abbruch = default)
        => await db.Infomonitore.OrderBy(m => m.Name).ThenBy(m => m.Id).ToListAsync(abbruch);

    public Task<Infomonitor?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Infomonitore.FirstOrDefaultAsync(m => m.Id == id, abbruch);

    public Task<Infomonitor?> ZumPruefwertAsync(string pruefwert, CancellationToken abbruch = default)
        => db.Infomonitore.FirstOrDefaultAsync(m => m.Pruefwert == pruefwert || m.VorschauPruefwert == pruefwert, abbruch);

    public void Hinzufuegen(Infomonitor monitor) => db.Infomonitore.Add(monitor);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
