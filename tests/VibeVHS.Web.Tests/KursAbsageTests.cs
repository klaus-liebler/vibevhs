using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Sicherheit;
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>Kurs absagen von der Kursseite: nur mit Grund, Belegungen aufheben, Teilnehmende benachrichtigen – kein stiller Statuswechsel.</summary>
public sealed class KursAbsageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Benutzer Fbl = new("fbl", "Fritz FBL", Rolle.Fachbereichsleitung, [Rolle.Fachbereichsleitung], [8]);

    private static readonly Benutzer Tresen = new("tina.tresen", "Tina Tresen", Rolle.Teilnehmerservice, [Rolle.Teilnehmerservice]);

    private static async Task<T> MitAsync<T>(ShopTestHost h, Benutzer wer, Func<IServiceProvider, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider);
    }

    private static async Task<Kurs> KursMitAnmeldungenAsync(ShopTestHost h, string nummer, params string?[] mails)
    {
        var kurs = await h.KursAsync(nummer, 10);
        var i = 0;
        foreach (var mail in mails)
        {
            await using (var bereich = h.App.Services.CreateAsyncScope())
            {
                var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
                var jetzt = h.Uhr.GetLocalNow();
                var p = Person.Anlegen($"Person{++i}", "Absage", new DateOnly(1990, 1, i), jetzt.AddYears(-1), null, mail);
                p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
                db.Personen.Add(p);
                await db.SaveChangesAsync(Ct);
                await MitAsync(h, Benutzer.Automatik, d => d.GetRequiredService<AnmeldungDienst>().AnmeldenAsync(new AnmeldeAuftrag(p.Id, kurs.Id, Zahlart.Ueberweisung), Ct));
            }
        }

        return kurs;
    }

    [Fact]
    public async Task Absage_mit_Grund_hebt_Belegungen_auf_benachrichtigt_und_laesst_Termine_ausfallen()
    {
        await using var h = await StartenAsync();
        var kurs = await KursMitAnmeldungenAsync(h, "26H-971", "a@example.org", "b@example.org");

        var n = await MitAsync(h, Fbl, d => d.GetRequiredService<AnmeldungDienst>().KursAbsagenAsync(kurs.Id, "Kursleitung erkrankt", Ct));

        n.ShouldBe(2);
        await using var bereich = h.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var k = await db.Kurse.Include(x => x.Veranstaltungen).SingleAsync(x => x.Id == kurs.Id, Ct);
        k.Status.ShouldBe(Kursstatus.Abgesagt);
        k.Veranstaltungen.Where(v => v.Beginn >= h.Uhr.GetLocalNow().DateTime).ShouldAllBe(v => v.Ausgefallen);
        (await db.Belegungen.Where(b => b.KursId == kurs.Id).ToListAsync(Ct)).ShouldAllBe(b => !b.Offen);
        var absagen = (await db.Postausgang.ToListAsync(Ct)).Where(m => m.Betreff.StartsWith("Absage", StringComparison.Ordinal)).ToList();
        absagen.Select(m => m.Empfaenger).Order().ShouldBe(["a@example.org", "b@example.org"]);
        absagen.ShouldAllBe(m => m.Inhalt.Contains("Kursleitung erkrankt") && m.Uebersetzungslink != null);
    }

    [Fact]
    public async Task Ohne_Grund_oder_ohne_Recht_wird_nicht_abgesagt()
    {
        await using var h = await StartenAsync();
        var kurs = await KursMitAnmeldungenAsync(h, "26H-972", "c@example.org");

        await Should.ThrowAsync<ArgumentException>(() => MitAsync(h, Fbl, d => d.GetRequiredService<AnmeldungDienst>().KursAbsagenAsync(kurs.Id, "  ", Ct)));
        (await Should.ThrowAsync<InvalidOperationException>(() => MitAsync(h, Tresen, d => d.GetRequiredService<AnmeldungDienst>().KursAbsagenAsync(kurs.Id, "Grund", Ct))))
            .Message.ShouldContain("Nur für die Rolle");

        await using var bereich = h.App.Services.CreateAsyncScope();
        (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Kurse.SingleAsync(x => x.Id == kurs.Id, Ct)).Status.ShouldNotBe(Kursstatus.Abgesagt);
    }

    [Fact]
    public async Task Stiller_Statuswechsel_auf_abgesagt_ist_gesperrt()
    {
        await using var h = await StartenAsync();
        var kurs = await KursMitAnmeldungenAsync(h, "26H-973", "d@example.org");

        var fehler = await Should.ThrowAsync<InvalidOperationException>(() =>
            MitAsync(h, Fbl, async d => { await d.GetRequiredService<ProgrammDienst>().StatusSetzenAsync(kurs.Id, Kursstatus.Abgesagt, Ct); return 0; }));

        fehler.Message.ShouldContain("mit Grund");
    }
}
