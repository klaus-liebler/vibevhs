using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Legt einmalig fiktive Beispielpersonen an (alle Adressen und Nummern sind erfunden).
/// Größere Testdatenmengen erzeugt tools/Testdaten.
/// </summary>
internal static class Demodaten
{
    private const string Von = "Demodaten";

    /// <summary>
    /// Alle Demodaten in fester Reihenfolge, relativ zum Tagesdatum (<see cref="Demozeit"/>). Nachgereichte Demodaten dürfen den Start
    /// einer bestehenden Instanz nie verhindern; ihre Fehler werden nur protokolliert.
    /// </summary>
    public static async Task AllesAnlegenAsync(IServiceProvider dienste, int massendaten, ILogger log)
    {
        await AnlegenAsync(dienste);
        await MassendatenAnlegenAsync(dienste, massendaten);
        // Nach dem Massenladen Planerstatistik sofort aktualisieren (autovacuum käme erst später): sonst plant PostgreSQL wie für leere Tabellen.
        await DatenbankInitialisierung.StatistikAktualisierenAsync(dienste);
        await ProgrammDemodaten.AnlegenAsync(dienste);
        foreach (var (name, anlegen) in new (string, Func<IServiceProvider, Task>)[]
                 {
                     ("Anmeldungen", AnmeldungDemodaten.AnlegenAsync),
                     ("Qualifikationen", QualifikationsDemodaten.AnlegenAsync),
                     ("Sprachfreigaben", QualifikationsDemodaten.SprachfreigabenAnlegenAsync),
                     ("Merkmal-Typen", MerkmalDemodaten.AnlegenAsync),
                     ("Teilnehmendenportal", TeilnehmendenDemodaten.AnlegenAsync),
                     ("Durchführung", DurchfuehrungDemodaten.AnlegenAsync),
                     ("Integration", IntegrationDemodaten.AnlegenAsync),
                     ("Finanzen", FinanzDemodaten.AnlegenAsync),
                 })
        {
            try
            {
                await anlegen(dienste);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbUpdateException)
            {
                log.LogWarning(ex, "Demodaten {Name} konnten nicht angelegt werden", name);
            }
        }
    }

    /// <summary>Große Testdatenmenge (fester Startwert), einmalig, wenn <c>Demodaten:Massendaten</c> &gt; 0.</summary>
    public static async Task MassendatenAnlegenAsync(IServiceProvider dienste, int anzahl)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (anzahl <= 0 || await db.Protokoll.AnyAsync(e => e.Aktion == "Testdaten.Erzeugt"))
        {
            return;
        }

