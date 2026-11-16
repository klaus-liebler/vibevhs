using Microsoft.EntityFrameworkCore;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Termine werden als Ortszeit geplant („dienstags 18:00“) und je Termin nach UTC gespeichert: über die Zeitumstellung (letzter Sonntag im
/// März bzw. Oktober) ändert sich die UTC-Zeit, die Ortszeit bleibt 18:00.
/// </summary>
public sealed class ZeitumstellungTests
{
    [Fact]
    public async Task Serie_bleibt_ueber_beide_Umstellungen_um_18_Uhr_Ortszeit()
    {
        var ct = TestContext.Current.CancellationToken;
        Zeitzone.Festlegen("Europe/Berlin");
        await using var t = await TestDatenbank.ErzeugenAsync();
        var semester = new Semester("Jahr", "26J", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var art = new Kursart("Sprachkurs", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var kurs = new Kurs("26J-001", "Abendkurs", semester, 5, art, 1, 10);
        // Dienstage: 24.03. (MEZ), 31.03. (MESZ, nach 29.03.), 20.10. (MESZ), 27.10. (MEZ, nach 25.10.)
        foreach (var tag in new[] { new DateOnly(2026, 3, 24), new DateOnly(2026, 3, 31), new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 27) })
        {
            kurs.EinzelveranstaltungHinzufuegen(tag.ToDateTime(new TimeOnly(18, 0)), tag.ToDateTime(new TimeOnly(19, 30)), [new VeranstaltungsOrt(Guid.NewGuid())], null);
        }

        await using (var db = t.NeuerKontext())
        {
            db.Semester.Add(semester);
            db.Kursarten.Add(art);
            db.Kurse.Add(kurs);
            await db.SaveChangesAsync(ct);
        }

        await using var pruef = t.NeuerKontext();
        var roh = await pruef.Database.SqlQuery<long>($"SELECT Beginn AS Value FROM Veranstaltungen ORDER BY Beginn").ToListAsync(ct);
        roh.Select(ms => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.Hour).ShouldBe([17, 16, 16, 17]); // UTC
        var geladen = await pruef.Veranstaltungen.AsNoTracking().OrderBy(v => v.Beginn).ToListAsync(ct);
        geladen.ShouldAllBe(v => v.Beginn.Hour == 18 && v.Beginn.Minute == 0 && v.Ende.Hour == 19);

        // Abfrage mit Ortszeit-Parameter wird ebenfalls umgerechnet: Termine ab 31.03. 18:00 Ortszeit.
        var ab = new DateTime(2026, 3, 31, 18, 0, 0);
        (await pruef.Veranstaltungen.CountAsync(v => v.Beginn >= ab, ct)).ShouldBe(3);
    }

    [Fact]
    public void Ortszeit_in_der_Sommerzeitluecke_zaehlt_nach_der_Umstellung()
    {
        Zeitzone.Festlegen("Europe/Berlin");
        Zeitzone.NachUtc(new DateTime(2026, 3, 29, 2, 30, 0)).ShouldBe(new DateTime(2026, 3, 29, 1, 30, 0)); // 03:30 MESZ
    }
}
