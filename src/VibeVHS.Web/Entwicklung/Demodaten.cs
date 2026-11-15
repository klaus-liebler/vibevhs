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

    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (await db.Protokoll.AnyAsync(e => e.Aktion == "Demodaten.Angelegt"))
        {
            return;
        }

        var mez = TimeSpan.FromHours(1);
        var mesz = TimeSpan.FromHours(2);
        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);

        // Namensänderung durch Heirat (mit vorheriger Tippfehler-Korrektur).
        var anna = Person.Anlegen("Anna", "Schmitt", new DateOnly(2019, 3, 1), new DateTimeOffset(2019, 3, 1, 9, 0, 0, mez),
            new DateOnly(1986, 8, 12), "anna.becker@example.org", von: Von);
        anna.NameKorrigieren("Anna", "Schmidt", new DateOnly(2019, 3, 1), "Tippfehler bei Ersterfassung",
            new DateTimeOffset(2019, 3, 4, 10, 0, 0, mez), Von);
        anna.NameAendernAb("Anna", "Becker", new DateOnly(2025, 6, 14), "Heirat, Nachweis: Heiratsurkunde",
            new DateTimeOffset(2025, 7, 2, 14, 30, 0, mesz), Von);
        anna.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(2019, 3, 1), Erfassung.Erst(new DateTimeOffset(2019, 3, 1, 9, 0, 0, mez), Von));

        // Umzug und abweichende Korrespondenzadresse (c/o).
        var start = new DateTimeOffset(2025, 3, 1, 10, 0, 0, mez);
        var olena = Person.Anlegen("Olena", "Kovalenko", new DateOnly(2025, 3, 1), start, new DateOnly(1991, 1, 21),
            "olena.kovalenko@example.org", "uk", Von);
        olena.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2025, 3, 1), Erfassung.Erst(start, Von));
        var wohnung1 = olena.AdresseHinzufuegen(null, "Lindenstraße 14", "48282", "Emsdetten", new DateOnly(2025, 3, 1), Erfassung.Erst(start, Von));
        olena.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, wohnung1, new DateOnly(2025, 3, 1), Erfassung.Erst(start, Von));
        var handy = olena.RufnummerHinzufuegen(RufnummerArt.Mobil, "+49 151 00000001", Messagingdienst.Anruf | Messagingdienst.Sms | Messagingdienst.WhatsApp,
            true, true, new DateOnly(2025, 3, 1), Erfassung.Erst(start, Von, "Einwilligung schriftlich bei Anmeldung"));

        var coErfasst = new DateTimeOffset(2026, 6, 3, 11, 15, 0, mesz);
        var co = olena.AdresseHinzufuegen("c/o Flüchtlingsberatung", "Kirchplatz 2", "48268", "Greven", new DateOnly(2026, 6, 1),
            Erfassung.Aenderung(coErfasst, "Post soll über die Beratungsstelle laufen (Wunsch der Teilnehmerin)", Von));
        olena.AdressKennzeichnenAb(AdressKennzeichenArt.Korrespondenzadresse, co, new DateOnly(2026, 6, 1),
            Erfassung.Aenderung(coErfasst, "Post soll über die Beratungsstelle laufen (Wunsch der Teilnehmerin)", Von));

        olena.UmzugAb(wohnung1, null, "Grevener Damm 120", "48282", "Emsdetten", new DateOnly(2026, 8, 1),
            Erfassung.Aenderung(new DateTimeOffset(2026, 8, 10, 9, 30, 0, mesz), "Umzug, Nachweis: Meldebescheinigung", Von));
        olena.RufnummerAendernAb(handy, RufnummerArt.Mobil, "+49 151 00000001",
            Messagingdienst.Anruf | Messagingdienst.Sms | Messagingdienst.WhatsApp | Messagingdienst.Signal, true, true, new DateOnly(2026, 9, 1),
            Erfassung.Aenderung(new DateTimeOffset(2026, 9, 1, 14, 0, 0, mesz), "Einwilligung für Signal erteilt", Von));

        var mehmet = Person.Anlegen("Mehmet", "Yılmaz", heute, jetzt, new DateOnly(1988, 5, 3), "mehmet.yilmaz@example.org", "tr", Von);
        mehmet.RolleHinzufuegen(Personenrolle.Teilnehmende, heute, Erfassung.Erst(jetzt, Von));
        var weber = Person.Anlegen("Sabine", "Weber", heute, jetzt, new DateOnly(1972, 11, 14), "sabine.weber@example.org", von: Von);
        weber.RolleHinzufuegen(Personenrolle.Teilnehmende, heute, Erfassung.Erst(jetzt, Von));
        var nadia = Person.Anlegen("Nadia", "Haddad", heute, jetzt, new DateOnly(1993, 10, 9), null, "ar", Von);
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
