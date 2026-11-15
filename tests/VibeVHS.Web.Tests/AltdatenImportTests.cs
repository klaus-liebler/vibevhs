using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Betrieb;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Sicherheit;
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>Altdatenimport: Personen → Kurse → Belegungen, Zuordnung über Altnummer und Kursnummer, Probelauf ohne Schreiben.</summary>
public sealed class AltdatenImportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Benutzer Verwaltung = new("vl", "Vera Leitung", Rolle.Verwaltungsleitung, [Rolle.Verwaltungsleitung]);

    private static ImportDatei Datei(string name, string inhalt) => ImportDienst.Lesen(name, Encoding.UTF8.GetBytes(inhalt));

    private static async Task<T> AlsAsync<T>(ShopTestHost h, Benutzer wer, Func<IServiceProvider, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider);
    }

    [Fact]
    public async Task Personen_Kurse_und_Belegungen_nacheinander_mit_Zuordnung_ueber_Altnummern()
    {
        await using var h = await StartenAsync();
        var vorhanden = await h.KursAsync("26H-501", 14); // legt Semester „26H“, Ort „B 1.01“ und Kursart „Art 26H-501“ an

        // 1. Personen
        var personen = Datei("personen.csv", "TN-Nr;Name;Vorname;GebDatum\n100231;Kowalski;Jan;03.04.1978\n100232;Yılmaz;Elif;17.11.1990\n100238;Becker;Lukas;14.08.2001\n");
        await AlsAsync(h, Verwaltung, async d =>
        {
            var dienst = d.GetRequiredService<ImportDienst>();
            return await dienst.ImportierenAsync(personen, ImportDienst.ZuordnungVorschlagen(personen.Spalten).ToDictionary(), Ct);
        });

        // 2. Kurse
        var kurse = Datei("kurse.csv", "KursNr;Titel;Semester;FB;Kursart;Min;Max;Entgelt;Beginn;Uhrzeit;Termine;Ort;Status\n"
                                       + "K-9001;Ukrainisch für Einsteiger;26H;5;Art 26H-501;1;2;89,00;12.01.2027;18:00-19:30;10;B 1.01;veröffentlicht\n"
                                       + "K-9001;doppelt;26H;5;;1;2;89;12.01.2027;;1;;\n"
                                       + "K-9004;Semester unbekannt;30X;5;;1;2;89;12.01.2027;;1;;\n"
                                       + "K-9005;Ort unbekannt;26H;5;;1;2;89;12.01.2027;;1;Z 9.99;\n"
                                       + "26H-501;gibt es schon;26H;5;;1;2;89;12.01.2027;;1;;\n");
        var zuKurse = AltdatenImportDienst.ZuordnungVorschlagen(Altdatenart.Kurse, kurse.Spalten).ToDictionary();
        var probe = await AlsAsync(h, Verwaltung, d => d.GetRequiredService<AltdatenImportDienst>().ProbelaufAsync(Altdatenart.Kurse, kurse, zuKurse, Ct));
        probe.Neu.ShouldBe(1);
        probe.Dubletten.ShouldBe(2);
        probe.Fehler.ShouldBe(2);
        probe.Zeilen.Single(z => z.Wert("Kursnummer") == "K-9005").Hinweis.ShouldContain("Ort „Z 9.99“ unbekannt");
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Kurse.CountAsync(Ct)).ShouldBe(1); // Probelauf schreibt nichts
        }

        var ergebnis = await AlsAsync(h, Verwaltung, d => d.GetRequiredService<AltdatenImportDienst>().ImportierenAsync(Altdatenart.Kurse, kurse, zuKurse, Ct));
        ergebnis.Angelegt.ShouldBe(1);
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var k = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Kurse.Include(x => x.Veranstaltungen).SingleAsync(x => x.Kursnummer == "K-9001", Ct);
            k.Veranstaltungen.Count.ShouldBe(10);
            k.Veranstaltungen.Min(v => v.Beginn).ShouldBe(new DateTime(2027, 1, 12, 18, 0, 0));
            k.Status.ShouldBe(Kursstatus.Veroeffentlicht);
            k.Entgelte.Single().Cent.ShouldBe(8900);
        }

        // 3. Belegungen: Person über TN-Nr, Kurs über Kursnummer; keine Mails
        var belegungen = Datei("belegungen.csv", "TN-Nr;KursNr;Status;Anmeldedatum;Zahlart\n"
                                                + "100231;K-9001;angemeldet;02.12.2026;Lastschrift\n"
                                                + "100232;K-9001;angemeldet;03.12.2026;Rechnung\n"
                                                + "100238;K-9001;warteliste;;Bar\n"
                                                + "100231;K-9001;angemeldet;;\n"
                                                + "999999;K-9001;angemeldet;;\n"
                                                + "100232;K-0000;angemeldet;;\n"
                                                + "100232;26H-501;storniert;;\n");
        var zuBel = AltdatenImportDienst.ZuordnungVorschlagen(Altdatenart.Belegungen, belegungen.Spalten).ToDictionary();
        var pb = await AlsAsync(h, Verwaltung, d => d.GetRequiredService<AltdatenImportDienst>().ProbelaufAsync(Altdatenart.Belegungen, belegungen, zuBel, Ct));
        pb.Neu.ShouldBe(4, string.Join(" | ", pb.Zeilen.Select(z => $"{z.Nr}:{z.Status}:{z.Hinweis}")));
        pb.Dubletten.ShouldBe(1);
        pb.Fehler.ShouldBe(2);
        pb.Zeilen.Single(z => z.Wert("TeilnehmerAltnummer") == "999999").Hinweis.ShouldContain("nicht importiert");
        var mailsVorher = await AlsAsync(h, Verwaltung, async d => await d.GetRequiredService<VibeVhsDbContext>().Postausgang.CountAsync(Ct));
        (await AlsAsync(h, Verwaltung, d => d.GetRequiredService<AltdatenImportDienst>().ImportierenAsync(Altdatenart.Belegungen, belegungen, zuBel, Ct))).Angelegt.ShouldBe(4);
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var bs = await db.Belegungen.ToListAsync(Ct);
            bs.Count.ShouldBe(4);
            bs.Count(b => b.Kursnummer == "K-9001" && b.Status == Belegungsstatus.Angemeldet).ShouldBe(2);
            bs.Single(b => b.Status == Belegungsstatus.Warteliste).Zahlart.ShouldBe(Zahlart.Bar);
            bs.Single(b => b.KursId == vorhanden.Id).Status.ShouldBe(Belegungsstatus.Storniert);
            bs.Single(b => b.Zahlart == Zahlart.Lastschrift).EntgeltCent.ShouldBe(8900);
            (await db.Postausgang.CountAsync(Ct)).ShouldBe(mailsVorher);
            (await db.Set<VibeVHS.Domain.Personen.Importlauf>().Select(l => l.Art).ToListAsync(Ct)).Order().ShouldBe(["Belegungen", "Kurse", "Personen"]);
        }

        // Gleiche Datei nochmal: abgelehnt
        (await Should.ThrowAsync<InvalidOperationException>(() =>
            AlsAsync(h, Verwaltung, d => d.GetRequiredService<AltdatenImportDienst>().ImportierenAsync(Altdatenart.Belegungen, belegungen, zuBel, Ct)))).Message.ShouldContain("bereits");
        // Ohne Recht
        (await Should.ThrowAsync<InvalidOperationException>(() => AlsAsync(h, new Benutzer("b", "B", Rolle.Buchhaltung),
            d => d.GetRequiredService<AltdatenImportDienst>().ProbelaufAsync(Altdatenart.Kurse, kurse, zuKurse, Ct)))).Message.ShouldContain("Verwaltungsleitung");
    }
}
