using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Personen;

/// <summary>
/// Personenliste direkt aus den Versionen (Stand heute, keine Kopien): Filter und Sortierung in SQL über die am Stichtag gültigen
/// Namen und Merkmale, danach werden nur die Personen der Seite vollständig geladen und ihr Stand berechnet.
/// Blättern per Keyset (Sortierschlüssel &gt; letzter Schlüssel) statt OFFSET: gleich schnell auf jeder Seite.
/// </summary>
internal sealed class PersonenAbfrage(VibeVhsDbContext db, IDublettenSuche dubletten, TimeProvider? uhr = null) : IPersonenAbfrage, IPersonenExport
{
    private DateTimeOffset Jetzt => (uhr ?? TimeProvider.System).GetLocalNow();

    private DateOnly Heute => DateOnly.FromDateTime(Jetzt.Date);

    public async Task<IReadOnlyList<PersonZeile>> SeiteAsync(PersonenFilter filter, Seitenschluessel? nach, int anzahl,
        CancellationToken abbruch = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(anzahl, 1);
        var sortiert = db.MitSortierschluessel(await BasisAsync(filter, abbruch), Heute);
        if (nach is not null)
        {
            sortiert = sortiert.Nach(nach.Nachname, nach.Vorname, nach.IdHex);
        }

        var seite = await sortiert.Sortiert().Take(anzahl).ToListAsync(abbruch);
        var ids = seite.Select(x => x.Id).ToList();
        var personen = await db.Personen.AsNoTracking().AsSplitQuery().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, abbruch);
        var jetzt = Jetzt;
        return seite.Where(x => personen.ContainsKey(x.Id)).Select(x =>
        {
            var p = personen[x.Id];
            p.StichzeitSetzen(jetzt);
            return new PersonZeile(p.Id, p.Anzeigename, new Seitenschluessel(x.Nachname, x.Vorname, x.IdHex), p.Geburtsdatum, p.Wohnort, p.EMail, p.Sprache, p.Rollen, p.Status, p.Fachbereiche);
        }).ToList();
    }

    public async Task<RundschreibenVorschau> RundschreibenZaehlenAsync(PersonenFilter filter, Kontaktzweck zweck, CancellationToken abbruch = default)
    {
        var basis = await BasisAsync(filter, abbruch);
        var treffer = await basis.CountAsync(abbruch);
        var mitMail = MitEMail(basis);
        var mitEMail = await mitMail.CountAsync(abbruch);
        var empfaenger = zweck == Kontaktzweck.Kursorganisation ? mitEMail : await MitEinwilligung(mitMail, zweck).CountAsync(abbruch);
        return new RundschreibenVorschau(treffer, mitEMail, empfaenger, mitEMail - empfaenger);
    }

    public async IAsyncEnumerable<Person> RundschreibenEmpfaengerAsync(PersonenFilter filter, Kontaktzweck zweck,
        [EnumeratorCancellation] CancellationToken abbruch = default)
    {
        var mitMail = MitEMail(await BasisAsync(filter, abbruch));
        var empfaenger = zweck == Kontaktzweck.Kursorganisation ? mitMail : MitEinwilligung(mitMail, zweck);
        var ids = await empfaenger.Select(p => p.Id).OrderBy(id => id).ToListAsync(abbruch);
        var jetzt = Jetzt;
        foreach (var paket in ids.Chunk(500))
        {
            foreach (var p in await db.Personen.AsNoTracking().AsSplitQuery().Where(p => paket.Contains(p.Id)).OrderBy(p => p.Id).ToListAsync(abbruch))
            {
                p.StichzeitSetzen(jetzt);
                yield return p;
            }
        }
    }

    /// <summary>Personen mit heute gültiger E-Mail-Adresse („–“ = bewusst keine).</summary>
    private IQueryable<Person> MitEMail(IQueryable<Person> personen)
    {
        var tag = Heute;
        var mitAdresse = db.Set<EMailAdresse>()
            .Where(e => EF.Property<Guid?>(e, "ErsetztDurchVorgangId") == null && e.GueltigVon <= tag && (e.GueltigBis == null || e.GueltigBis > tag) && e.Adresse != "–" && e.Adresse != "")
            .Select(e => EF.Property<Guid>(e, "PersonId"));
        return personen.Where(p => mitAdresse.Contains(p.Id));
    }

    /// <summary>Personen mit heute gültiger Einwilligung für E-Mail zu diesem Zweck.</summary>
    private IQueryable<Person> MitEinwilligung(IQueryable<Person> personen, Kontaktzweck zweck)
    {
        var tag = Heute;
        var erteilt = db.Set<Einwilligung>()
            .Where(w => EF.Property<Guid?>(w, "ErsetztDurchVorgangId") == null && w.GueltigVon <= tag && (w.GueltigBis == null || w.GueltigBis > tag)
                        && w.Kanal == Kontaktkanal.EMail && w.Zweck == zweck && w.Erteilt)
            .Select(w => EF.Property<Guid>(w, "PersonId"));
        return personen.Where(p => erteilt.Contains(p.Id));
    }

    /// <summary>Für Rundschreiben und Export: alle Treffer mit vollständig geladener Person, paketweise (500).</summary>
    public async IAsyncEnumerable<Person> PersonenAsync(PersonenFilter filter, [EnumeratorCancellation] CancellationToken abbruch = default)
    {
        var ids = await db.MitSortierschluessel(await BasisAsync(filter, abbruch), Heute).Sortiert().Select(x => x.Id).ToListAsync(abbruch);
        var jetzt = Jetzt;
        foreach (var paket in ids.Chunk(500))
        {
            var personen = await db.Personen.AsNoTracking().AsSplitQuery().Where(p => paket.Contains(p.Id)).ToDictionaryAsync(p => p.Id, abbruch);
            foreach (var id in paket)
            {
                if (personen.TryGetValue(id, out var p))
                {
                    p.StichzeitSetzen(jetzt);
                    yield return p;
                }
            }
        }
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
            var mitRolle = db.MitRolle(rolle, Heute);
            q = q.Where(p => mitRolle.Contains(p.Id));
        }

        if (filter.Fachbereich is { } fb)
        {
            q = q.Where(p => EF.Property<List<Domain.Gemeinsam.Listenwert<int>>>(p, "_fachbereiche").Any(f => f.Wert == fb));
        }

        // Suche direkt in den Versionen (kein Suchindex, keine Kopie): jedes Wort muss als Wortanfang vorkommen.
        foreach (var wort in Domain.Gemeinsam.Suchtext.Woerter(filter.Suche))
        {
            var treffer = db.MitSuchwort(wort);
            q = q.Where(p => treffer.Contains(p.Id));
        }

        if (filter.NurDublettenHinweise)
        {
            var ids = (await dubletten.PaareAsync(abbruch)).SelectMany(p => new[] { p.A, p.B }).Distinct().ToList();
            q = q.Where(p => ids.Contains(p.Id));
        }

        return q;
    }
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
        var mitDatum = db.MitGeburtsdatum(geburtsdatum.Value);
        var gleicherTag = (await db.Personen.AsNoTracking().AsSplitQuery()
                .Where(p => mitDatum.Contains(p.Id) && p.Status != Personenstatus.Zusammengefuehrt && p.Id != ausser
                            && p.Datenschutz == Domain.Datenschutz.Datenschutzstatus.Aktiv)
                .ToListAsync(abbruch))
            .Select(p => new { p.Id, p.Anzeigename, p.Sortiername, p.Geburtsdatum, p.Wohnort })
            .ToList();
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
        // Geburtsdatum und heutiger Name direkt aus den Versionen (eine Abfrage je Tabelle).
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var aktiv = db.Personen.IgnoreAutoIncludes().Where(p => p.Status != Personenstatus.Zusammengefuehrt && p.Datenschutz == Domain.Datenschutz.Datenschutzstatus.Aktiv)
            .Select(p => p.Id);
        var geburt = (await db.Set<PersonMerkmal>().AsNoTracking()
                .Where(m => m.Art == MerkmalArt.Geburtsdatum && EF.Property<Guid?>(m, "ErsetztDurchVorgangId") == null && aktiv.Contains(EF.Property<Guid>(m, "PersonId")))
                .Select(m => new { PersonId = EF.Property<Guid>(m, "PersonId"), m.Inhalt }).ToListAsync(abbruch))
            .GroupBy(m => m.PersonId).ToDictionary(g => g.Key, g => g.First().Inhalt);
        var namen = await db.NamenAm(heute).AsNoTracking().Where(n => aktiv.Contains(EF.Property<Guid>(n, "PersonId")))
            .Select(n => new { PersonId = EF.Property<Guid>(n, "PersonId"), Name = n }).ToListAsync(abbruch);
        var alle = namen.Where(n => geburt.ContainsKey(n.PersonId))
            .Select(n => new { Id = n.PersonId, Geburtsdatum = (DateOnly?)DateOnly.ParseExact(geburt[n.PersonId], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Sortiername = n.Name.Sortierschluessel })
            .ToList();
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
