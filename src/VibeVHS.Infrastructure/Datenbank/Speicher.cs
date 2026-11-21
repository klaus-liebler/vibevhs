using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Datenbank;

internal sealed class PersonenSpeicher(VibeVhsDbContext db) : IPersonenSpeicher
{
    public void Hinzufuegen(Person person) => db.Personen.Add(person);

    public async Task<Person?> LadenAsync(Guid id, CancellationToken abbruch = default)
    {
        var p = await db.Personen.FirstOrDefaultAsync(p => p.Id == id, abbruch);
        if (p is not null)
        {
            await Bevollmaechtigtennamen.ErgaenzenAsync(db, [p], abbruch);
        }

        return p;
    }

    public async Task<IReadOnlyList<Person>> LadenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default)
    {
        var liste = ids.ToList();
        var personen = await db.Personen.Where(p => liste.Contains(p.Id)).ToListAsync(abbruch);
        await Bevollmaechtigtennamen.ErgaenzenAsync(db, personen, abbruch);
        return personen;
    }

    public void AusschlussHinzufuegen(DublettenAusschluss ausschluss) => db.DublettenAusschluesse.Add(ausschluss);

    public async Task<IReadOnlyList<Person>> AlleAsync(CancellationToken abbruch = default)
        => await db.Personen.AsNoTracking().OrderBy(p => p.AngelegtAm).ToListAsync(abbruch);

    public Task<int> AnzahlAsync(CancellationToken abbruch = default) => db.Personen.CountAsync(abbruch);

    public async Task<IReadOnlyDictionary<Guid, string>> AnzeigenamenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default)
    {
        var liste = ids.Distinct().ToList();
        return await db.AnzeigenamenAsync(liste, DateOnly.FromDateTime(DateTime.Today), abbruch);
    }

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}

internal sealed class NachrichtenSpeicher(VibeVhsDbContext db) : INachrichtenSpeicher
{
    public void Hinzufuegen(AusgehendeNachricht nachricht) => db.Postausgang.Add(nachricht);

    public Task<AusgehendeNachricht?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Postausgang.FirstOrDefaultAsync(n => n.Id == id, abbruch);

    public async Task<IReadOnlyList<AusgehendeNachricht>> WartendeAsync(int maximal, CancellationToken abbruch = default)
        => await db.Postausgang
            .Where(n => n.Status == NachrichtenStatus.Wartend)
            .OrderBy(n => n.ErstelltAm).ThenBy(n => n.Id)
            .Take(maximal)
            .ToListAsync(abbruch);

    public async Task<IReadOnlyList<AusgehendeNachricht>> NeuesteAsync(int anzahl, CancellationToken abbruch = default)
        => await db.Postausgang.AsNoTracking().OrderByDescending(n => n.ErstelltAm).ThenByDescending(n => n.Id).Take(anzahl).ToListAsync(abbruch);

    public Task<int> AnzahlAsync(NachrichtenStatus status, CancellationToken abbruch = default)
        => db.Postausgang.CountAsync(n => n.Status == status, abbruch);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}

internal sealed class AuditProtokoll(VibeVhsDbContext db, IAktuellerBenutzer benutzer, TimeProvider uhr) : IAuditProtokoll
{
    public async Task ProtokollierenAsync(string aktion, string beschreibung, string? objektTyp = null, Guid? objektId = null,
        bool erfolgreich = true, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), wer.Kennung, wer.Name, wer.Rolle?.Anzeigename(), aktion,
            objektTyp, objektId, beschreibung, erfolgreich));
    }

    public async Task<IReadOnlyList<AuditEintrag>> NeuesteAsync(int anzahl, CancellationToken abbruch = default)
        => await db.Protokoll.AsNoTracking().OrderByDescending(e => e.Zeitpunkt).ThenByDescending(e => e.Id).Take(anzahl).ToListAsync(abbruch);

    /// <summary>Blockweise rückwärts lesen, bis der Zeitpunkt unterschritten ist (Zeitpunkte sind binär gespeichert, der Vergleich läuft im Speicher).</summary>
    public async Task<IReadOnlyList<AuditEintrag>> SeitAsync(DateTimeOffset seit, string? benutzerKennung = null, CancellationToken abbruch = default)
    {
        const int Block = 1000;
        var basis = db.Protokoll.AsNoTracking().Where(e => benutzerKennung == null || e.BenutzerKennung == benutzerKennung)
            .OrderByDescending(e => e.Zeitpunkt).ThenByDescending(e => e.Id);
        var ergebnis = new List<AuditEintrag>();
        for (var start = 0; ; start += Block)
        {
            var block = await basis.Skip(start).Take(Block).ToListAsync(abbruch);
            ergebnis.AddRange(block.Where(e => e.Zeitpunkt >= seit));
            if (block.Count < Block || block[^1].Zeitpunkt < seit)
            {
                return ergebnis;
            }
        }
    }
}
