using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Aufgaben;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Aufgaben;

internal sealed class AufgabenSpeicher(VibeVhsDbContext db) : IAufgabenSpeicher
{
    public async Task<IReadOnlyList<Aufgabentyp>> TypenAsync(CancellationToken abbruch = default)
        => await db.Set<Aufgabentyp>().ToListAsync(abbruch);

    public void Hinzufuegen(Aufgabentyp typ) => db.Set<Aufgabentyp>().Add(typ);

    public void Hinzufuegen(Aufgabe aufgabe) => db.Aufgaben.Add(aufgabe);

    public Task<Aufgabe?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Aufgaben.FirstOrDefaultAsync(a => a.Id == id, abbruch);

    public async Task<IReadOnlyList<Aufgabe>> ZumTypAsync(string typ, CancellationToken abbruch = default)
        => await db.Aufgaben.Where(a => a.Typ == typ).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Aufgabe>> OffeneAsync(bool verfolgen = false, CancellationToken abbruch = default)
    {
        var abfrage = db.Aufgaben.Where(a => a.Status == Aufgabenstatus.Offen || a.Status == Aufgabenstatus.InBearbeitung);
        return await (verfolgen ? abfrage : abfrage.AsNoTracking()).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Aufgabe>> AbgeschlossenSeitAsync(DateTimeOffset seit, CancellationToken abbruch = default)
    {
        // DateTimeOffset liegt als Zahl vor; Vergleich deshalb im Speicher (wie bei den Nachrückangeboten).
        var abgeschlossen = await db.Aufgaben.AsNoTracking()
            .Where(a => a.Status == Aufgabenstatus.Erledigt || a.Status == Aufgabenstatus.Geschlossen)
            .ToListAsync(abbruch);
        return abgeschlossen.Where(a => a.ErledigtAm >= seit).ToList();
    }

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
