using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Beschäftigungsart und Qualifikationen der Demo-Dozierenden: wenige festangestellt, die meisten freiberuflich;
/// die Integrationskurs-Lehrkräfte haben eine BAMF-Zulassung (eine läuft in 6 Wochen ab), eine Deutsch-Lehrkraft hat keine.
/// Läuft auch für bestehende Datenbanken einmal nach (Protokolleintrag "Demodaten.Qualifikationen").
/// </summary>
internal static class QualifikationsDemodaten
{
    private const string Von = "Demodaten";

    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (await db.Protokoll.AnyAsync(e => e.Aktion == "Demodaten.Qualifikationen") || !await db.Kurse.AnyAsync())
        {
            return;
        }

        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var z = await Demozeit.LadenAsync(db, uhr);
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var e = Erfassung.Erst(jetzt, Von);
        var katalog = await db.Merkmale.AsNoTracking().ToDictionaryAsync(m => m.Schluessel);
        if (!katalog.ContainsKey("bamf-lk"))
        {
            return;
        }

        var dozierende = await db.Personen
            .Where(p => (p.Rollen & Personenrolle.Dozierende) == Personenrolle.Dozierende && p.Status != Personenstatus.Zusammengefuehrt
                        && p.Datenschutz == VibeVHS.Domain.Datenschutz.Datenschutzstatus.Aktiv)
            .OrderBy(p => p.Sortierschluessel).ToListAsync();
        var zufall = new Random(815);

        // Beschäftigungsart: etwa jede fünfte Person festangestellt, Anna Becker wechselt 2026 in die Festanstellung.
        foreach (var p in dozierende)
        {
            var ab = DateOnly.FromDateTime(p.AngelegtAm.Date);
            var erfasst = Erfassung.Erst(p.AngelegtAm, Von); // bei der Ersterfassung der Person bekannt
            if (p.Anzeigename == "Anna Becker")
            {
                p.BeschaeftigungAendernAb(Beschaeftigungsart.Freiberuflich, ab, erfasst);
                p.BeschaeftigungAendernAb(Beschaeftigungsart.Festangestellt, z.D(2026, 1, 1),
                    Erfassung.Aenderung(z.DTO(2025, 12, 10, 10, 0), $"Arbeitsvertrag vom {z.D(2025, 12, 8):dd.MM.yyyy}", Von));
                continue;
            }

            p.BeschaeftigungAendernAb(zufall.Next(5) == 0 ? Beschaeftigungsart.Festangestellt : Beschaeftigungsart.Freiberuflich, ab, erfasst);
        }

        // Integrationskurs-Lehrkräfte
        var ikKurse = (await db.Kurse.AsNoTracking().Where(k => k.ArchiviertAm == null).ToListAsync())
            .Where(k => k.BenoetigteDozierendenmerkmale.Contains("bamf-lk")).OrderBy(k => k.Kursnummer).ToList();
        var mitZulassung = new HashSet<Guid>();
        var notizen = new List<string>();
        foreach (var kurs in ikKurse)
        {
            if (kurs.Leitung is not { } leitung || dozierende.FirstOrDefault(d => d.Id == leitung.PersonId) is not { } p || !mitZulassung.Add(p.Id))
            {
                continue;
            }

            var alpha = kurs.BenoetigteDozierendenmerkmale.Contains("alpha");
            var bis = alpha ? heute.AddDays(42) : z.D(2028, 12, 31);
            p.QualifikationSetzenAb(katalog["bamf-lk"], alpha ? "mit Auflagen" : "direkt", $"Zulassungsbescheid BAMF Az. 81-{4711 + mitZulassung.Count}/{2023 + mitZulassung.Count}",
                "Bundesamt für Migration und Flüchtlinge", z.D(2023, 1, 1), bis, e);
            if (alpha)
            {
                p.QualifikationSetzenAb(katalog["alpha"], null, "Zertifikat Zusatzqualifizierung Alphabetisierung (fiktiv)", "Goethe-Institut", z.D(2022, 6, 1), null, e);
            }

            notizen.Add($"{p.Anzeigename} ({kurs.Kursnummer}, BAMF bis {bis:dd.MM.yyyy})");
        }

