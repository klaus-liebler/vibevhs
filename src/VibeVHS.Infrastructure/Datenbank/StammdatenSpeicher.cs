using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Stammdaten;

namespace VibeVHS.Infrastructure.Datenbank;

internal sealed class StammdatenSpeicher(VibeVhsDbContext db) : IStammdaten, IBenutzerkonten, IEinrichtungAendern, IBenutzerkontenPflege
{
    public async Task<IReadOnlyList<Benutzerkonto>> AlleMitInaktivenAsync(CancellationToken abbruch = default)
        => await db.Benutzerkonten.OrderBy(k => k.Name).ThenBy(k => k.Id).ToListAsync(abbruch);

    public Task<Benutzerkonto?> ZumAendernAsync(Guid id, CancellationToken abbruch = default) => db.Benutzerkonten.FirstOrDefaultAsync(k => k.Id == id, abbruch);

    public Task<bool> LoginVergebenAsync(string login, CancellationToken abbruch = default) => db.Benutzerkonten.AnyAsync(k => k.Login == login, abbruch);

    public void Hinzufuegen(Benutzerkonto konto) => db.Benutzerkonten.Add(konto);

    public Task<Einrichtung?> EinrichtungZumAendernAsync(CancellationToken abbruch = default)
        => db.Einrichtungen.FirstOrDefaultAsync(e => e.Id == Einrichtung.EinzigeId, abbruch);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);

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
