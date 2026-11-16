using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Personen;

internal sealed class ZwischenstandSpeicher(VibeVhsDbContext db, TimeProvider uhr) : IZwischenstandSpeicher
{
    public async Task<IReadOnlyList<Guid>> PersonenMitGrenzeAsync(DateOnly nach, DateOnly bis, CancellationToken abbruch = default)
    {
        var ids = new HashSet<Guid>();
        ids.UnionWith(await GrenzenAsync<PersonName>(nach, bis, abbruch));
        ids.UnionWith(await GrenzenAsync<PersonMerkmal>(nach, bis, abbruch));
        ids.UnionWith(await GrenzenAsync<Postadresse>(nach, bis, abbruch));
        ids.UnionWith(await GrenzenAsync<AdressKennzeichen>(nach, bis, abbruch));
        ids.UnionWith(await GrenzenAsync<EMailAdresse>(nach, bis, abbruch));
        return ids.Order().ToList();
    }

    private Task<List<Guid>> GrenzenAsync<T>(DateOnly nach, DateOnly bis, CancellationToken abbruch)
        where T : BitemporaleVersion<T>
        => db.Set<T>().AsNoTracking()
            .Where(v => (v.GueltigVon > nach && v.GueltigVon <= bis) || (v.GueltigBis != null && v.GueltigBis > nach && v.GueltigBis <= bis))
            .Select(v => EF.Property<Guid>(v, "PersonId")).Distinct().ToListAsync(abbruch);

    public async Task<IReadOnlyList<Person>> LadenAsync(IReadOnlyCollection<Guid> ids, CancellationToken abbruch = default)
        => await db.Personen.AsSplitQuery().Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).ToListAsync(abbruch);

    public async Task<DateOnly?> LetzterLaufAsync(CancellationToken abbruch = default)
    {
        // Zeitpunkt ist binär gespeichert: neuesten Eintrag im Speicher bestimmen (ein Eintrag je Tag).
        var zeiten = await db.Protokoll.AsNoTracking().Where(e => e.Aktion == Zwischenstaende.Aktion).Select(e => e.Zeitpunkt).ToListAsync(abbruch);
        return zeiten.Count == 0 ? null : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(zeiten.Max(), uhr.LocalTimeZone).Date);
    }

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);

    public async Task<int> NamenskopienAbgleichenAsync(DateTimeOffset jetzt, CancellationToken abbruch = default)
    {
        var geaendert = 0;
        var kurse = await db.Kurse.Include(k => k.Veranstaltungen).Where(k => k.ArchiviertAm == null).OrderBy(k => k.Id).ToListAsync(abbruch);
        var personIds = kurse.SelectMany(k => k.Team.Select(d => d.PersonId)).Distinct().ToList();
        var konten = await db.Benutzerkonten.Where(k => k.PersonId != null).OrderBy(k => k.Id).ToListAsync(abbruch);
        personIds.AddRange(konten.Select(k => k.PersonId!.Value));
        personIds.AddRange(kurse.SelectMany(k => k.Veranstaltungen).Where(v => v.DozentId is not null).Select(v => v.DozentId!.Value));
        var ids = personIds.Distinct().ToList();
        var personen = await db.Personen.AsNoTracking().AsSplitQuery()
            .Where(p => ids.Contains(p.Id) && p.Datenschutz != VibeVHS.Domain.Datenschutz.Datenschutzstatus.Anonymisiert)
            .OrderBy(p => p.Id).ToDictionaryAsync(p => p.Id, abbruch);
        string? NameAm(Guid id, DateOnly tag) => personen.TryGetValue(id, out var p) ? p.StandAm(tag, jetzt).NameMitTitel : null;
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var namen = personen.ToDictionary(p => p.Key, p => p.Value.Anzeigename);
        var organisationen = await db.Organisationen.AsNoTracking().Select(o => new { o.Id, o.Name }).ToDictionaryAsync(o => o.Id, o => o.Name, abbruch);
        foreach (var kurs in kurse)
        {
            var anders = kurs.NamenskopienAbgleichen(NameAm, heute);
            if (kurs.AuftraggeberId is { } org && organisationen.TryGetValue(org, out var orgName) && kurs.AuftraggeberNameNachziehen(orgName))
            {
                anders = true;
            }

            geaendert += anders ? 1 : 0;
        }

        geaendert += konten.Count(k => namen.TryGetValue(k.PersonId!.Value, out var name) && k.NameNachziehen(name));
        await db.SaveChangesAsync(abbruch);
        return geaendert;
    }
}
