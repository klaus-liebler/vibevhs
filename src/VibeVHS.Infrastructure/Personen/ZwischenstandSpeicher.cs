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
}
