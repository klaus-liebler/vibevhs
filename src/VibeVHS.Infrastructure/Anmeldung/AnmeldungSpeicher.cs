using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Vorlagen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Anmeldung;

internal sealed class AnmeldungSpeicher(VibeVhsDbContext db) : IAnmeldungSpeicher, IVorlagenSpeicher
{
    public async Task<IReadOnlyList<Belegung>> BelegungenDesKursesAsync(Guid kursId, CancellationToken abbruch = default)
        => await db.Belegungen.Where(b => b.KursId == kursId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<PersonBelegung>> BelegungenDerPersonAsync(Guid personId, CancellationToken abbruch = default)
    {
        var belegungen = await db.Belegungen.AsNoTracking().Where(b => b.PersonId == personId).OrderByDescending(b => b.GebuchtAm).ToListAsync(abbruch);
        var ids = belegungen.Select(b => b.KursId).Distinct().ToList();
        var kurse = await db.Kurse.IgnoreAutoIncludes().AsNoTracking().Where(k => ids.Contains(k.Id))
            .Select(k => new { k.Id, k.Titel, k.Status }).ToDictionaryAsync(k => k.Id, abbruch);
        var beginn = await db.Veranstaltungen.AsNoTracking().Where(v => ids.Contains(v.KursId) && !v.Ausgefallen)
            .GroupBy(v => v.KursId).Select(g => new { g.Key, Beginn = g.Min(v => v.Beginn) }).ToDictionaryAsync(x => x.Key, x => x.Beginn, abbruch);
        return belegungen.Select(b => new PersonBelegung(b, kurse.GetValueOrDefault(b.KursId)?.Titel ?? "(Kurs gelöscht)",
            beginn.TryGetValue(b.KursId, out var d) ? d : null, kurse.GetValueOrDefault(b.KursId)?.Status == Kursstatus.Abgesagt)).ToList();
    }

    public async Task<IReadOnlyList<Belegung>> BelegungenZuPersonAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Belegungen.Where(b => b.PersonId == personId).ToListAsync(abbruch);

    public Task<Belegung?> BelegungLadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Belegungen.FirstOrDefaultAsync(b => b.Id == id, abbruch);

    public async Task<IReadOnlyDictionary<Guid, (int Belegt, int Wartend)>> BelegungszahlenAsync(IEnumerable<Guid> kursIds, CancellationToken abbruch = default)
    {
        var ids = kursIds.ToList();
        var zeilen = await db.Belegungen.IgnoreAutoIncludes().AsNoTracking()
            .Where(b => ids.Contains(b.KursId) && (b.Status == Belegungsstatus.Angemeldet || b.Status == Belegungsstatus.Nachrueckangebot || b.Status == Belegungsstatus.Warteliste))
            .GroupBy(b => new { b.KursId, b.Status })
            .Select(g => new { g.Key.KursId, g.Key.Status, Anzahl = g.Count() })
            .ToListAsync(abbruch);
        return zeilen.GroupBy(z => z.KursId).ToDictionary(g => g.Key,
            g => (g.Where(z => z.Status != Belegungsstatus.Warteliste).Sum(z => z.Anzahl), g.Where(z => z.Status == Belegungsstatus.Warteliste).Sum(z => z.Anzahl)));
    }

    public async Task<IReadOnlyDictionary<Guid, Belegungsstatus>> OffeneBelegungenAsync(Guid personId, CancellationToken abbruch = default)
        => (await db.Belegungen.IgnoreAutoIncludes().AsNoTracking()
                .Where(b => b.PersonId == personId && (b.Status == Belegungsstatus.Angemeldet || b.Status == Belegungsstatus.Nachrueckangebot || b.Status == Belegungsstatus.Warteliste))
                .Select(b => new { b.KursId, b.Status }).ToListAsync(abbruch))
            .GroupBy(b => b.KursId).ToDictionary(g => g.Key, g => g.Min(b => b.Status));

    public async Task<IReadOnlyList<Kurs>> KurseZurAuswahlAsync(IEnumerable<Guid> semesterIds, CancellationToken abbruch = default)
    {
        var ids = semesterIds.ToList();
        return await db.Kurse.AsNoTracking().Include(k => k.Veranstaltungen)
            .Where(k => ids.Contains(k.SemesterId) && k.ArchiviertAm == null).OrderBy(k => k.Kursnummer).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Ermaessigung>> ErmaessigungenAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Ermaessigungen.AsNoTracking().Where(e => e.PersonId == personId).OrderByDescending(e => e.GueltigBis).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Belegung>> AbgelaufeneAngeboteAsync(DateTimeOffset jetzt, CancellationToken abbruch = default)
    {
        // DateTimeOffset liegt als sortierbare Zahl vor; Vergleich deshalb im Speicher (wenige offene Angebote).
        var offen = await db.Belegungen.Where(b => b.Status == Belegungsstatus.Nachrueckangebot).ToListAsync(abbruch);
        return offen.Where(b => b.AngebotBis < jetzt).ToList();
    }

    public async Task<IReadOnlyList<Belegung>> KontingenteMitAblaufVorAsync(DateOnly tag, CancellationToken abbruch = default)
        => await db.Belegungen.Where(b => b.Belegungsart == Belegungsart.Kontingent && b.KontingentGueltigBis != null && b.KontingentGueltigBis < tag
                                          && b.Status == Belegungsstatus.Angemeldet).ToListAsync(abbruch);

    public void Hinzufuegen(object eintrag)
    {
        if (db.Entry(eintrag).State == EntityState.Detached)
        {
            db.Add(eintrag);
        }
    }

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);

    // ---------- Vorlagen ----------

    public async Task<IReadOnlyList<Textvorlage>> VorlagenAsync(CancellationToken abbruch = default)
        => await db.Vorlagen.OrderBy(v => v.Bezeichnung).ToListAsync(abbruch);

    public Task<Textvorlage?> VorlageAsync(string schluessel, CancellationToken abbruch = default)
        => db.Vorlagen.FirstOrDefaultAsync(v => v.Schluessel == schluessel, abbruch);

    public void Hinzufuegen(Textvorlage vorlage) => db.Vorlagen.Add(vorlage);
}
