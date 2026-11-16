using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Personen;

/// <summary>
/// Abfragen über die Zwischenstands-Spalten der Personentabelle (ohne Historie zu laden).
/// Blättern per Keyset (Sortierschlüssel &gt; letzter Schlüssel) statt OFFSET: gleich schnell auf jeder Seite.
/// </summary>
internal sealed class PersonenAbfrage(VibeVhsDbContext db, IDublettenSuche dubletten) : IPersonenAbfrage, IPersonenExport
{
    public async Task<IReadOnlyList<PersonZeile>> SeiteAsync(PersonenFilter filter, Seitenschluessel? nach, int anzahl,
        CancellationToken abbruch = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(anzahl, 1);
        var q = await BasisAsync(filter, abbruch);
        if (nach is not null)
        {
            q = q.Where(p => string.Compare(p.Sortierschluessel, nach.Wert) > 0);
        }

        var roh = await q.OrderBy(p => p.Sortierschluessel).Take(anzahl).Select(Projektion).ToListAsync(abbruch);
        return roh.Select(Zeile).ToList();
    }

    public async Task<Filterzahlen> ZaehlenAsync(PersonenFilter filter, CancellationToken abbruch = default)
    {
        var gesamt = await (await BasisAsync(filter, abbruch)).CountAsync(abbruch);

        var rollen = new Dictionary<Personenrolle, int>();
        foreach (var r in new[] { Personenrolle.Teilnehmende, Personenrolle.Dozierende, Personenrolle.Mitarbeitende })
        {
            rollen[r] = await (await BasisAsync(filter with { Rolle = r }, abbruch)).CountAsync(abbruch);
        }

        var status = new Dictionary<Personenstatus, int>();
        foreach (var s in new[] { Personenstatus.Aktiv, Personenstatus.Ruhend, Personenstatus.LoeschungVorgemerkt })
        {
            status[s] = await (await BasisAsync(filter with { Status = s }, abbruch)).CountAsync(abbruch);
        }

        var fachbereiche = new Dictionary<int, int>();
        var nummern = await db.Fachbereiche.Select(f => f.Nr).OrderBy(n => n).ToListAsync(abbruch);
        foreach (var nr in nummern.Count > 0 ? nummern : Enumerable.Range(1, 8).ToList())
        {
            fachbereiche[nr] = await (await BasisAsync(filter with { Fachbereich = nr }, abbruch)).CountAsync(abbruch);
        }

        var hinweise = await (await BasisAsync(filter with { NurDublettenHinweise = true }, abbruch)).CountAsync(abbruch);
        var datenschutz = new Dictionary<Domain.Datenschutz.Datenschutzstatus, int>();
        foreach (var d in Enum.GetValues<Domain.Datenschutz.Datenschutzstatus>().Where(d => d != Domain.Datenschutz.Datenschutzstatus.Aktiv))
        {
            datenschutz[d] = await (await BasisAsync(filter with { Datenschutz = d, Suche = filter.Suche }, abbruch)).CountAsync(abbruch);
        }

        return new Filterzahlen(gesamt, rollen, status, fachbereiche, hinweise, datenschutz);
    }

    public async IAsyncEnumerable<PersonZeile> AlleAsync(PersonenFilter filter, [EnumeratorCancellation] CancellationToken abbruch = default)
    {
        Seitenschluessel? nach = null;
        while (true)
        {
            var seite = await SeiteAsync(filter, nach, 500, abbruch);
            foreach (var z in seite)
            {
                yield return z;
            }

            if (seite.Count < 500)
            {
                yield break;
            }

            nach = Seitenschluessel.Von(seite[^1]);
        }
    }

