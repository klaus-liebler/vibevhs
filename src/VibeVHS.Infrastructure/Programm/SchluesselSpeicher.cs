using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Programm;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Programm;

internal sealed class SchluesselSpeicher(VibeVhsDbContext db) : ISchluesselSpeicher
{
    public async Task<IReadOnlyList<Schluessel>> AlleAsync(CancellationToken abbruch = default)
        => await db.Set<Schluessel>().OrderBy(s => s.Nummer).ThenBy(s => s.Id).ToListAsync(abbruch);

    public Task<Schluessel?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Set<Schluessel>().FirstOrDefaultAsync(s => s.Id == id, abbruch);

    public Task<bool> NummerVergebenAsync(string nummer, CancellationToken abbruch = default)
        => db.Set<Schluessel>().AnyAsync(s => s.Nummer == nummer, abbruch);

    public async Task<IReadOnlyList<Schluesselausgabe>> AusgabenAsync(bool nurOffene, CancellationToken abbruch = default)
        => await db.Set<Schluesselausgabe>().AsNoTracking().Where(a => !nurOffene || a.ZurueckAm == null)
            .OrderByDescending(a => a.AusgegebenAm).ThenBy(a => a.Id).ToListAsync(abbruch);

    public Task<Schluesselausgabe?> AusgabeAsync(Guid id, CancellationToken abbruch = default)
        => db.Set<Schluesselausgabe>().FirstOrDefaultAsync(a => a.Id == id, abbruch);

    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
