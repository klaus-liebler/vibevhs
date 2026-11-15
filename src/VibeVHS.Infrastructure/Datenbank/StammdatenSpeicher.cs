using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Stammdaten;

namespace VibeVHS.Infrastructure.Datenbank;

internal sealed class StammdatenSpeicher(VibeVhsDbContext db) : IStammdaten, IBenutzerkonten
{
    public Task<Einrichtung?> EinrichtungAsync(CancellationToken abbruch = default)
        => db.Einrichtungen.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Einrichtung.EinzigeId, abbruch);

    public async Task<IReadOnlyList<Fachbereich>> FachbereicheAsync(CancellationToken abbruch = default)
        => await db.Fachbereiche.AsNoTracking().OrderBy(f => f.Nr).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Benutzerkonto>> AlleAsync(CancellationToken abbruch = default)
        => await db.Benutzerkonten.AsNoTracking().Where(k => k.Aktiv).OrderBy(k => k.Name).ToListAsync(abbruch);

    public Task<Benutzerkonto?> FindenAsync(string login, CancellationToken abbruch = default)
    {
        var l = login.Trim().ToLowerInvariant();
        return db.Benutzerkonten.AsNoTracking().FirstOrDefaultAsync(k => k.Login == l && k.Aktiv, abbruch);
    }
}
