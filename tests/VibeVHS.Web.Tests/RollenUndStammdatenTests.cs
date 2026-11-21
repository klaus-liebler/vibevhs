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
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>Rollen an Personen (additiv, zeitlich gültig, Portalzugang, Sperre beim Beenden) und „Stammdaten ändern“.</summary>
public sealed class RollenUndStammdatenTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Benutzer Tresen = new("tina.tresen", "Tina Tresen", Rolle.Teilnehmerservice, [Rolle.Teilnehmerservice]);

    private static async Task<T> MitAsync<T>(ShopTestHost h, Benutzer wer, Func<IServiceProvider, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider);
    }

    private static Task TunAsync(ShopTestHost h, Benutzer wer, Func<IServiceProvider, Task> arbeit)
        => MitAsync(h, wer, async d =>
        {
            await arbeit(d);
            return 0;
        });

    private static async Task<Guid> TeilnehmerinMitPortalkontoAsync(ShopTestHost h)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var jetzt = h.Uhr.GetLocalNow();
        var p = Person.Anlegen("Paula", "Peters", new DateOnly(2024, 1, 1), jetzt.AddYears(-2), new DateOnly(1985, 5, 5), "paula@example.org");
        p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-2)));
        p.AdresseHinzufuegen(null, "Lindenstraße 1", "48282", "Emsdetten", new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-2)));
        db.Personen.Add(p);
        db.Benutzerkonten.Add(new Benutzerkonto("tn-paula", "Paula Peters", "Teilnehmende (Portal)", [Rolle.Teilnehmende], [], p.Id));
        await db.SaveChangesAsync(Ct);
        return p.Id;
    }

    [Fact]
    public async Task Teilnehmerin_wird_auch_Dozentin_und_das_Portal_konto_bekommt_beide_Portale()
    {
        await using var h = await StartenAsync();
        var pid = await TeilnehmerinMitPortalkontoAsync(h);
        var heute = DateOnly.FromDateTime(h.Uhr.GetLocalNow().Date);

        await TunAsync(h, Tresen, d => d.GetRequiredService<PersonenrollenDienst>().RolleHinzufuegenAsync(pid, Personenrolle.Dozierende, heute, "leitet ab sofort den Strickkurs", Ct));
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var p = await db.Personen.SingleAsync(x => x.Id == pid, Ct);
            p.RollenAm(heute, h.Uhr.GetLocalNow()).ShouldBe(Personenrolle.Teilnehmende | Personenrolle.Dozierende);
            p.RollenAm(heute.AddDays(-1), h.Uhr.GetLocalNow()).ShouldBe(Personenrolle.Teilnehmende); // zeitlich gültig
            (await db.Benutzerkonten.SingleAsync(k => k.Login == "tn-paula", Ct)).Rollen.ShouldBe([Rolle.Teilnehmende, Rolle.Dozierende]);
        }

        // Ohne Recht
        (await Should.ThrowAsync<InvalidOperationException>(() => TunAsync(h, new Benutzer("bh", "B", Rolle.Buchhaltung, [Rolle.Buchhaltung]),
            d => d.GetRequiredService<PersonenrollenDienst>().RolleBeendenAsync(pid, Personenrolle.Dozierende, heute, "x", Ct)))).Message.ShouldContain("Teilnehmerservice");
        // Mitarbeitende nicht hier
        (await Should.ThrowAsync<ArgumentException>(() => TunAsync(h, Tresen,
            d => d.GetRequiredService<PersonenrollenDienst>().RolleHinzufuegenAsync(pid, Personenrolle.Mitarbeitende, heute, "x", Ct)))).Message.ShouldContain("Benutzerkonto");

        // Dozierende wieder beenden (keine Einsätze) → Portal-Konto nur noch Teilnehmende
        await TunAsync(h, Tresen, d => d.GetRequiredService<PersonenrollenDienst>().RolleBeendenAsync(pid, Personenrolle.Dozierende, heute.AddDays(30), "Kurs beendet", Ct));
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var p = await db.Personen.SingleAsync(x => x.Id == pid, Ct);
            p.RollenAm(heute.AddDays(30), h.Uhr.GetLocalNow()).HasFlag(Personenrolle.Dozierende).ShouldBeTrue();
            p.RollenAm(heute.AddDays(31), h.Uhr.GetLocalNow()).ShouldBe(Personenrolle.Teilnehmende);
        }
    }

    [Fact]
    public async Task Teilnehmende_beenden_ist_gesperrt_solange_eine_Anmeldung_offen_ist()
    {
        await using var h = await StartenAsync();
        var pid = await TeilnehmerinMitPortalkontoAsync(h);
        var kurs = await h.KursAsync("26H-990", 10);
        await MitAsync(h, Benutzer.Automatik, d => d.GetRequiredService<AnmeldungDienst>().AnmeldenAsync(new AnmeldeAuftrag(pid, kurs.Id, Zahlart.Ueberweisung), Ct));
        var heute = DateOnly.FromDateTime(h.Uhr.GetLocalNow().Date);

        var pruefung = await MitAsync(h, Tresen, d => d.GetRequiredService<PersonenrollenDienst>().PruefenAsync(pid, Personenrolle.Teilnehmende, heute, Ct));
        pruefung.Sperrgrund.ShouldNotBeNull();
        pruefung.Sperrgrund.ShouldContain("1 Anmeldung");
        (await Should.ThrowAsync<InvalidOperationException>(() => TunAsync(h, Tresen,
            d => d.GetRequiredService<PersonenrollenDienst>().RolleBeendenAsync(pid, Personenrolle.Teilnehmende, heute, "Wunsch", Ct)))).Message.ShouldContain("Anmeldung");
    }

    [Fact]
    public async Task Stammdaten_aendern_uebernimmt_nur_Geaendertes_mit_Datum_und_meldet_Namensaenderung()
    {
        await using var h = await StartenAsync();
        var pid = await TeilnehmerinMitPortalkontoAsync(h);
        var heute = DateOnly.FromDateTime(h.Uhr.GetLocalNow().Date);
        var ab = heute.AddDays(-5);

        var vorher = await MitAsync(h, Tresen, async d => (await d.GetRequiredService<VibeVhsDbContext>().Personen.SingleAsync(x => x.Id == pid, Ct)).StandAm(heute, h.Uhr.GetLocalNow()));
        var neu = StammdatenEingabe.Aus(vorher) with { Anrede = "Frau", Nachname = "Peters-Lang", Geburtsname = "Peters", Strasse = "Bahnhofstraße 7", Telefon = "0172 1234567" };
        var ergebnis = await MitAsync(h, Tresen, d => d.GetRequiredService<Stammdatenaenderung>().AendernAsync(pid, neu, korrektur: false, ab, "Heirat, Urkunde liegt vor", Ct));
        ergebnis.NameGeaendert.ShouldBeTrue();
        ergebnis.Geaendert.ShouldBe(["Name", "Anrede", "Geburtsname", "Anschrift (Umzug)", "Telefon"], ignoreOrder: true);

        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var p = await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Personen.SingleAsync(x => x.Id == pid, Ct);
            var jetzt = h.Uhr.GetLocalNow();
            var neuStand = p.StandAm(heute, jetzt);
            neuStand.Name!.Nachname.ShouldBe("Peters-Lang");
            neuStand.Anrede.ShouldBe("Frau");
            neuStand.Geburtsname.ShouldBe("Peters");
            StammdatenEingabe.Hauptanschrift(neuStand)!.Strasse.ShouldBe("Bahnhofstraße 7");
            var alt = p.StandAm(ab.AddDays(-1), jetzt); // davor gilt der alte Stand
            alt.Name!.Nachname.ShouldBe("Peters");
            StammdatenEingabe.Hauptanschrift(alt)!.Strasse.ShouldBe("Lindenstraße 1");
            alt.Geburtsname.ShouldBeNull();
        }

        // Nur das Geschlecht korrigiert: keine Namensänderung (also kein „Bescheinigungen neu ausstellen“)
        var stand2 = await MitAsync(h, Tresen, async d => (await d.GetRequiredService<VibeVhsDbContext>().Personen.SingleAsync(x => x.Id == pid, Ct)).StandAm(heute, h.Uhr.GetLocalNow()));
        var e2 = await MitAsync(h, Tresen, d => d.GetRequiredService<Stammdatenaenderung>()
            .AendernAsync(pid, StammdatenEingabe.Aus(stand2) with { Geschlecht = "weiblich" }, korrektur: true, heute, "Erfassungsfehler", Ct));
        e2.NameGeaendert.ShouldBeFalse();
        e2.Geaendert.ShouldBe(["Geschlecht"]);
        (await Should.ThrowAsync<InvalidOperationException>(() => MitAsync(h, Tresen, d => d.GetRequiredService<Stammdatenaenderung>()
            .AendernAsync(pid, StammdatenEingabe.Aus(stand2) with { Geschlecht = "weiblich" }, true, heute, "nichts", Ct)))).Message.ShouldContain("nichts geändert");
    }

    [Fact]
    public async Task Umzugsdatum_korrigieren_mit_Rechten_Grund_und_Datenhistorie()
    {
        await using var h = await StartenAsync();
        var pid = await TeilnehmerinMitPortalkontoAsync(h);
        var heute = DateOnly.FromDateTime(h.Uhr.GetLocalNow().Date);
        var umzug = heute.AddDays(30);
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var p = await db.Personen.SingleAsync(x => x.Id == pid, Ct);
            var alt = p.Adressen.Single().AdressId;
            p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, alt, new DateOnly(2024, 1, 1), Erfassung.Erst(h.Uhr.GetLocalNow()));
            p.UmzugAb(alt, null, "Bergweg 2", "48268", "Greven", umzug, Erfassung.Aenderung(h.Uhr.GetLocalNow(), "Umzug gemeldet"));
            await db.SaveChangesAsync(Ct);
        }

        h.Uhr.Advance(TimeSpan.FromMinutes(5));
        var eintrag = (await MitAsync(h, Tresen, async d => Datenhistorie.Erstellen((await d.GetRequiredService<IPersonenSpeicher>().LadenAsync(pid, Ct))!)))
            .First(e => e.Korrektur == Stammdatenaenderung.Datumskorrektur.Umzug && e.DatumKorrigierbar && e.GueltigVon == umzug);

        // Ohne Recht (Buchhaltung) und ohne Grund geht es nicht.
        var buchhaltung = new Benutzer("bea.buch", "Bea Buch", Rolle.Buchhaltung, [Rolle.Buchhaltung]);
        await Should.ThrowAsync<UnauthorizedAccessException>(() => TunAsync(h, buchhaltung, d => d.GetRequiredService<Stammdatenaenderung>()
            .DatumKorrigierenAsync(pid, eintrag.Korrektur!.Value, eintrag.KorrekturBezug, umzug, umzug.AddDays(-10), "Meldebescheinigung", Ct)));
        await Should.ThrowAsync<ArgumentException>(() => TunAsync(h, Tresen, d => d.GetRequiredService<Stammdatenaenderung>()
            .DatumKorrigierenAsync(pid, eintrag.Korrektur!.Value, eintrag.KorrekturBezug, umzug, umzug.AddDays(-10), " ", Ct)));

        await TunAsync(h, Tresen, d => d.GetRequiredService<Stammdatenaenderung>()
            .DatumKorrigierenAsync(pid, eintrag.Korrektur!.Value, eintrag.KorrekturBezug, umzug, umzug.AddDays(-10), "Meldebescheinigung", Ct));

        h.Uhr.Advance(TimeSpan.FromMinutes(1));
        var jetzt = h.Uhr.GetLocalNow();
        var person = await MitAsync(h, Tresen, d => d.GetRequiredService<IPersonenSpeicher>().LadenAsync(pid, Ct));
        person!.StandAm(umzug.AddDays(-5), jetzt).Aufenthaltsort!.Ort.ShouldBe("Greven");
        person.StandAm(umzug.AddDays(-11), jetzt).Aufenthaltsort!.Ort.ShouldBe("Emsdetten");
        Datenhistorie.Erstellen(person).ShouldContain(e => e.Art == Erfassungsart.Korrektur && e.Grund == "Meldebescheinigung");
    }
}
