using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Datenschutz;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Datenschutz;

internal sealed class DatenschutzSpeicher(VibeVhsDbContext db) : IDatenschutzSpeicher
{
    public async Task<IReadOnlyList<Aufbewahrungsfrist>> FristenAsync(CancellationToken abbruch = default)
        => await db.Set<Aufbewahrungsfrist>().AsNoTracking().OrderBy(f => f.Bezeichnung).ToListAsync(abbruch);

    public Task<Aufbewahrungsfrist?> FristLadenAsync(string schluessel, CancellationToken abbruch = default)
        => db.Set<Aufbewahrungsfrist>().FirstOrDefaultAsync(f => f.Schluessel == schluessel, abbruch);

    public async Task<IReadOnlyList<Aufbewahrungsgrund>> GruendeAsync(Guid personId, CancellationToken abbruch = default)
    {
        // Honorarabrechnung als Dozent/in: Buchhaltungsfrist ab dem letzten Termin.
        // (Buchungen, Zahlungen und Integrationskurs-Teilnahmen kommen mit I3/I6/I7 als weitere Gründe hinzu.)
        var alsDozent = await db.Veranstaltungen.AsNoTracking()
            .Where(v => v.DozentId == personId && !v.Ausgefallen)
            .GroupBy(v => v.KursId)
            .Select(g => new { KursId = g.Key, Letzter = g.Max(v => v.Ende) })
            .Join(db.Kurse.IgnoreAutoIncludes(), x => x.KursId, k => k.Id, (x, k) => new { k.Kursnummer, x.Letzter })
            .ToListAsync(abbruch);
        var gruende = alsDozent
            .Select(x => new Aufbewahrungsgrund("buchhaltung", $"Honorarabrechnung als Dozent/in, Kurs {x.Kursnummer}", DateOnly.FromDateTime(x.Letzter)))
            .ToList();

        // Kursentgelte: Belegungen mit Entgelt oder Gebühr sind buchhaltungsrelevant (ab dem letzten Termin bzw. der Buchung).
        var belegungen = await db.Belegungen.IgnoreAutoIncludes().AsNoTracking()
            .Where(b => b.PersonId == personId && (b.EntgeltCent > 0 || b.GebuehrCent > 0)
                        && (b.Status == Domain.Anmeldung.Belegungsstatus.Angemeldet || b.Status == Domain.Anmeldung.Belegungsstatus.Storniert
                            || b.Status == Domain.Anmeldung.Belegungsstatus.Umgebucht))
            .Select(b => new { b.KursId, b.Kursnummer, b.GebuchtAm, b.Status, b.GebuehrCent })
            .ToListAsync(abbruch);
        var kursIds = belegungen.Select(b => b.KursId).Distinct().ToList();
        var enden = await db.Veranstaltungen.AsNoTracking().Where(v => kursIds.Contains(v.KursId))
            .GroupBy(v => v.KursId).Select(g => new { g.Key, Ende = g.Max(v => v.Ende) }).ToDictionaryAsync(x => x.Key, x => x.Ende, abbruch);
        foreach (var b in belegungen.Where(b => b.Status == Domain.Anmeldung.Belegungsstatus.Angemeldet || b.GebuehrCent > 0))
        {
            var bezug = enden.TryGetValue(b.KursId, out var ende) ? DateOnly.FromDateTime(ende) : DateOnly.FromDateTime(b.GebuchtAm.Date);
            gruende.Add(new Aufbewahrungsgrund("buchhaltung", $"Kursentgelt, Kurs {b.Kursnummer}", bezug));
        }

        // Ausgestellte Bescheinigungen (Nachweis für Teilnehmende, Arbeitgeber, Kostenträger)
        foreach (var d in await db.Set<Domain.Dokumente.Dokument>().AsNoTracking()
                     .Where(d => d.PersonId == personId && d.AnonymisiertAm == null
                                 && (d.Art == Domain.Dokumente.Dokumentart.Teilnahmebescheinigung || d.Art == Domain.Dokumente.Dokumentart.Bildungsurlaub))
                     .Select(d => new { d.Titel, d.ErstelltAm }).ToListAsync(abbruch))
        {
            gruende.Add(new Aufbewahrungsgrund("bescheinigungen", d.Titel, DateOnly.FromDateTime(d.ErstelltAm.Date)));
        }

        return gruende;
    }

    public async Task<IReadOnlyList<PersonAktivitaet>> InaktivePersonenAsync(DateTimeOffset vor, CancellationToken abbruch = default)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var kandidaten = await db.Personen.IgnoreAutoIncludes().AsNoTracking()
            .Where(p => p.Benutzerkennung == null && p.Status != Personenstatus.Zusammengefuehrt
                        && (p.Datenschutz == Datenschutzstatus.Aktiv || p.Datenschutz == Datenschutzstatus.Archiviert
                            || (p.Datenschutz == Datenschutzstatus.Gesperrt && p.GesperrtBis < heute)))
            .Select(p => new { p.Id, p.Anzeigename, p.AngelegtAm, p.Datenschutz })
            .ToListAsync(abbruch);

