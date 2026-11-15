using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Legt in der Entwicklungsumgebung einige Beispielpersonen an, wenn die Datenbank leer ist.
/// Größere Testdatenmengen erzeugt tools/Testdaten.
/// </summary>
internal static class Demodaten
{
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

        // Das Beispiel aus dem Konzept: Tippfehler, Korrektur, Heirat.
        var anna = Person.Anlegen("Anna", "Schmitt", new DateOnly(2019, 3, 1), new DateTimeOffset(2019, 3, 1, 9, 0, 0, mez),
            new DateOnly(1986, 8, 12), "anna.becker@example.org");
        anna.NameKorrigieren("Anna", "Schmidt", new DateOnly(2019, 3, 1), "Tippfehler bei Ersterfassung",
            new DateTimeOffset(2019, 3, 4, 10, 0, 0, mez));
        anna.NameAendernAb("Anna", "Becker", new DateOnly(2025, 6, 14), "Heirat, Nachweis: Heiratsurkunde",
            new DateTimeOffset(2025, 7, 2, 14, 30, 0, mesz));

        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var mehmet = Person.Anlegen("Mehmet", "Yılmaz", heute, jetzt, new DateOnly(1988, 5, 3), "mehmet.yilmaz@example.org", "tr");
        db.Personen.AddRange(
            anna,
            mehmet,
            Person.Anlegen("Olena", "Kovalenko", heute, jetzt, new DateOnly(1991, 1, 21), "olena.kovalenko@example.org", "uk"),
            Person.Anlegen("Sabine", "Weber", heute, jetzt, new DateOnly(1972, 11, 14), "sabine.weber@example.org"),
            Person.Anlegen("Nadia", "Haddad", heute, jetzt, new DateOnly(1993, 10, 9), null, "ar"));

        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, "Demodaten.Angelegt",
            null, null, "Entwicklungsumgebung: 5 Beispielpersonen angelegt"));
        await db.SaveChangesAsync();

        var postausgang = bereich.ServiceProvider.GetRequiredService<PostausgangDienst>();
        await postausgang.KundenmailEinreihenAsync(mehmet.EMail!, "Mehmet Yılmaz",
            "Erinnerung: Ihr Kurs „Englisch A2 – Konversation“ geht am Dienstag weiter",
            "Guten Tag Herr Yılmaz,\n\nIhr Kurs findet am Dienstag, 18:00–19:30 Uhr in Raum R 2.14 statt.\n" +
            "Bitte checken Sie im Raum mit dem QR-Code ein.\n\nIhre VHS Musterstadt",
            mehmet.Sprache, mehmet.Id);
        await bereich.ServiceProvider.GetRequiredService<INachrichtenSpeicher>().SpeichernAsync();
    }
}
