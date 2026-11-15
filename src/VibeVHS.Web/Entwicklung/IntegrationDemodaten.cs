using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Integration;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Integration;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Integrationskurse: BAMF-Kursnummern für 26H-401/402, Berechtigungen (teils kostenbefreit), Ausweisprüfungen (je Kurs
/// bewusst zwei offen, damit die Sperre des Kursstarts sichtbar ist), Platzhalter-Lichtbilder, eine vorläufige Anwesenheit
/// nach „Zweifel“ (Aufgabe Ausweiskontrolle) und ein DTZ-Termin. Läuft einmal (Protokolleintrag "Demodaten.Integration").
/// </summary>
internal static class IntegrationDemodaten
{
    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var d = bereich.ServiceProvider;
        var db = d.GetRequiredService<VibeVhsDbContext>();
        if (await db.Protokoll.AnyAsync(e => e.Aktion == "Demodaten.Integration"))
        {
            return;
        }

        var integration = d.GetRequiredService<IntegrationDienst>();
        var qr = d.GetRequiredService<IQrCode>();
        var notizen = new List<string>();
        var nr = 0;
        foreach (var (kursnummer, bamf, art) in new[] { ("26H-401", "IK-2026-11-0401", Integrationskursart.Allgemein), ("26H-402", "IK-2026-10-0402", Integrationskursart.Alphabetisierung) })
        {
            if (await db.Kurse.IgnoreAutoIncludes().FirstOrDefaultAsync(k => k.Kursnummer == kursnummer) is not { } kurs)
            {
                continue;
            }

            await integration.KursEinrichtenAsync(kurs.Id, bamf, art);
            var belegungen = await db.Belegungen.IgnoreAutoIncludes().AsNoTracking().Where(b => b.KursId == kurs.Id && b.Status == Belegungsstatus.Angemeldet).ToListAsync();
            for (var i = 0; i < belegungen.Count; i++)
            {
                var pid = belegungen[i].PersonId;
                var befreit = i % 3 != 1;
                await integration.BerechtigungErfassenAsync(pid, i % 2 == 0 ? Berechtigungsart.Berechtigungsschein : Berechtigungsart.Verpflichtung, $"TN-{4711000 + nr++}",
                    i % 2 == 0 ? "BAMF" : "Jobcenter Kreis Steinfurt", new DateOnly(2026, 8, 1).AddDays(i), new DateOnly(2027, 8, 1), art == Integrationskursart.Allgemein ? 700 : 1000,
                    befreit, befreit ? "Bezug von Bürgergeld" : null, befreit && i % 2 == 0);
                if (i < belegungen.Count - 2)
                {
                    await integration.IdentitaetPruefenAsync(pid, i % 4 == 0 ? Ausweisart.Reisepass : Ausweisart.Aufenthaltstitel, true, null);
                }

                if (i < 3)
                {
                    await integration.LichtbildSpeichernAsync(pid, kurs.Id, qr.Png($"Lichtbild (Demo) {i + 1}"), "image/png", informiert: true);
                }
            }

            notizen.Add($"{kursnummer}: {belegungen.Count} Berechtigungen");
        }

        // Eine vergangene Anwesenheit mit „Zweifel“ beim Sichtvergleich → vorläufig, Aufgabe Ausweiskontrolle
        if (await db.Kurse.IgnoreAutoIncludes().FirstOrDefaultAsync(k => k.Kursnummer == "26H-401") is { } k401
            && await db.Set<Anwesenheit>().Where(a => a.KursId == k401.Id && a.Status == Anwesenheitsstatus.Anwesend).OrderByDescending(a => a.Datum).FirstOrDefaultAsync() is { } a)
        {
            a.VorlaeufigSetzen(IntegrationDienst.GrundZweifel);
            await db.SaveChangesAsync();
            notizen.Add("1 vorläufige Anwesenheit (Zweifel)");
        }

        if (await db.Kurse.IgnoreAutoIncludes().FirstOrDefaultAsync(k => k.Kursnummer == "26H-401") is { } kurs401)
        {
            await integration.PruefungAnlegenAsync(Pruefungsart.Dtz, new DateTime(2027, 2, 20, 9, 0, 0), "vhs-Haus Emsdetten, A 1.10", kurs401.Id);
        }

        await d.GetRequiredService<IntegrationRoutinen>().AufgabenAbgleichenAsync();
        await d.GetRequiredService<IAuditProtokoll>().ProtokollierenAsync("Demodaten.Integration", "Integrationskurs-Demodaten angelegt: " + string.Join("; ", notizen));
        await db.SaveChangesAsync();
    }
}
