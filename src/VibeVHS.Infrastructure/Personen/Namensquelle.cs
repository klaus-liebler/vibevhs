using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Programm;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Personen;

/// <summary>
/// Namen direkt aus den Versionstabellen (aktueller Wissensstand, alle Gültigkeitsabschnitte) und den Stammdaten – je Art eine Abfrage.
/// Der Titel gilt wie der Name zum jeweiligen Tag; die Abschnitte von Name und Titel werden zusammengeführt.
/// </summary>
internal sealed class Namensquelle(VibeVhsDbContext db) : INamensquelle
{
    public async Task<Namen> LadenAsync(IEnumerable<Guid> personen, IEnumerable<Guid> orte, IEnumerable<Guid> organisationen, CancellationToken abbruch = default)
    {
        var pIds = personen.Distinct().ToList();
        var oIds = orte.Distinct().ToList();
        var gIds = organisationen.Distinct().ToList();
        var heute = DateOnly.FromDateTime(db.Uhr.GetLocalNow().Date);
        var ergebnis = new Dictionary<Guid, IReadOnlyList<Namensversion>>();
        if (pIds.Count > 0)
        {
            var namen = await db.Set<PersonName>().AsNoTracking().Where(n => n.ErsetztAm == null && pIds.Contains(EF.Property<Guid>(n, "PersonId")))
                .Select(n => new { PersonId = EF.Property<Guid>(n, "PersonId"), Version = n }).ToListAsync(abbruch);
            var titel = await db.Set<PersonMerkmal>().AsNoTracking()
                .Where(m => m.Art == MerkmalArt.Titel && m.ErsetztAm == null && pIds.Contains(EF.Property<Guid>(m, "PersonId")))
                .Select(m => new { PersonId = EF.Property<Guid>(m, "PersonId"), m.Inhalt, m.GueltigVon, m.GueltigBis }).ToListAsync(abbruch);
            var anonymisiert = await db.Personen.IgnoreAutoIncludes().AsNoTracking()
                .Where(p => pIds.Contains(p.Id) && p.Datenschutz == Datenschutzstatus.Anonymisiert).Select(p => p.Id).ToListAsync(abbruch);
            var titelJe = titel.ToLookup(t => t.PersonId);
            foreach (var g in namen.GroupBy(n => n.PersonId))
            {
                // Abschnittsgrenzen aus Name und Titel, je Abschnitt Name mit Titel.
                var t = titelJe[g.Key].ToList();
                var grenzen = g.Select(n => n.Version.GueltigVon).Concat(t.Select(x => x.GueltigVon))
                    .Concat(t.Where(x => x.GueltigBis is not null).Select(x => x.GueltigBis!.Value)).Distinct().Order().ToList();
                var liste = new List<Namensversion>();
                foreach (var n in g.Select(x => x.Version).OrderBy(x => x.GueltigVon))
                {
                    var abschnitte = grenzen.Where(d => d > n.GueltigVon && (n.GueltigBis is null || d < n.GueltigBis)).Prepend(n.GueltigVon).ToList();
                    for (var i = 0; i < abschnitte.Count; i++)
                    {
                        var von = abschnitte[i];
                        DateOnly? bis = i + 1 < abschnitte.Count ? abschnitte[i + 1] : n.GueltigBis;
                        var tt = t.FirstOrDefault(x => x.GueltigVon <= von && (x.GueltigBis is null || x.GueltigBis > von))?.Inhalt;
                        liste.Add(new Namensversion(von, bis, string.IsNullOrEmpty(tt) ? n.Anzeige : $"{tt} {n.Anzeige}"));
                    }
                }

                ergebnis[g.Key] = liste;
            }

            foreach (var id in anonymisiert)
            {
                ergebnis[id] = [new Namensversion(DateOnly.MinValue, null, $"Gelöschte Person {Pseudonym.Kurz(id)}")];
            }
        }

        var ortNamen = oIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await db.Orte.IgnoreAutoIncludes().AsNoTracking().Where(o => oIds.Contains(o.Id)).ToListAsync(abbruch)).ToDictionary(o => o.Id, o => o.Anzeige);
        var orgNamen = gIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Organisationen.AsNoTracking().Where(o => gIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, abbruch);
        return new Namen(ergebnis, ortNamen, orgNamen, heute);
    }
}
