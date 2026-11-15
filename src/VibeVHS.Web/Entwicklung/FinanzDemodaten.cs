using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Finanzen.Honorare;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Finanzen: Testkonto „Prüfung“, SEPA-Mandate für Lastschriftzahler, Honorarverträge (beide Modelle) mit Auszahlungskonto,
/// ältere überfällige Rechnungen (für das Mahnwesen), Rechnungslauf, ein exportierter Lastschriftlauf und ein simulierter
/// Kontoauszug (sichere Treffer gebucht, der Rest zum Ausprobieren offen). Läuft einmal (Protokolleintrag "Demodaten.Finanzen").
/// </summary>
internal static class FinanzDemodaten
{
    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var d = bereich.ServiceProvider;
        var db = d.GetRequiredService<VibeVhsDbContext>();
        if (await db.Protokoll.AnyAsync(e => e.Aktion == "Demodaten.Finanzen") || !await db.Belegungen.AnyAsync())
        {
            return;
        }

        var uhr = d.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var finanzen = d.GetRequiredService<FinanzDienst>();
        var notizen = new List<string>();

        if (!await db.Benutzerkonten.AnyAsync(k => k.Login == "pruefung"))
        {
            db.Benutzerkonten.Add(new Benutzerkonto("pruefung", "Betriebsprüfung (Demo)", "externe Prüfung", [Rolle.Pruefung], [], null,
                "Demokonto: liest Finanzdaten und Protokoll, exportiert für die Prüfung, ändert nichts"));
            await db.SaveChangesAsync();
            notizen.Add("Testkonto pruefung");
        }

        await finanzen.EinstellungenAsync();

        // Mandate für alle, die per Lastschrift zahlen (Test-IBANs mit gültiger Prüfziffer)
        var lastschrift = await db.Belegungen.IgnoreAutoIncludes().AsNoTracking().Where(b => b.Zahlart == Zahlart.Lastschrift && b.Status == Belegungsstatus.Angemeldet)
            .Select(b => b.PersonId).Distinct().ToListAsync();
        var nr = 0;
        foreach (var pid in lastschrift)
        {
            if (await db.Personen.FindAsync(pid) is not { IstAnonymisiert: false } p)
            {
                continue;
            }

            await finanzen.MandatAnlegenAsync(pid, p.Anzeigename, Iban.AusBlzUndKonto("40050150", (2000000 + nr++).ToString(System.Globalization.CultureInfo.InvariantCulture)), null,
                heute.AddDays(-30 - nr));
        }

        notizen.Add($"{nr} SEPA-Mandate");

        // Honorarverträge: freiberufliche Kursleitungen mit Terminen, abwechselnd Modell 1 und 2
        var termine = await d.GetRequiredService<IFinanzSpeicher>().DozententermineAsync(heute.AddMonths(-3).ToDateTime(TimeOnly.MinValue), heute.ToDateTime(TimeOnly.MinValue));
        var honorare = d.GetRequiredService<HonorarDienst>();
        var vertraege = 0;
        foreach (var dozentId in termine.Select(t => t.DozentId).Distinct().Take(12))
        {
            if (await db.Personen.FindAsync(dozentId) is not { IstAnonymisiert: false } p || p.BeschaeftigungAm(heute, jetzt) == Beschaeftigungsart.Festangestellt)
            {
                continue;
            }

            if (vertraege % 3 == 2)
            {
                await honorare.VertragSpeichernAsync(dozentId, null, "Rahmenvertrag 2026", "basis-ue-tn", new Dictionary<string, string?>
                {
                    ["basisJeTermin"] = "15", ["satzJeUe"] = "22", ["satzJeTeilnehmer"] = "1.50", ["mindestJeTermin"] = "45", ["hoechstJeTermin"] = "120",
                    ["fahrtkosten"] = "km", ["kmJeTermin"] = "18",
                }, new DateOnly(2026, 1, 1), null);
            }
            else
            {
                await honorare.VertragSpeichernAsync(dozentId, null, "Rahmenvertrag 2026", "pro-ue", new Dictionary<string, string?>
                {
                    ["satzJeUe"] = vertraege % 2 == 0 ? "30" : "32.50", ["ausfallProzent"] = "50", ["fahrtkosten"] = vertraege % 2 == 0 ? "pauschal" : "keine", ["fahrtPauschale"] = "6",
                }, new DateOnly(2026, 1, 1), null);
            }

            if (vertraege % 4 != 3)
            {
                await finanzen.BankverbindungSetzenAsync(dozentId, p.Anzeigename, Iban.AusBlzUndKonto("40351060", (3000000 + vertraege).ToString(System.Globalization.CultureInfo.InvariantCulture)), null);
            }

            vertraege++;
        }