    public async Task<byte[]> ExcelAsync(PersonenFilter filter, CancellationToken abbruch = default)
    {
        var namen = await db.Fachbereiche.ToDictionaryAsync(f => f.Nr, f => f.Name, abbruch);
        using var mappe = new XLWorkbook();
        var blatt = mappe.Worksheets.Add("Personen");
        string[] kopf = ["Name", "Geburtsdatum", "Wohnort", "E-Mail", "Sprache", "Rollen", "Status", "Fachbereiche"];
        for (var i = 0; i < kopf.Length; i++)
        {
            blatt.Cell(1, i + 1).Value = kopf[i];
        }

        var zeile = 2;
        await foreach (var p in AlleAsync(filter, abbruch))
        {
            blatt.Cell(zeile, 1).Value = p.Anzeigename;
            if (p.Geburtsdatum is { } g)
            {
                blatt.Cell(zeile, 2).Value = g.ToDateTime(TimeOnly.MinValue);
                blatt.Cell(zeile, 2).Style.DateFormat.Format = "dd.MM.yyyy";
            }

            blatt.Cell(zeile, 3).Value = p.Wohnort;
            blatt.Cell(zeile, 4).Value = p.EMail;
            blatt.Cell(zeile, 5).Value = p.Sprache;
            blatt.Cell(zeile, 6).Value = PersonMerkmal.RollenAnzeige(p.Rollen);
            blatt.Cell(zeile, 7).Value = PersonenstatusAnzeige.Text(p.Status);
            blatt.Cell(zeile, 8).Value = string.Join("; ", p.Fachbereiche.Select(n => namen.TryGetValue(n, out var name) ? $"{n} {name}" : n.ToString()));
            zeile++;
        }

        var tabelle = blatt.Range(1, 1, Math.Max(zeile - 1, 1), kopf.Length).CreateTable("Personen");
        tabelle.ShowAutoFilter = true;
        blatt.Columns().AdjustToContents(1, Math.Min(zeile, 200));
        blatt.SheetView.FreezeRows(1);

        using var strom = new MemoryStream();
        mappe.SaveAs(strom);
        return strom.ToArray();
    }

    private async Task<IQueryable<Person>> BasisAsync(PersonenFilter filter, CancellationToken abbruch)
    {
        var q = db.Personen.IgnoreAutoIncludes().AsNoTracking();
        var ds = filter.Datenschutz ?? Domain.Datenschutz.Datenschutzstatus.Aktiv;
        q = q.Where(p => p.Datenschutz == ds);
        q = filter.Status is { } s
            ? q.Where(p => p.Status == s)
            : q.Where(p => p.Status != Personenstatus.Zusammengefuehrt);

        if (filter.Rolle is { } rolle)
        {
            q = q.Where(p => (p.Rollen & rolle) == rolle);
        }

        if (filter.Fachbereich is { } fb)
        {
            q = q.Where(p => p.Fachbereiche.Contains(fb));
        }

        if (PersonenSuchindex.Ausdruck(filter.Suche) is { } ausdruck)
        {
            var treffer = PersonenSuchindex.Treffer(db, ausdruck);
            q = q.Where(p => treffer.Contains(p.Id));
        }

        if (filter.NurDublettenHinweise)
        {
            var ids = (await dubletten.PaareAsync(abbruch)).SelectMany(p => new[] { p.A, p.B }).Distinct().ToList();
            q = q.Where(p => ids.Contains(p.Id));
        }

        return q;
    }

    private sealed record Roh(Guid Id, string Anzeigename, string Sortierschluessel, DateOnly? Geburtsdatum, string? Wohnort, string? EMail,
        string Sprache, Personenrolle Rollen, Personenstatus Status, List<int> Fachbereiche);

    private static readonly System.Linq.Expressions.Expression<Func<Person, Roh>> Projektion = p =>
        new Roh(p.Id, p.Anzeigename, p.Sortierschluessel, p.Geburtsdatum, p.Wohnort, p.EMail, p.Sprache, p.Rollen, p.Status, p.Fachbereiche);

    private static PersonZeile Zeile(Roh r)
        => new(r.Id, r.Anzeigename, r.Sortierschluessel, r.Geburtsdatum, r.Wohnort, r.EMail, r.Sprache, r.Rollen, r.Status, r.Fachbereiche);
}

