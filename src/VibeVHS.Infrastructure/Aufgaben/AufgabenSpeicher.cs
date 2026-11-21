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

    public async Task<IReadOnlyList<Abwesenheit>> AbwesenheitenAsync(CancellationToken abbruch = default)
        => Mit(await db.Set<Abwesenheit>().ToListAsync(abbruch), await NamenAsync(abbruch)).OrderBy(a => a.Von).ToList();

    public async Task<Abwesenheit?> AbwesenheitAsync(Guid id, CancellationToken abbruch = default)
        => Mit((await db.Set<Abwesenheit>().Where(a => a.Id == id).ToListAsync(abbruch)), await NamenAsync(abbruch)).FirstOrDefault();

    public void Hinzufuegen(Abwesenheit abwesenheit) => db.Set<Abwesenheit>().Add(abwesenheit);

    public void Hinzufuegen(Aufgabe aufgabe) => db.Aufgaben.Add(aufgabe);

    public async Task<Aufgabe?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => Mit(await db.Aufgaben.Where(a => a.Id == id).ToListAsync(abbruch), await NamenAsync(abbruch)).FirstOrDefault();

    public async Task<IReadOnlyList<Aufgabe>> ZumTypAsync(string typ, CancellationToken abbruch = default)
        => Mit(await db.Aufgaben.Where(a => a.Typ == typ).ToListAsync(abbruch), await NamenAsync(abbruch));

    public async Task<IReadOnlyList<Aufgabe>> OffeneAsync(bool verfolgen = false, CancellationToken abbruch = default)
    {
        var abfrage = db.Aufgaben.Where(a => a.Status == Aufgabenstatus.Offen || a.Status == Aufgabenstatus.InBearbeitung);
        return Mit(await (verfolgen ? abfrage : abfrage.AsNoTracking()).ToListAsync(abbruch), await NamenAsync(abbruch));
    }

    public async Task<IReadOnlyList<Aufgabe>> AbgeschlossenSeitAsync(DateTimeOffset seit, CancellationToken abbruch = default)
    {
        // DateTimeOffset liegt als Zahl vor; Vergleich deshalb im Speicher (wie bei den Nachrückangeboten).
        var abgeschlossen = await db.Aufgaben.AsNoTracking()
            .Where(a => a.Status == Aufgabenstatus.Erledigt || a.Status == Aufgabenstatus.Geschlossen)
            .ToListAsync(abbruch);
        return Mit(abgeschlossen.Where(a => a.ErledigtAm >= seit).ToList(), await NamenAsync(abbruch));
    }

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);

    /// <summary>Anmeldename → Name (Konten mit Person: Name der Person zum heutigen Tag; Funktionskonten: gespeicherter Name).</summary>
    private async Task<IReadOnlyDictionary<string, string>> NamenAsync(CancellationToken abbruch)
        => (await new StammdatenSpeicher(db).AlleMitInaktivenAsync(abbruch)).ToDictionary(k => k.Login, k => k.Name);

    private static List<Aufgabe> Mit(List<Aufgabe> aufgaben, IReadOnlyDictionary<string, string> namen)
    {
        aufgaben.ForEach(a => a.NamenSetzen(namen));
        return aufgaben;
    }

    private static List<Abwesenheit> Mit(List<Abwesenheit> liste, IReadOnlyDictionary<string, string> namen)
    {
        liste.ForEach(a => a.NamenSetzen(namen));
        return liste;
    }
}