        var letzte = kandidaten.ToDictionary(k => k.Id, k => k.AngelegtAm);
        void Max(IEnumerable<(Guid Id, DateTimeOffset Am)> werte)
        {
            foreach (var (id, am) in werte)
            {
                if (letzte.TryGetValue(id, out var bisher) && am > bisher)
                {
                    letzte[id] = am;
                }
            }
        }

        Max(await Letzte<PersonName>(abbruch));
        Max(await Letzte<PersonMerkmal>(abbruch));
        Max(await Letzte<Postadresse>(abbruch));
        Max(await Letzte<AdressKennzeichen>(abbruch));
        Max(await Letzte<Rufnummer>(abbruch));
        Max(await Letzte<EMailAdresse>(abbruch));
        Max(await Letzte<Organisationszugehoerigkeit>(abbruch));
        Max(await Letzte<Buchungsvollmacht>(abbruch));
        Max(await Letzte<Einwilligung>(abbruch));
        Max(await Letzte<PersonQualifikation>(abbruch));
        Max((await db.Veranstaltungen.AsNoTracking().Where(v => v.DozentId != null)
                .GroupBy(v => v.DozentId!.Value).Select(g => new { g.Key, Ende = g.Max(v => v.Ende) }).ToListAsync(abbruch))
            .Select(x => (x.Key, new DateTimeOffset(x.Ende))));
        Max((await db.Postausgang.AsNoTracking().Where(n => n.PersonId != null)
                .Select(n => new { Id = n.PersonId!.Value, n.ErstelltAm }).ToListAsync(abbruch))
            .GroupBy(x => x.Id).Select(g => (g.Key, g.Max(x => x.ErstelltAm))));
        Max((await db.Set<Domain.Durchfuehrung.Anwesenheit>().AsNoTracking().Select(a => new { a.PersonId, a.ErfasstAm }).ToListAsync(abbruch))
            .GroupBy(x => x.PersonId).Select(g => (g.Key, g.Max(x => x.ErfasstAm))));
        Max((await db.Belegungen.IgnoreAutoIncludes().AsNoTracking().Select(b => new { b.PersonId, b.GebuchtAm }).ToListAsync(abbruch))
            .GroupBy(x => x.PersonId).Select(g => (g.Key, g.Max(x => x.GebuchtAm))));
        Max((await db.KontingentEreignisse.AsNoTracking()
                .Join(db.Belegungen.IgnoreAutoIncludes(), e => e.BelegungId, b => b.Id, (e, b) => new { b.PersonId, e.Zeitpunkt }).ToListAsync(abbruch))
            .GroupBy(x => x.PersonId).Select(g => (g.Key, g.Max(x => x.Zeitpunkt))));

