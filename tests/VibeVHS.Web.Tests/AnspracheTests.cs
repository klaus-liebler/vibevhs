using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Sicherheit;

namespace VibeVHS.Web.Tests;

/// <summary>Persönliche Ansprache (Du/Sie, förmlich/freundlich): an der eigenen Person gespeichert, Begrüßung, Vorrang vor der Vorlage in Mails.</summary>
public sealed class AnspracheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<T> AlsAsync<T>(ShopTestHost h, Benutzer wer, Func<IServiceProvider, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider);
    }

    [Fact]
    public async Task Ansprache_wird_an_der_Person_gespeichert_und_gilt_fuer_Begruessung_und_Mails()
    {
        await using var h = await ShopTestHost.StartenAsync();
        var kurs = await h.KursAsync("26H-971", 10);
        var jetzt = h.Uhr.GetLocalNow();
        var kim = Person.Anlegen("Kim", "Muster", DateOnly.FromDateTime(jetzt.Date).AddYears(-1), jetzt.AddYears(-1), new DateOnly(1990, 1, 1), "kim@example.org");
        kim.TitelSetzen("Dr.");
        kim.AngabeAendern(MerkmalArt.Anrede, "Frau", DateOnly.FromDateTime(jetzt.Date).AddYears(-1), Erfassung.Erst(jetzt), korrektur: true);
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            db.Personen.Add(kim);
            db.Benutzerkonten.Add(new Benutzerkonto("kim.muster", "Kim Muster", null, [Rolle.Teilnehmende], [], kim.Id));
            db.Benutzerkonten.Add(new Benutzerkonto("ohne.person", "Otto Ohne", null, [Rolle.Teilnehmerservice], [], null));
            await db.SaveChangesAsync(Ct);
        }

        var ich = new Benutzer("kim.muster", "Kim Muster", Rolle.Teilnehmende);
        var vorher = await AlsAsync(h, ich, d => d.GetRequiredService<AnspracheDienst>().MeineAsync(Ct));
        (vorher.Gesetzt, vorher.Begruessung).ShouldBe((false, "Sehr geehrte Frau Dr. Muster"));

        await AlsAsync(h, ich, async d => { await d.GetRequiredService<AnspracheDienst>().SetzenAsync(new Ansprache(true, Anredestil.Informell), Ct); return 0; });
        var nachher = await AlsAsync(h, ich, d => d.GetRequiredService<AnspracheDienst>().MeineAsync(Ct));
        (nachher.Gesetzt, nachher.Begruessung, nachher.Ansprache).ShouldBe((true, "Hallo Kim", new Ansprache(true, Anredestil.Informell)));

        // Mail aus einer (förmlichen) Vorlage: die persönliche Einstellung geht vor.
        var tresen = new Benutzer("tina.tresen", "Tina Tresen", Rolle.Teilnehmerservice, [Rolle.Teilnehmerservice]);
        var e = await AlsAsync(h, tresen, d => d.GetRequiredService<AnmeldungDienst>().AnmeldenAsync(new AnmeldeAuftrag(kim.Id, kurs.Id, Zahlart.Ueberweisung), Ct));
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            (await db.Postausgang.SingleAsync(n => n.Id == e.NachrichtId, Ct)).Inhalt.ShouldStartWith("Hallo Kim,");
            (await db.Protokoll.CountAsync(p => p.Aktion == "Person.Ansprache", Ct)).ShouldBe(1);
        }

        // Ohne verknüpfte Person: Standard, Speichern mit verständlichem Grund abgelehnt.
        var otto = new Benutzer("ohne.person", "Otto Ohne", Rolle.Teilnehmerservice);
        (await AlsAsync(h, otto, d => d.GetRequiredService<AnspracheDienst>().MeineAsync(Ct))).PersonId.ShouldBeNull();
        (await Should.ThrowAsync<InvalidOperationException>(() =>
            AlsAsync(h, otto, async d => { await d.GetRequiredService<AnspracheDienst>().SetzenAsync(Ansprache.Standard, Ct); return 0; })))
            .Message.ShouldContain("mit keiner Person verknüpft");
    }
}