        // Anschlusskurs, der über den Ablauf der Zulassung hinausläuft: Ablauf mitten in der Serie wird sichtbar markiert.
        var ablaufend = ikKurse.FirstOrDefault(k => k.BenoetigteDozierendenmerkmale.Contains("alpha"))?.Leitung;
        var art = await db.Kursarten.Where(k => k.Nutzungsart == VibeVHS.Domain.Programm.Nutzungsart.Integrationskurs).OrderBy(k => k.Name).ThenBy(k => k.Id).FirstOrDefaultAsync();
        var raum = await db.Orte.FirstOrDefaultAsync(o => o.Kurzname == "A 1.10");
        var semester = (await db.Semester.ToListAsync()).Where(s => s.Beginn <= heute && heute <= s.Ende).FirstOrDefault();
        if (ablaufend is not null && art is not null && raum is not null && semester is not null && !await db.Kurse.AnyAsync(k => k.Kursnummer == semester.Kuerzel + "-405"))
        {
            var kalender = new VibeVHS.Domain.Programm.Kalender(await db.Ferien.AsNoTracking().ToListAsync());
            var start = heute.AddDays(28);
            while (start.DayOfWeek != DayOfWeek.Monday)
            {
                start = start.AddDays(1);
            }

            var modul4 = new VibeVHS.Domain.Programm.Kurs($"{semester.Kuerzel}-405", "Integrationskurs allgemein, Modul 4", semester, 4, art, 10, 20)
                .MitTeam([ablaufend with { Leitung = true }])
                .MitBeschreibung("Anschlusskurs an Modul 3 (Demodaten: Die BAMF-Zulassung der Lehrkraft läuft mitten in der Serie ab).");
            modul4.EntgeltSetzenAb(new Geld(0), null, semester.Beginn, e);
            modul4.SerieFestlegen(new VibeVHS.Domain.Programm.Serienregel
            {
                Start = start, Anzahl = 24, Wochentage = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
                Beginn = new TimeOnly(8, 30), DauerMinuten = 240, OrtIds = [raum.Id], FerienAuslassen = true,
            }, kalender, _ => raum.Anzeige, heute.AddDays(12 * 7));
            modul4.StatusSetzen(VibeVHS.Domain.Programm.Kursstatus.Veroeffentlicht);
            db.Kurse.Add(modul4);
            notizen.Add($"{modul4.Kursnummer} mit {ablaufend.Name} ab {start:dd.MM.yyyy}");
        }

        // Deutsch-Lehrkraft ohne BAMF-Zulassung (Sperre sichtbar), aber mit telc-Prüferlizenz
        var ohne = dozierende.FirstOrDefault(d => d.Fachbereiche.Contains(4) && !mitZulassung.Contains(d.Id));
        if (ohne is not null)
        {
            ohne.QualifikationSetzenAb(katalog["telc-pruefer"], null, "Lizenz-Nr. T-2024-0815 (fiktiv)", "telc gGmbH", z.D(2024, 3, 1), z.D(2027, 2, 28), e);
            notizen.Add($"{ohne.Anzeigename} ohne BAMF-Zulassung");
        }

        // Weitere Beispiele: Übungsleiterlizenz und Erste Hilfe für Bewegung, Führungszeugnis
        foreach (var p in dozierende.Where(d => d.Fachbereiche.Contains(7) || d.Fachbereiche.Contains(8)).Take(6))
        {
            p.QualifikationSetzenAb(katalog["uebungsleiter"], null, $"Lizenz Nr. {zufall.Next(10000, 99999)} (fiktiv)", "LandesSportBund NRW", z.D(2023, 1, 1),
                z.D(new DateOnly(2026 + zufall.Next(1, 3), 12, 31)), e);
            p.QualifikationSetzenAb(katalog["erste-hilfe"], null, "Bescheinigung Erste-Hilfe-Kurs (fiktiv)", "DRK Kreisverband Steinfurt", z.D(2025, 2, 1),
                z.D(2027, 1, 31), e);
        }