/// <summary>Dublettensuche über Geburtsdatum (Index) und normalisierte Namen.</summary>
internal sealed class DublettenSuche(VibeVhsDbContext db) : IDublettenSuche
{
    public async Task<IReadOnlyList<DublettenKandidat>> KandidatenAsync(string vorname, string nachname, DateOnly? geburtsdatum,
        Guid? ausser = null, CancellationToken abbruch = default)
    {
        if (geburtsdatum is null || string.IsNullOrWhiteSpace(nachname)) // Vorname leer = nur ein Name laut Ausweis
        {
            return [];
        }

        var form = Dublettenregel.Normalform(vorname, nachname);
        var gleicherTag = await db.Personen.IgnoreAutoIncludes().AsNoTracking()
            .Where(p => p.Geburtsdatum == geburtsdatum && p.Status != Personenstatus.Zusammengefuehrt && p.Id != ausser && p.Datenschutz == Domain.Datenschutz.Datenschutzstatus.Aktiv)
            .Select(p => new { p.Id, p.Anzeigename, p.Sortiername, p.Geburtsdatum, p.Wohnort })
            .ToListAsync(abbruch);
        var ausgeschlossen = ausser is { } a ? await AusschluesseAsync(a, abbruch) : [];

        return gleicherTag
            .Where(p => !ausgeschlossen.Contains(p.Id))
            .Select(p => (p, grund: Dublettenregel.Aehnlichkeit(form, p.Sortiername)))
            .Where(x => x.grund is not null)
            .Select(x => new DublettenKandidat(x.p.Id, x.p.Anzeigename, x.p.Geburtsdatum, x.p.Wohnort, x.grund!))
            .ToList();
    }

    public async Task<IReadOnlyList<DublettenPaar>> PaareAsync(CancellationToken abbruch = default)
    {
        var alle = await db.Personen.IgnoreAutoIncludes().AsNoTracking()
            .Where(p => p.Geburtsdatum != null && p.Status != Personenstatus.Zusammengefuehrt && p.Datenschutz == Domain.Datenschutz.Datenschutzstatus.Aktiv)
            .Select(p => new { p.Id, p.Geburtsdatum, p.Sortiername })
            .ToListAsync(abbruch);
        var ausschluesse = (await db.DublettenAusschluesse.AsNoTracking().Select(d => new { d.PersonA, d.PersonB }).ToListAsync(abbruch))
            .Select(d => (d.PersonA, d.PersonB)).ToHashSet();

        var paare = new List<DublettenPaar>();
        foreach (var gruppe in alle.GroupBy(p => p.Geburtsdatum).Where(g => g.Count() > 1))
        {
            var liste = gruppe.ToList();
            for (var i = 0; i < liste.Count; i++)
            {
                for (var j = i + 1; j < liste.Count; j++)
                {
                    var (x, y) = liste[i].Id.CompareTo(liste[j].Id) < 0 ? (liste[i], liste[j]) : (liste[j], liste[i]);
                    if (ausschluesse.Contains((x.Id, y.Id)))
                    {
                        continue;
                    }

                    if (Dublettenregel.Aehnlichkeit(x.Sortiername, y.Sortiername) is { } grund)
                    {
                        paare.Add(new DublettenPaar(x.Id, y.Id, grund));
                    }
                }
            }
        }

        return paare;
    }

    private async Task<HashSet<Guid>> AusschluesseAsync(Guid id, CancellationToken abbruch)
        => (await db.DublettenAusschluesse.AsNoTracking()
                .Where(d => d.PersonA == id || d.PersonB == id)
                .Select(d => d.PersonA == id ? d.PersonB : d.PersonA)
                .ToListAsync(abbruch))
            .ToHashSet();
}

internal sealed class OrganisationenSpeicher(VibeVhsDbContext db) : IOrganisationen
{
    public async Task<IReadOnlyList<Organisation>> AlleAsync(CancellationToken abbruch = default)
        => await db.Organisationen.AsNoTracking().OrderBy(o => o.Name).ToListAsync(abbruch);

    public Task<Organisation?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Organisationen.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, abbruch);
}
