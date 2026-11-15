using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Infrastructure.Datenbank;

internal sealed class PersonenSpeicher(VibeVhsDbContext db) : IPersonenSpeicher
{
    public void Hinzufuegen(Person person) => db.Personen.Add(person);

    public Task<Person?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Personen.FirstOrDefaultAsync(p => p.Id == id, abbruch);

    public async Task<IReadOnlyList<Person>> AlleAsync(CancellationToken abbruch = default)
        => await db.Personen.AsNoTracking().OrderBy(p => p.AngelegtAm).ToListAsync(abbruch);

    public Task<int> AnzahlAsync(CancellationToken abbruch = default) => db.Personen.CountAsync(abbruch);

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
            .OrderBy(n => n.ErstelltAm)
            .Take(maximal)
            .ToListAsync(abbruch);

    public async Task<IReadOnlyList<AusgehendeNachricht>> NeuesteAsync(int anzahl, CancellationToken abbruch = default)
        => await db.Postausgang.AsNoTracking().OrderByDescending(n => n.ErstelltAm).Take(anzahl).ToListAsync(abbruch);

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
        => await db.Protokoll.AsNoTracking().OrderByDescending(e => e.Zeitpunkt).Take(anzahl).ToListAsync(abbruch);
}