        if (dozierende.FirstOrDefault(d => d.Fachbereiche.Contains(3) && !mitZulassung.Contains(d.Id)) is { } fz)
        {
            fz.QualifikationSetzenAb(katalog["fuehrungszeugnis"], null, $"Erweitertes Führungszeugnis vom {z.D(2026, 1, 15):dd.MM.yyyy} (Einsicht vermerkt)", "Stadt Emsdetten, Bürgerbüro",
                z.D(2026, 1, 15), z.D(2031, 1, 14), e);
        }

        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, "Demodaten.Qualifikationen", null, null,
            $"Demo-Qualifikationen und Beschäftigungsarten für {dozierende.Count} Dozierende angelegt: {string.Join("; ", notizen)}"));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Lehrfreigaben für Sprachkurse: Sprachkurse verlangen die Freigabe für ihre Sprache, ihre Kursleitungen bekommen sie –
    /// bis auf eine Lehrkraft, die nur für eine andere Sprache freigegeben ist (Sperre mit Begründung sichtbar).
    /// Läuft auch für bestehende Datenbanken einmal nach (Protokolleintrag "Demodaten.Sprachfreigaben").
    /// </summary>
    public static async Task SprachfreigabenAnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (await db.Protokoll.AnyAsync(e => e.Aktion == "Demodaten.Sprachfreigaben") || !await db.Kurse.AnyAsync())
        {
            return;
        }

        var katalog = await db.Merkmale.AsNoTracking().ToDictionaryAsync(m => m.Schluessel);
        if (!katalog.TryGetValue(Sprachfreigabe.Schluessel, out var freigabe))
        {
            return;
        }

        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var z = await Demozeit.LadenAsync(db, uhr);
        var e = Erfassung.Erst(jetzt, Von);

        // Kursart „Sprachkurs“ verlangt die Freigabe; die Kurse bestimmen ihre Sprache aus dem Titel.
        foreach (var art in (await db.Kursarten.ToListAsync()).Where(a => a.Name.Contains("Sprach", StringComparison.OrdinalIgnoreCase)
                                                                          && a.Nutzungsart != VibeVHS.Domain.Programm.Nutzungsart.Integrationskurs))
        {
            if (!art.BenoetigteDozierendenmerkmale.Contains(Sprachfreigabe.Schluessel))
            {
                art.MitDozierendenmerkmalen(art.BenoetigteDozierendenmerkmale.Append(Sprachfreigabe.Schluessel));
            }

            foreach (var kurs in await db.Kurse.Where(k => k.KursartId == art.Id && k.ArchiviertAm == null).ToListAsync())
            {
                var basis = kurs.BenoetigteDozierendenmerkmale.Any(a => Anforderung.Basis(a) == Sprachfreigabe.Schluessel)
                    ? kurs.BenoetigteDozierendenmerkmale
                    : kurs.BenoetigteDozierendenmerkmale.Append(Sprachfreigabe.Schluessel).ToList();
                kurs.MitDozierendenmerkmalen(Sprachfreigabe.Konkretisieren(basis, kurs.Titel));
            }
        }

        await db.SaveChangesAsync();

        var kurse = (await db.Kurse.AsNoTracking().Where(k => k.ArchiviertAm == null).ToListAsync())
            .Select(k => (Kurs: k, Sprache: k.BenoetigteDozierendenmerkmale.Select(Anforderung.Zerlegen).FirstOrDefault(a => a.Schluessel == Sprachfreigabe.Schluessel).Wert))
            .Where(x => x.Sprache is not null && x.Kurs.Leitung is not null).OrderBy(x => x.Kurs.Kursnummer).ToList();
        var sprachenJePerson = kurse.GroupBy(x => x.Kurs.Leitung!.PersonId).ToDictionary(g => g.Key, g => g.Select(x => x.Sprache!).Distinct().ToList());
        var notizen = new List<string>();

        // Eine Lehrkraft ist nur für eine andere Sprache freigegeben (z. B. Italienisch statt Spanisch).
        var ohne = kurse.FirstOrDefault(x => x.Sprache == "Spanisch").Kurs?.Leitung?.PersonId ?? kurse.LastOrDefault().Kurs?.Leitung?.PersonId;
        var nr = 0;
        foreach (var (personId, sprachen) in sprachenJePerson)
        {
            var p = await db.Personen.FirstOrDefaultAsync(x => x.Id == personId);
            if (p is null)
            {
                continue;
            }

            var werte = personId == ohne ? sprachen.Select(s => s == "Italienisch" ? "Französisch" : "Italienisch").Distinct().ToList() : sprachen;
            p.QualifikationSetzenAb(freigabe, string.Join(", ", werte), $"Hospitation und Gespräch mit der Fachbereichsleitung, Protokoll FB5-{2024}-{++nr:000} (fiktiv)",
                "vhs Emsdetten-Greven-Saerbeck", z.D(2024, 8, 1), null, e);
            if (werte.Contains(Sprachfreigabe.DeutschWert) && katalog.TryGetValue("daf-daz", out var daf))
            {
                p.QualifikationSetzenAb(daf, null, "Zertifikat Zusatzqualifikation DaF/DaZ (fiktiv)", "Goethe-Institut", z.D(2021, 3, 1), null, e);
            }

            notizen.Add($"{p.Anzeigename}: {string.Join(", ", werte)}{(personId == ohne ? " (Absicht: nicht passend)" : "")}");
        }

        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, "Demodaten.Sprachfreigaben", null, null,
            $"Lehrfreigaben für Sprachkurse angelegt: {string.Join("; ", notizen)}"));
        await db.SaveChangesAsync();
    }
}
