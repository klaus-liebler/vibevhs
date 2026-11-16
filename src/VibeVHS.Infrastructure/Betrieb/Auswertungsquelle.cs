using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Betrieb;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Betrieb;

/// <summary>Liest die Grunddaten für Auswertungen in wenigen Abfragen (ohne Verfolgung) und rechnet im Speicher.</summary>
internal sealed class Auswertungsquelle(VibeVhsDbContext db, TimeProvider uhr) : IAuswertungsquelle
{
    public async Task<IReadOnlyList<Kurskennzahl>> KurseAsync(Guid? semesterId, int? fachbereich, CancellationToken abbruch = default)
    {
        var kurse = await db.Kurse.AsNoTracking().IgnoreAutoIncludes().Include(k => k.Veranstaltungen)
            .Where(k => k.ArchiviertAm == null && k.Status != Kursstatus.Entwurf && (semesterId == null || k.SemesterId == semesterId) && (fachbereich == null || k.Fachbereich == fachbereich))
            .ToListAsync(abbruch);
        var ids = kurse.Select(k => k.Id).ToList();
        var semester = await db.Semester.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, abbruch);
        var belegungen = await db.Belegungen.IgnoreAutoIncludes().AsNoTracking().Where(b => ids.Contains(b.KursId))
            .GroupBy(b => new { b.KursId, b.Status }).Select(g => new { g.Key.KursId, g.Key.Status, Anzahl = g.Count() }).ToListAsync(abbruch);
        var anwesenheit = await db.Set<Anwesenheit>().AsNoTracking().Where(a => ids.Contains(a.KursId))
            .GroupBy(a => a.KursId)
            .Select(g => new { g.Key, Gesamt = g.Count(), Anwesend = g.Count(a => a.Status == Anwesenheitsstatus.Anwesend) })
            .ToDictionaryAsync(x => x.Key, abbruch);
        var umsatz = await UmsatzJeKursAsync(abbruch);
        var jetzt = uhr.GetLocalNow().DateTime;
        return kurse.Select(k =>
        {
            int Anzahl(Belegungsstatus s) => belegungen.Where(b => b.KursId == k.Id && b.Status == s).Sum(b => b.Anzahl);
            var findet = k.Veranstaltungen.Where(v => !v.Ausgefallen).ToList();
            var a = anwesenheit.GetValueOrDefault(k.Id);
            return new Kurskennzahl(k.Id, k.Kursnummer, k.Titel, k.Fachbereich, semester.GetValueOrDefault(k.SemesterId) ?? "", k.Status.ToString(), k.MinTeilnehmende, k.MaxTeilnehmende,
                Anzahl(Belegungsstatus.Angemeldet) + Anzahl(Belegungsstatus.Nachrueckangebot), Anzahl(Belegungsstatus.Warteliste), k.Veranstaltungen.Count,
                k.Veranstaltungen.Count(v => v.Ausgefallen), Math.Round(findet.Sum(v => (decimal)(v.Ende - v.Beginn).TotalMinutes) / 45m, 2),
                Math.Round(findet.Where(v => v.Ende <= jetzt).Sum(v => (decimal)(v.Ende - v.Beginn).TotalMinutes) / 45m, 2),
                a is { Gesamt: > 0 } ? Math.Round(100m * a.Anwesend / a.Gesamt, 1) : null, umsatz.GetValueOrDefault(k.Id), k.ErsterTermin);
        }).ToList();
    }

    private async Task<Dictionary<Guid, long>> UmsatzJeKursAsync(CancellationToken abbruch)
    {
        var belege = await BelegeAsync(abbruch);
        return belege.SelectMany(b => b.Positionen.Where(p => p.KursId is not null).Select(p => (Kurs: p.KursId!.Value, Cent: Vorzeichen(b, belege) * p.BetragCent)))
            .GroupBy(x => x.Kurs).ToDictionary(g => g.Key, g => g.Sum(x => x.Cent));
    }

    private async Task<List<Beleg>> BelegeAsync(CancellationToken abbruch)
        => await db.Set<Beleg>().AsNoTracking().Where(b => b.Nummer != null
                && (b.Art == Belegart.Rechnung || b.Art == Belegart.Gutschrift || b.Art == Belegart.Storno)).ToListAsync(abbruch);

    /// <summary>Rechnung +, Gutschrift −, Storno einer Rechnung −, Storno einer Gutschrift +.</summary>
    private static int Vorzeichen(Beleg b, IReadOnlyList<Beleg> alle) => b.Art switch
    {
        Belegart.Rechnung => 1,
        Belegart.Gutschrift => -1,
        _ => alle.FirstOrDefault(x => x.Id == b.BezugBelegId)?.Art switch
        {
            Belegart.Rechnung => -1,
            Belegart.Gutschrift => 1,
            _ => 0,
        },
    };

    /// <summary>Laufendes Semester zuerst (Vorauswahl im Dashboard), dann die übrigen absteigend.</summary>
    public async Task<IReadOnlyList<(Guid Id, string Name)>> SemesterAsync(CancellationToken abbruch = default)
    {
        var heute = DateOnly.FromDateTime(uhr.GetLocalNow().Date);
        return (await db.Semester.AsNoTracking().ToListAsync(abbruch))
            .OrderByDescending(s => s.Beginn <= heute && s.Ende >= heute).ThenByDescending(s => s.Beginn)
            .Select(s => (s.Id, s.Name)).ToList();
    }

    public async Task<IReadOnlyDictionary<int, string>> FachbereicheAsync(CancellationToken abbruch = default)
        => await db.Fachbereiche.AsNoTracking().ToDictionaryAsync(f => f.Nr, f => f.Name, abbruch);

    public async Task<IReadOnlyList<(DateOnly Datum, int? Fachbereich, long Cent)>> UmsatzAsync(int jahr, CancellationToken abbruch = default)
    {
        var belege = await BelegeAsync(abbruch);
        var kurse = await db.Kurse.AsNoTracking().IgnoreAutoIncludes().Select(k => new { k.Id, k.Fachbereich }).ToDictionaryAsync(k => k.Id, k => k.Fachbereich, abbruch);
        return belege.Where(b => b.Belegdatum.Year == jahr)
            .SelectMany(b => b.Positionen.Select(p => (b.Belegdatum, p.KursId is { } k && kurse.TryGetValue(k, out var fb) ? fb : (int?)null, (long)Vorzeichen(b, belege) * p.BetragCent)))
            .ToList();
    }

    public async Task<IReadOnlyList<Statistikfall>> StatistikfaelleAsync(int jahr, CancellationToken abbruch = default)
    {
        var kurse = (await db.Kurse.AsNoTracking().IgnoreAutoIncludes().Include(k => k.Veranstaltungen)
                .Where(k => k.Status == Kursstatus.Veroeffentlicht).ToListAsync(abbruch))
            .Where(k => k.Veranstaltungen.Any(v => !v.Ausgefallen && v.Beginn.Year == jahr)).ToDictionary(k => k.Id);
        var arten = await db.Kursarten.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, abbruch);
        var ids = kurse.Keys.ToList();
        var faelle = await db.Belegungen.IgnoreAutoIncludes().AsNoTracking()
            .Where(b => ids.Contains(b.KursId) && b.Status == Belegungsstatus.Angemeldet)
            .Join(db.Personen.AsNoTracking().IgnoreAutoIncludes(), b => b.PersonId, p => p.Id, (b, p) => new { b.KursId, b.PersonId, p.AnonymisiertGeburtskohorte })
            .ToListAsync(abbruch);
        var geburtIds = faelle.Select(f => f.PersonId).Distinct().ToList();
        var geburtsdaten = (await db.Set<PersonMerkmal>().AsNoTracking()
                .Where(m => m.Art == MerkmalArt.Geburtsdatum && m.ErsetztAm == null && geburtIds.Contains(EF.Property<Guid>(m, "PersonId")))
                .Select(m => new { PersonId = EF.Property<Guid>(m, "PersonId"), Version = m }).ToListAsync(abbruch))
            .GroupBy(x => x.PersonId).ToDictionary(g => g.Key, g => g.First().Version.AlsDatum());

        // Geschlecht zum Kursdatum aus dem versionierten Merkmal (nicht der heutige Stand); Alter zum Stichtag des Berichtsjahrs (31.12.).
        var personIds = faelle.Select(f => f.PersonId).Distinct().ToList();
        var geschlechter = (await db.Set<PersonMerkmal>().AsNoTracking()
                .Where(m => m.Art == MerkmalArt.Geschlecht && personIds.Contains(EF.Property<Guid>(m, "PersonId")))
                .Select(m => new { PersonId = EF.Property<Guid>(m, "PersonId"), Version = m })
                .ToListAsync(abbruch))
            .ToLookup(x => x.PersonId, x => x.Version);
        var jetzt = uhr.GetLocalNow();
        var stichtag = new DateOnly(jahr, 12, 31);
        return faelle.Select(f =>
        {
            var k = kurse[f.KursId];
            var termine = k.Veranstaltungen.Where(v => !v.Ausgefallen && v.Beginn.Year == jahr).ToList();
            var ue = Math.Round(termine.Sum(v => (decimal)(v.Ende - v.Beginn).TotalMinutes) / 45m, 2);
            var kursdatum = DateOnly.FromDateTime(termine.Min(v => v.Beginn));
            var geschlecht = geschlechter[f.PersonId].AlsStichtag(kursdatum, jetzt)?.Inhalt is { Length: > 0 } g ? g : "nicht erfasst";
            var altersgruppe = geburtsdaten.GetValueOrDefault(f.PersonId) is { } geb
                ? Domain.Datenschutz.Statistik.Altersgruppe(geb, stichtag)
                : Domain.Datenschutz.Statistik.AltersgruppeAusKohorte(f.AnonymisiertGeburtskohorte, stichtag);
            return new Statistikfall(k.Fachbereich, arten.GetValueOrDefault(k.KursartId, ""), ue, geschlecht, altersgruppe, k.Nutzungsart == Nutzungsart.Integrationskurs);
        }).ToList();
    }

    public async Task<(int Offen, int Ueberfaellig)> AufgabenAsync(CancellationToken abbruch = default)
    {
        var offen = await db.Aufgaben.AsNoTracking().Where(a => a.Status == Aufgabenstatus.Offen || a.Status == Aufgabenstatus.InBearbeitung).Select(a => a.Faellig).ToListAsync(abbruch);
        var heute = DateOnly.FromDateTime(uhr.GetLocalNow().Date);
        return (offen.Count, offen.Count(f => f < heute));
    }
}
