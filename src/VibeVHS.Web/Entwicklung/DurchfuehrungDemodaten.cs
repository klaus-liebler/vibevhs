using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Durchführung: Anwesenheiten vergangener Termine (abgeschlossene Listen), ein Testkonto mit der Rolle „Dozierende“,
/// ein Frühjahrskurs 2025 mit Anna Schmidt (heute Becker) als Kursleitung und Olena Kovalenko als Teilnehmerin samt
/// archivierter Teilnahmebescheinigung, eine Zwischenbescheinigung für Mehmet Yılmaz und ein Bildungsurlaubskurs.
/// Läuft einmal nach (Protokolleintrag "Demodaten.Durchfuehrung").
/// </summary>
internal static class DurchfuehrungDemodaten
{
    private const string Von = "Demodaten";

    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (await db.Protokoll.AnyAsync(e => e.Aktion == "Demodaten.Durchfuehrung") || !await db.Belegungen.AnyAsync())
        {
            return;
        }

        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var zufall = new Random(2207);
        var notizen = new List<string>();

        // Kursleitung als Testkonto (Rolle Dozierende)
        var pilates = await db.Kurse.Include(k => k.Veranstaltungen).FirstOrDefaultAsync(k => k.Kursnummer == "26H-704");
        if (pilates?.Leitung is { } leitung && !await db.Benutzerkonten.AnyAsync(k => k.PersonId == leitung.PersonId))
        {
            var login = Login(leitung.Name);
            if (!await db.Benutzerkonten.AnyAsync(k => k.Login == login))
            {
                db.Benutzerkonten.Add(new Benutzerkonto(login, leitung.Name, "Kursleitung (Demo, freiberuflich)", [Rolle.Dozierende], [7, 8], leitung.PersonId,
                    "Demokonto: sieht und erfasst die Anwesenheit eigener Veranstaltungen"));
                notizen.Add($"Testkonto {login} (Dozierende)");
            }
        }

        // Bildungsurlaub
        if (await db.Kurse.FirstOrDefaultAsync(k => k.Kursnummer == "26H-602") is { } bu)
        {
            bu.AlsBildungsurlaub("AWbG NRW, Anerkennung Nr. 2026/4711 (fiktiv)");
        }

        // Anwesenheit vergangener Termine (nicht Kontingent): ca. 85 % anwesend, abgeschlossene Listen
        var kurse = await db.Kurse.Include(k => k.Veranstaltungen).Where(k => k.Status != Kursstatus.Entwurf).ToListAsync();