        var log = bereich.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Demodaten));
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        var daten = new VibeVHS.Adapters.Mocks.Testdaten.Massendaten().Erzeugen(anzahl,
            bereich.ServiceProvider.GetRequiredService<TimeProvider>().GetLocalNow());
        db.Organisationen.AddRange(daten.Organisationen);
        db.Personen.AddRange(daten.Personen);
        await bereich.ServiceProvider.GetRequiredService<IAuditProtokoll>().ProtokollierenAsync("Testdaten.Erzeugt",
            $"Entwicklungsumgebung: {daten.Personen.Count} fiktive Testpersonen ({daten.GeplanteDubletten} geplante Dubletten) und {daten.Organisationen.Count} Organisationen angelegt");
        await db.SaveChangesAsync();
        log.LogInformation("Massendaten angelegt: {Anzahl} Personen in {Dauer} ms", daten.Personen.Count, uhr.ElapsedMilliseconds);
    }

    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (await db.Protokoll.AnyAsync(e => e.Aktion == "Demodaten.Angelegt"))
        {
            return;
        }

        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var z = await Demozeit.LadenAsync(db, uhr);

        // Namensänderung durch Heirat (mit vorheriger Tippfehler-Korrektur).
        var anna = Person.Anlegen("Anna", "Schmitt", z.D(2019, 3, 1), z.DTO(2019, 3, 1, 9, 0),
            z.D(1986, 8, 12), "anna.becker@example.org", von: Von);
        anna.NameKorrigieren("Anna", "Schmidt", z.D(2019, 3, 1), "Tippfehler bei Ersterfassung",
            z.DTO(2019, 3, 4, 10, 0), Von);
        anna.NameAendernAb("Anna", "Becker", z.D(2025, 6, 14), "Heirat, Nachweis: Heiratsurkunde",
            z.DTO(2025, 7, 2, 14, 30), Von);
        anna.RolleHinzufuegen(Personenrolle.Dozierende, z.D(2019, 3, 1), Erfassung.Erst(z.DTO(2019, 3, 1, 9, 0), Von));

        // Umzug und abweichende Korrespondenzadresse (c/o).
        var start = z.DTO(2025, 3, 1, 10, 0);
        var olena = Person.Anlegen("Olena", "Kovalenko", z.D(2025, 3, 1), start, z.D(1991, 1, 21),
            "olena.kovalenko@example.org", "uk", Von);
        olena.RolleHinzufuegen(Personenrolle.Teilnehmende, z.D(2025, 3, 1), Erfassung.Erst(start, Von));
        var wohnung1 = olena.AdresseHinzufuegen(null, "Lindenstraße 14", "48282", "Emsdetten", z.D(2025, 3, 1), Erfassung.Erst(start, Von));
        olena.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, wohnung1, z.D(2025, 3, 1), Erfassung.Erst(start, Von));
        var handy = olena.RufnummerHinzufuegen(RufnummerArt.Mobil, "+49 151 00000001", Messagingdienst.Anruf | Messagingdienst.Sms | Messagingdienst.WhatsApp,
            true, true, z.D(2025, 3, 1), Erfassung.Erst(start, Von, "Einwilligung schriftlich bei Anmeldung"));

        var coErfasst = z.DTO(2026, 6, 3, 11, 15);
        var co = olena.AdresseHinzufuegen("c/o Flüchtlingsberatung", "Kirchplatz 2", "48268", "Greven", z.D(2026, 6, 1),
            Erfassung.Aenderung(coErfasst, "Post soll über die Beratungsstelle laufen (Wunsch der Teilnehmerin)", Von));
        olena.AdressKennzeichnenAb(AdressKennzeichenArt.Korrespondenzadresse, co, z.D(2026, 6, 1),
            Erfassung.Aenderung(coErfasst, "Post soll über die Beratungsstelle laufen (Wunsch der Teilnehmerin)", Von));

        olena.UmzugAb(wohnung1, null, "Grevener Damm 120", "48282", "Emsdetten", z.D(2026, 8, 1),
            Erfassung.Aenderung(z.DTO(2026, 8, 10, 9, 30), "Umzug, Nachweis: Meldebescheinigung", Von));
        olena.RufnummerAendernAb(handy, RufnummerArt.Mobil, "+49 151 00000001",
            Messagingdienst.Anruf | Messagingdienst.Sms | Messagingdienst.WhatsApp | Messagingdienst.Signal, true, true, z.D(2026, 9, 1),
            Erfassung.Aenderung(z.DTO(2026, 9, 1, 14, 0), "Einwilligung für Signal erteilt", Von));

        var mehmet = Person.Anlegen("Mehmet", "Yılmaz", heute, jetzt, z.D(1988, 5, 3), "mehmet.yilmaz@example.org", "tr", Von);
        mehmet.RolleHinzufuegen(Personenrolle.Teilnehmende, heute, Erfassung.Erst(jetzt, Von));
        var weber = Person.Anlegen("Sabine", "Weber", heute, jetzt, z.D(1972, 11, 14), "sabine.weber@example.org", von: Von);
        weber.RolleHinzufuegen(Personenrolle.Teilnehmende, heute, Erfassung.Erst(jetzt, Von));
        var nadia = Person.Anlegen("Nadia", "Haddad", heute, jetzt, z.D(1993, 10, 9), null, "ar", Von);
        nadia.RolleHinzufuegen(Personenrolle.Teilnehmende, heute, Erfassung.Erst(jetzt, Von));

        db.Personen.AddRange(anna, olena, mehmet, weber, nadia);
        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, "Demodaten.Angelegt",
            null, null, "Entwicklungsumgebung: 5 fiktive Beispielpersonen angelegt (u. a. Umzug mit c/o-Adresse, Heirat)"));
        await db.SaveChangesAsync();

        var postausgang = bereich.ServiceProvider.GetRequiredService<PostausgangDienst>();
        await postausgang.KundenmailEinreihenAsync(mehmet.EMail!, "Mehmet Yılmaz",
            "Erinnerung: Ihr Kurs „Englisch A2 – Konversation“ geht am Dienstag weiter",
            "Guten Tag Herr Yılmaz,\n\nIhr Kurs findet am Dienstag, 18:00–19:30 Uhr in Raum R 2.14 statt.\n" +
            "Bitte checken Sie im Raum mit dem QR-Code ein.\n\nIhre Volkshochschule",
            mehmet.Sprache, mehmet.Id);
        await bereich.ServiceProvider.GetRequiredService<INachrichtenSpeicher>().SpeichernAsync();
    }
}
