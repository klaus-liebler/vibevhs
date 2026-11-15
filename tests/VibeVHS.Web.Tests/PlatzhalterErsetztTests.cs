using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Datenschutz;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Dokumente;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Sicherheit;
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>Frühere Platzhalter-Sperren: Benutzerverwaltung, „Teilnehmende informieren“ nach Ausfall, Antwortbrief zum Löschverlangen.</summary>
public sealed class PlatzhalterErsetztTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Benutzer Admin = new("amy.admin", "Amy Admin", Rolle.Systemverwaltung, [Rolle.Systemverwaltung]);

    private static readonly Benutzer Tresen = new("tina.tresen", "Tina Tresen", Rolle.Teilnehmerservice, [Rolle.Teilnehmerservice]);

    private static async Task<T> MitAsync<T>(ShopTestHost h, Benutzer wer, Func<IServiceProvider, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider);
    }

    private static async Task OhneErgebnisAsync(ShopTestHost h, Benutzer wer, Func<Benutzerverwaltung, Task> arbeit)
        => await MitAsync(h, wer, async d =>
        {
            await arbeit(d.GetRequiredService<Benutzerverwaltung>());
            return 0;
        });

    private static async Task<Guid> PersonAsync(ShopTestHost h, string vorname, string? mail, bool mitAdresse = false)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var jetzt = h.Uhr.GetLocalNow();
        var p = Person.Anlegen(vorname, "Muster", new DateOnly(1990, 1, 1), jetzt.AddYears(-1), null, mail);
        p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
        if (mitAdresse)
        {
            p.AdresseHinzufuegen(null, "Lindenstraße 14", "48282", "Emsdetten", new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
        }

        db.Personen.Add(p);
        await db.SaveChangesAsync(Ct);
        return p.Id;
    }

    [Fact]
    public async Task Benutzerverwaltung_legt_an_aendert_und_schuetzt_die_letzte_Systemverwaltung()
    {
        await using var h = await StartenAsync();
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            db.Benutzerkonten.Add(new Benutzerkonto("amy.admin", "Amy Admin", null, [Rolle.Systemverwaltung], [], null));
            db.Benutzerkonten.Add(new Benutzerkonto("tn.portal", "Portal Person", null, [Rolle.Teilnehmende], [], null));
            await db.SaveChangesAsync(Ct);
        }

        var neu = await MitAsync(h, Admin, d => d.GetRequiredService<Benutzerverwaltung>()
            .AnlegenAsync(new KontoEingabe("Fiona.Fb", "Fiona Fachbereich", "FBL Sprachen", [Rolle.Fachbereichsleitung], [5]), Ct));
        neu.Login.ShouldBe("fiona.fb");
        var konten = await MitAsync(h, Admin, d => d.GetRequiredService<Benutzerverwaltung>().KontenAsync(Ct));
        konten.Select(k => k.Login).ShouldBe(["amy.admin", "fiona.fb"], ignoreOrder: true); // Portal-Konten erscheinen nicht

        await MitAsync(h, Admin, async d =>
        {
            await d.GetRequiredService<Benutzerverwaltung>().AendernAsync(neu.Id, new KontoEingabe("fiona.fb", "Fiona Fachbereich", null, [Rolle.Fachbereichsleitung, Rolle.Leitung], [4, 5]), Ct);
            return 0;
        });
        var admin = konten.Single(k => k.Login == "amy.admin");
        (await Should.ThrowAsync<InvalidOperationException>(() => OhneErgebnisAsync(h, Admin,
            v => v.AendernAsync(admin.Id, new KontoEingabe("amy.admin", "Amy Admin", null, [Rolle.Leitung], []), Ct))))
            .Message.ShouldContain("nicht selbst entziehen");
        (await Should.ThrowAsync<InvalidOperationException>(() => OhneErgebnisAsync(h, Admin, v => v.AktivSetzenAsync(admin.Id, false, Ct))))
            .Message.ShouldContain("eigene Konto");
        (await Should.ThrowAsync<InvalidOperationException>(() => MitAsync(h, Admin, d => d.GetRequiredService<Benutzerverwaltung>()
            .AnlegenAsync(new KontoEingabe("fiona.fb", "Doppelt", null, [Rolle.Leitung], []), Ct)))).Message.ShouldContain("vergeben");
        (await Should.ThrowAsync<ArgumentException>(() => MitAsync(h, Admin, d => d.GetRequiredService<Benutzerverwaltung>()
            .AnlegenAsync(new KontoEingabe("neu.tn", "TN", null, [Rolle.Teilnehmende], []), Ct)))).Message.ShouldContain("Portalrollen");
        (await Should.ThrowAsync<InvalidOperationException>(() => OhneErgebnisAsync(h, Tresen,
            v => v.AnlegenAsync(new KontoEingabe("x.y", "X", null, [Rolle.Leitung], []), Ct))))
            .Message.ShouldContain("Systemverwaltung");

        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var k = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Benutzerkonten.SingleAsync(x => x.Login == "fiona.fb", Ct);
            k.Rollen.ShouldBe([Rolle.Fachbereichsleitung, Rolle.Leitung]);
            k.Fachbereiche.ShouldBe([4, 5]);
        }
    }

    [Fact]
    public async Task Nach_Ausfall_informiert_die_Verwaltung_die_Teilnehmenden_ueber_den_Postausgang()
    {
        await using var h = await StartenAsync();
        var kurs = await h.KursAsync("26H-981", 10);
        var mitMail = await PersonAsync(h, "Mia", "mia@example.org");
        var ohneMail = await PersonAsync(h, "Otto", null);
        foreach (var p in new[] { mitMail, ohneMail })
        {
            await MitAsync(h, Benutzer.Automatik, d => d.GetRequiredService<AnmeldungDienst>().AnmeldenAsync(new AnmeldeAuftrag(p, kurs.Id, Zahlart.Ueberweisung), Ct));
        }

        var termin = kurs.Veranstaltungen.OrderBy(v => v.Beginn).First();
        await MitAsync(h, new Benutzer("fbl", "Fritz FBL", Rolle.Fachbereichsleitung, [Rolle.Fachbereichsleitung], [8]), async d =>
        {
            await d.GetRequiredService<ProgrammDienst>().AusfallenLassenAsync(kurs.Id, termin.Id, "Krankheit der Kursleitung", Ct);
            return 0;
        });
        var vorschlag = await MitAsync(h, Tresen, async d =>
        {
            var k = await d.GetRequiredService<IProgrammSpeicher>().KursLadenAsync(kurs.Id, Ct);
            return Terminnachricht.AusfallText(k!, k!.Veranstaltungen.Single(v => v.Id == termin.Id));
        });
        vorschlag.ShouldContain("fällt leider aus");
        vorschlag.ShouldContain("Krankheit der Kursleitung");

        var vorher = await MitAsync(h, Tresen, async d => await d.GetRequiredService<VibeVhsDbContext>().Postausgang.CountAsync(Ct));
        var (versandt, ohne) = await MitAsync(h, Tresen, d => d.GetRequiredService<Terminnachricht>().SendenAsync(kurs.Id, termin.Id, "Termin fällt aus", vorschlag, Ct));
        versandt.ShouldBe(1);
        ohne.ShouldBe(1);
        await using var bereich = h.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        (await db.Postausgang.CountAsync(Ct)).ShouldBe(vorher + 1);
        var mail = (await db.Postausgang.ToListAsync(Ct)).Single(n => n.Betreff.Contains("Termin fällt aus", StringComparison.Ordinal));
        mail.Empfaenger.ShouldBe("mia@example.org");
    }

    [Fact]
    public async Task Loeschverlangen_ohne_Mail_ergibt_Antwortbrief_und_Archiv_ohne_Anschrift()
    {
        await using var h = await StartenAsync();
        var p = await PersonAsync(h, "Lena", null, mitAdresse: true);
        var ergebnis = await MitAsync(h, new Benutzer("vl", "Vera Leitung", Rolle.Verwaltungsleitung, [Rolle.Verwaltungsleitung]),
            d => d.GetRequiredService<DatenschutzDienst>().LoeschverlangenAsync(p, Ct));
        ergebnis.AntwortEingereiht.ShouldBeFalse();
        ergebnis.Antwortbrief.ShouldNotBeNull();
        System.Text.Encoding.ASCII.GetString(ergebnis.Antwortbrief!, 0, 5).ShouldBe("%PDF-");

        await using var bereich = h.App.Services.CreateAsyncScope();
        var dok = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Set<Dokument>().SingleAsync(d => d.Art == Dokumentart.Datenschutzantwort, Ct);
        dok.PersonId.ShouldBe(Guid.Empty);
        dok.Klartext.ShouldNotContain("Lindenstraße");
        dok.Klartext.ShouldNotContain("Lena");
        dok.Klartext.ShouldContain("gelöscht");
    }
}