        // Integrationskurse: abrechnungsrelevante Anwesenheit → Selbst-Check-in nur mit Bestätigung durch die Kursleitung
        foreach (var ik in kurse.Where(k => k.Nutzungsart == Nutzungsart.Integrationskurs && k.Richtlinie.SelbstCheckin == SelbstCheckin.Erlaubt))
        {
            ik.MitRichtlinie(ik.Richtlinie with { SelbstCheckin = SelbstCheckin.MitBestaetigung });
        }
        var belegungen = await db.Belegungen.Where(b => b.Status == Belegungsstatus.Angemeldet).ToListAsync();
        var anzahl = 0;
        foreach (var kurs in kurse)
        {
            var eigene = belegungen.Where(b => b.KursId == kurs.Id).ToList();
            if (eigene.Count == 0)
            {
                continue;
            }

            var leiter = kurs.Leitung?.Name ?? "Kursleitung";
            foreach (var v in kurs.Veranstaltungen.Where(v => !v.Ausgefallen && v.Ende < jetzt.LocalDateTime).OrderBy(v => v.Beginn))
            {
                var dauer = (int)(v.Ende - v.Beginn).TotalMinutes;
                var erfasst = new DateTimeOffset(v.Beginn.AddMinutes(10), jetzt.Offset);
                foreach (var b in eigene.Where(b => b.GebuchtAm < erfasst))
                {
                    if (b.IstKontingent)
                    {
                        var e = b.Ereignisse.FirstOrDefault(x => x.VeranstaltungId == v.Id && x.Art is Kontingentereignisart.Besucht or Kontingentereignisart.NichtErschienen);
                        if (e is not null)
                        {
                            db.Add(new Anwesenheit(b, v, v.Orte.FirstOrDefault()?.OrtId, e.Art == Kontingentereignisart.Besucht ? Anwesenheitsstatus.Anwesend : Anwesenheitsstatus.Abwesend,
                                e.Art == Kontingentereignisart.Besucht ? dauer : 0, e.Art == Kontingentereignisart.Besucht ? Erfassungsweg.RaumQr : Erfassungsweg.Automatik,
                                e.Art == Kontingentereignisart.Besucht ? "Selbst-Check-in (Raum-QR)" : "Automatik", e.Zeitpunkt));
                            anzahl++;
                        }

                        continue;
                    }

                    var w = zufall.NextDouble();
                    var status = w < 0.85 ? Anwesenheitsstatus.Anwesend : w < 0.9 ? Anwesenheitsstatus.Entschuldigt : Anwesenheitsstatus.Abwesend;
                    var minuten = status == Anwesenheitsstatus.Anwesend ? (zufall.Next(10) == 0 ? Math.Max(15, dauer - 30) : dauer) : 0;
                    db.Add(new Anwesenheit(b, v, v.Orte.FirstOrDefault()?.OrtId, status, minuten, zufall.Next(3) == 0 ? Erfassungsweg.RaumQr : Erfassungsweg.Liste,
                        $"{leiter} (Dozierende)", erfasst, minuten < dauer && status == Anwesenheitsstatus.Anwesend ? "später gekommen" : null));
                    anzahl++;
                }

                db.Add(new Listenabschluss(v.Id, kurs.Id, new DateTimeOffset(v.Ende.AddMinutes(5), jetzt.Offset), $"{leiter} (Dozierende)"));
            }
        }

        notizen.Add($"{anzahl} Anwesenheiten");

        // Frühjahr 2025: Anna Schmidt (heute Becker) leitet, Olena Kovalenko nimmt teil
        var anna = await db.Personen.Where(p => p.Anzeigename == "Anna Becker").OrderBy(p => p.Sortierschluessel).FirstOrDefaultAsync();
        var olena = await db.Personen.Where(p => p.Anzeigename == "Olena Kovalenko").OrderBy(p => p.Sortierschluessel).FirstOrDefaultAsync();
        var raum = await db.Orte.FirstOrDefaultAsync(o => o.Kurzname == "A 1.12");
        var sprachkurs = await db.Kursarten.FirstOrDefaultAsync(k => k.Name == "Sprachkurs");
        Guid? olenaBelegung = null;
        if (anna is not null && olena is not null && raum is not null && sprachkurs is not null && !await db.Kurse.AnyAsync(k => k.Kursnummer == "25F-501"))
        {
            var f25 = await db.Semester.FirstOrDefaultAsync(s => s.Kuerzel == "25F") ?? new Semester("Frühjahr 2025", "25F", new DateOnly(2025, 2, 1), new DateOnly(2025, 7, 31));
            if (db.Entry(f25).State == EntityState.Detached)
            {
                db.Semester.Add(f25);
            }

            var e = Erfassung.Erst(new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.FromHours(1)), Von);
            var kurs = new Kurs("25F-501", "Englisch A1 – Einstieg", f25, 5, sprachkurs, 6, 14).MitTeam([new KursDozent(anna.Id, "Anna Schmidt", true)]);
            kurs.EntgeltSetzenAb(Geld.Euro(92), Geld.Euro(69), f25.Beginn, e);
            kurs.SerieFestlegen(new Serienregel
            {
                Start = new DateOnly(2025, 3, 11), Anzahl = 10, Wochentage = [DayOfWeek.Tuesday], Beginn = new TimeOnly(18, 0), DauerMinuten = 90, OrtIds = [raum.Id],
            }, new Kalender(await db.Ferien.AsNoTracking().ToListAsync()), _ => raum.Anzeige, new DateOnly(2025, 12, 31));
            kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
            db.Kurse.Add(kurs);