        return kandidaten
            .Where(k => letzte[k.Id] < vor || (k.Datenschutz == Datenschutzstatus.Gesperrt))
            .Select(k => new PersonAktivitaet(k.Id, k.Anzeigename, letzte[k.Id], k.Datenschutz))
            .OrderBy(k => k.LetzteAktivitaet)
            .ToList();
    }

    /// <summary>Jüngste Erfassung je Person in einer Versionstabelle.</summary>
    private async Task<IEnumerable<(Guid, DateTimeOffset)>> Letzte<T>(CancellationToken abbruch)
        where T : BitemporaleVersion<T>
    {
        var roh = await db.Set<T>().AsNoTracking()
            .Select(v => new { Id = EF.Property<Guid>(v, "PersonId"), v.ErfasstAm })
            .ToListAsync(abbruch);
        return roh.GroupBy(x => x.Id).Select(g => (g.Key, g.Max(x => x.ErfasstAm)));
    }

    public async Task<IReadOnlyList<KursEnde>> KurseEndeVorAsync(DateOnly vor, CancellationToken abbruch = default)
    {
        var grenze = vor.ToDateTime(TimeOnly.MinValue);
        var enden = await db.Veranstaltungen.AsNoTracking().GroupBy(v => v.KursId).Select(g => new { g.Key, Ende = g.Max(v => v.Ende) })
            .Where(x => x.Ende < grenze).ToListAsync(abbruch);
        var ids = enden.Select(e => e.Key).ToList();
        var kurse = await db.Kurse.IgnoreAutoIncludes().AsNoTracking().Where(k => ids.Contains(k.Id))
            .Select(k => new { k.Id, k.Kursnummer, k.Titel, k.ArchiviertAm }).ToListAsync(abbruch);
        return kurse.Select(k => new KursEnde(k.Id, k.Kursnummer, k.Titel, DateOnly.FromDateTime(enden.First(e => e.Key == k.Id).Ende), k.ArchiviertAm != null))
            .OrderBy(k => k.Ende).ToList();
    }

    public async Task<IReadOnlyList<Semester>> SemesterEndeVorAsync(DateOnly vor, CancellationToken abbruch = default)
        => await db.Semester.AsNoTracking().Where(s => s.Ende < vor).OrderBy(s => s.Beginn).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Kurs>> KurseLadenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default)
    {
        var liste = ids.ToList();
        return await db.Kurse.Where(k => liste.Contains(k.Id)).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Kurs>> KurseDesSemestersAsync(Guid semesterId, CancellationToken abbruch = default)
        => await db.Kurse.Where(k => k.SemesterId == semesterId).ToListAsync(abbruch);

    public Task<Semester?> SemesterLadenAsync(Guid id, CancellationToken abbruch = default) => db.Semester.FirstOrDefaultAsync(s => s.Id == id, abbruch);

    public async Task<IReadOnlyList<Kurs>> KurseMitDozentAsync(Guid personId, CancellationToken abbruch = default)
    {
        var ids = await db.Veranstaltungen.AsNoTracking().Where(v => v.DozentId == personId).Select(v => v.KursId).Distinct().ToListAsync(abbruch);

        // Team ist eine JSON-Spalte: zusätzlich Kurse, in deren Team die Person steht (auch ohne eigene Termine).
        var teams = await db.Kurse.IgnoreAutoIncludes().AsNoTracking().Select(k => new { k.Id, k.Team }).ToListAsync(abbruch);
        ids.AddRange(teams.Where(k => k.Team.Any(t => t.PersonId == personId)).Select(k => k.Id));
        ids = ids.Distinct().ToList();
        return ids.Count == 0 ? [] : await db.Kurse.Include(k => k.Veranstaltungen).Where(k => ids.Contains(k.Id)).ToListAsync(abbruch);
    }
    public async Task<IReadOnlyList<Person>> PersonenMitVollmachtFuerAsync(Guid bevollmaechtigterId, CancellationToken abbruch = default)
    {
        var ids = await db.Set<Buchungsvollmacht>().AsNoTracking().Where(v => v.BevollmaechtigterId == bevollmaechtigterId)
            .Select(v => EF.Property<Guid>(v, "PersonId")).Distinct().ToListAsync(abbruch);
        return await db.Personen.Where(p => ids.Contains(p.Id)).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<AusgehendeNachricht>> NachrichtenAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Postausgang.Where(n => n.PersonId == personId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Domain.Anmeldung.Belegung>> BelegungenAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Belegungen.Where(b => b.PersonId == personId || b.BevollmaechtigterId == personId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Domain.Anmeldung.Ermaessigung>> ErmaessigungenAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Ermaessigungen.Where(e => e.PersonId == personId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Domain.Durchfuehrung.Zugangstoken>> ZugangstokensAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Set<Domain.Durchfuehrung.Zugangstoken>().Where(t => t.PersonId == personId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Domain.Dokumente.Dokument>> DokumenteAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Set<Domain.Dokumente.Dokument>().Where(d => d.PersonId == personId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Domain.Aufgaben.Aufgabe>> AufgabenZuPersonAsync(Guid personId, CancellationToken abbruch = default)
    {
        var ziel = $"personen/{personId}";
        var teil = personId.ToString("N");
        return await db.Aufgaben.Where(a => a.Ziel == ziel || a.Schluessel.Contains(teil)).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> KurstitelAsync(IEnumerable<Guid> kursIds, CancellationToken abbruch = default)
    {
        var ids = kursIds.ToList();
        return await db.Kurse.IgnoreAutoIncludes().AsNoTracking().Where(k => ids.Contains(k.Id))
            .ToDictionaryAsync(k => k.Id, k => k.Kursnummer + " " + k.Titel, abbruch);
    }

    public async Task<IReadOnlyList<AuditEintrag>> ProtokollZuAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Protokoll.AsNoTracking().Where(e => e.ObjektId == personId).OrderBy(e => e.Zeitpunkt).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Loeschlauf>> LoeschlaeufeAsync(CancellationToken abbruch = default)
        => await db.Set<Loeschlauf>().AsNoTracking().OrderByDescending(l => l.ErstelltAm).ToListAsync(abbruch);

    public Task<Loeschlauf?> LoeschlaufLadenAsync(Guid id, CancellationToken abbruch = default)
        => db.Set<Loeschlauf>().FirstOrDefaultAsync(l => l.Id == id, abbruch);

    public void Hinzufuegen(object eintrag)
    {
        if (db.Entry(eintrag).State == EntityState.Detached)
        {
            db.Add(eintrag);
        }
    }

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
