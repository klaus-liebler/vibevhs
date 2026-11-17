using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Personen;

/// <summary>
/// Namen der Bevollmächtigten (Person oder Organisation) zur Laufzeit – gebündelt in einer Abfrage je Art, keine Kopie in Vollmachten
/// oder Belegungen.
/// </summary>
internal static class Bevollmaechtigtennamen
{
    public static async Task ErgaenzenAsync(VibeVhsDbContext db, IEnumerable<Person> personen, CancellationToken abbruch)
    {
        var vollmachten = personen.SelectMany(p => p.Vollmachten).ToList();
        if (vollmachten.Count == 0)
        {
            return;
        }

        var ids = vollmachten.Select(v => v.BevollmaechtigterId).Distinct().ToList();
        var namen = await new Namensquelle(db).LadenAsync(ids, [], ids, abbruch);
        foreach (var v in vollmachten.Where(v => v.BevollmaechtigterName is not "–"))
        {
            v.NameSetzen(namen.PersonOderOrganisation(v.BevollmaechtigterId));
        }
    }

    public static async Task<T> ErgaenzenAsync<T>(VibeVhsDbContext db, T belegungen, CancellationToken abbruch)
        where T : IEnumerable<Belegung>
    {
        var liste = belegungen.Where(b => b.BevollmaechtigterId is not null).ToList();
        if (liste.Count > 0)
        {
            var ids = liste.Select(b => b.BevollmaechtigterId!.Value).Distinct().ToList();
            var namen = await new Namensquelle(db).LadenAsync(ids, [], ids, abbruch);
            foreach (var b in liste)
            {
                b.NameSetzen(namen.PersonOderOrganisation(b.BevollmaechtigterId!.Value));
            }
        }

        return belegungen;
    }
}