        notizen.Add($"{vertraege} Honorarverträge");

        // Ältere Rechnungen (September), damit das Mahnwesen etwas zu tun hat
        var aelter = await db.Belegungen.IgnoreAutoIncludes().AsNoTracking()
            .Where(b => b.Status == Belegungsstatus.Angemeldet && b.Zahlart == Zahlart.Ueberweisung && b.EntgeltCent > 0)
            .OrderBy(b => b.GebuchtAm).ThenBy(b => b.Id).Take(5).ToListAsync();
        foreach (var b in aelter)
        {
            if (await db.Personen.FindAsync(b.PersonId) is not { IstAnonymisiert: false } p || await db.Kurse.IgnoreAutoIncludes().FirstOrDefaultAsync(k => k.Id == b.KursId) is not { } kurs)
            {
                continue;
            }

            var empf = FinanzDienst.Personenempfaenger(p, jetzt);
            var r = new Beleg(Belegart.Rechnung, new DateOnly(2026, 9, 15), p.Id, null, empf.Name, string.Join('\n', empf.Anschrift),
                [new Belegposition($"Kursentgelt {kurs.Kursnummer} {kurs.Titel}", b.EntgeltCent, belegungId: b.Id, kursId: kurs.Id, art: Positionsart.Kursentgelt)],
                jetzt, "Demodaten", new DateOnly(2026, 9, 29));
            db.Add(r);
            await finanzen.FestschreibenAsync(r, empf, false);
        }

        // Rechnungslauf, Lastschrift- und Mahnvorschlag, Aufgaben (ohne Mails, sonst läuft der Postausgang über)
        var routinen = d.GetRequiredService<FinanzRoutinen>();
        var lauf = await routinen.TaeglichAsync(mailen: false);
        notizen.Add(lauf.Rechnungen.ToString());

        // Lastschriftlauf freigeben und exportieren, dann einen Kontoauszug simulieren und sichere Treffer buchen
        var zahlungen = d.GetRequiredService<ZahlungsDienst>();
        if (lauf.Lastschrift is { Status: Zahlungslaufstatus.Vorschlag } ls)
        {
            await zahlungen.FreigebenAsync(ls.Id);
            await zahlungen.ExportierenAsync(ls.Id);
            notizen.Add($"Lastschriftlauf mit {ls.Aktiv.Count()} Posten exportiert");
        }

        var (datei, inhalt) = await zahlungen.DemoAuszugAsync();
        var auszug = await zahlungen.EinlesenAsync(datei, inhalt);
        var gebucht = await zahlungen.SichereZuordnenAsync(auszug.Auszug.Id);
        notizen.Add($"Kontoauszug: {auszug.Umsaetze} Umsätze, {gebucht} sicher gebucht");

        // Honorarentwürfe für Oktober
        var honorarlauf = await honorare.AbrechnungenErstellenAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        notizen.Add($"{honorarlauf.Entwuerfe} Honorarentwürfe Oktober");
        await routinen.AufgabenAbgleichenAsync();

        await d.GetRequiredService<IAuditProtokoll>().ProtokollierenAsync("Demodaten.Finanzen", "Finanz-Demodaten angelegt: " + string.Join("; ", notizen));
        await db.SaveChangesAsync();
    }
}