            var teilnehmende = new List<Guid> { olena.Id };
            teilnehmende.AddRange(await db.Personen.IgnoreAutoIncludes().Where(p => (p.Rollen & Domain.Personen.Personenrolle.Teilnehmende) != 0 && p.Id != olena.Id
                                                                             && p.AngelegtAm < new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero))
                .OrderBy(p => p.Sortierschluessel).Select(p => p.Id).Take(7).ToListAsync());
            var tresen = new Buchender("Katja Friesen", "katja.friesen", "Teilnehmerservice");
            foreach (var pid in teilnehmende)
            {
                var b = Belegung.Anlegen(kurs, pid, false, Buchungsweg.Tresen, tresen, null, Zahlart.Lastschrift, new DateTimeOffset(2025, 3, 3, 10, 0, 0, TimeSpan.FromHours(1)));
                db.Belegungen.Add(b);
                if (pid == olena.Id)
                {
                    olenaBelegung = b.Id;
                }

                foreach (var v in kurs.Veranstaltungen.Where(v => !v.Ausgefallen))
                {
                    var da = pid == olena.Id ? v.Nummer != 4 : zufall.NextDouble() < 0.85;
                    db.Add(new Anwesenheit(b, v, raum.Id, da ? Anwesenheitsstatus.Anwesend : Anwesenheitsstatus.Abwesend, da ? 90 : 0, Erfassungsweg.Liste,
                        "Anna Schmidt (Dozierende)", new DateTimeOffset(v.Beginn.AddMinutes(10), TimeSpan.FromHours(1))));
                }
            }

            foreach (var v in kurs.Veranstaltungen)
            {
                db.Add(new Listenabschluss(v.Id, kurs.Id, new DateTimeOffset(v.Ende.AddMinutes(5), TimeSpan.FromHours(1)), "Anna Schmidt (Dozierende)"));
            }

            notizen.Add("Kurs 25F-501 mit Anna Schmidt und Olena Kovalenko");
        }

        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, "Demodaten.Durchfuehrung", null, null,
            $"Durchführungs-Demodaten angelegt: {string.Join("; ", notizen)}"));
        await db.SaveChangesAsync();

        // Archivierte Bescheinigungen (über den Dokumentdienst, damit PDF, Datenstand und Hash entstehen)
        var dokumente = bereich.ServiceProvider.GetRequiredService<DokumentDienst>();
        if (olenaBelegung is { } ob)
        {
            await dokumente.BescheinigungErstellenAsync(ob, Domain.Dokumente.Dokumentart.Teilnahmebescheinigung);
        }

        var mehmet = await db.Personen.AsNoTracking().Where(p => p.Anzeigename == "Mehmet Yılmaz" && p.AngelegtAm > jetzt.AddDays(-30)).OrderBy(p => p.Sortierschluessel).FirstOrDefaultAsync();
        var englisch = await db.Kurse.AsNoTracking().FirstOrDefaultAsync(k => k.Kursnummer == "26H-501");
        if (mehmet is not null && englisch is not null
            && await db.Belegungen.AsNoTracking().FirstOrDefaultAsync(b => b.PersonId == mehmet.Id && b.KursId == englisch.Id) is { } mb
            && await db.Set<Anwesenheit>().AnyAsync(a => a.BelegungId == mb.Id && a.Status == Anwesenheitsstatus.Anwesend))
        {
            await dokumente.BescheinigungErstellenAsync(mb.Id, Domain.Dokumente.Dokumentart.Teilnahmebescheinigung);
        }
    }

    private static string Login(string name)
    {
        var normal = name.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normal.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            sb.Append(char.IsLetter(c) ? c : c == ' ' ? '.' : '-');
        }

        return sb.ToString().Trim('.', '-').Replace("dr..", "", StringComparison.Ordinal);
    }
}
