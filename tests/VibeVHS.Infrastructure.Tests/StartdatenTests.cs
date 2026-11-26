using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Startdaten;

namespace VibeVHS.Infrastructure.Tests;

public class StartdatenTests
{
    private static string Datei => Path.Combine(AppContext.BaseDirectory, "testbenutzer-vhs-egs.json");

    [Fact]
    public async Task Startdaten_der_vhs_EGS_werden_vollstaendig_geladen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using var db = t.NeuerKontext();

        var ergebnis = await t.Lader(db).AusDateiLadenAsync(Datei, ct);

        ergebnis.ShouldBe(new StartdatenErgebnis(true, 8, 11, 11));
        var einrichtung = await db.Einrichtungen.SingleAsync(ct);
        einrichtung.Kurzname.ShouldBe("vhs EGS");
        einrichtung.Orte.ShouldBe(["Emsdetten", "Greven", "Saerbeck"]);
        einrichtung.OrteAnzeige.ShouldBe("Emsdetten · Greven · Saerbeck (NRW)");

        var fb = await db.Fachbereiche.OrderBy(f => f.Nr).ToListAsync(ct);
        fb.Select(f => f.Nr).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8]);
        fb[3].Name.ShouldBe("Alphabetisierung und Deutsch als Zweitsprache");
    }

    [Fact]
    public async Task Zweites_Laden_aendert_nichts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using (var db = t.NeuerKontext())
        {
            await t.Lader(db).AusDateiLadenAsync(Datei, ct);
        }

        await using var db2 = t.NeuerKontext();
        var zweites = await t.Lader(db2).AusDateiLadenAsync(Datei, ct);

        zweites.EtwasGeaendert.ShouldBeFalse();
        (await db2.Benutzerkonten.CountAsync(ct)).ShouldBe(11);
        (await db2.Personen.CountAsync(ct)).ShouldBe(11);
        (await db2.Einrichtungen.CountAsync(ct)).ShouldBe(1);
        (await db2.Protokoll.CountAsync(e => e.Aktion == "Startdaten.Geladen", ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Teammitglieder_sind_Mitarbeitende_mit_Konto_Rollen_und_Fachbereichen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using var db = t.NeuerKontext();
        await t.Lader(db).AusDateiLadenAsync(Datei, ct);

        var konto = await db.Benutzerkonten.SingleAsync(k => k.Login == "heike.steinkamp", ct);
        konto.Rollen.ShouldBe([Rolle.Fachbereichsleitung, Rolle.IntegrationskursTeam]);
        konto.Fachbereiche.ShouldBe([4, 5]);

        var person = await db.Personen.SingleAsync(p => p.Id == konto.PersonId, ct);
        person.Titel.ShouldBe("Dr.");
        person.Rollen.HasFlag(Personenrolle.Mitarbeitende).ShouldBeTrue();
        var name = person.AktuellerName(t.Uhr.GetLocalNow());
        name.Vorname.ShouldBe("Heike");
        name.Nachname.ShouldBe("Steinkamp");

        var schatte = await db.Benutzerkonten.SingleAsync(k => k.Login == "varvara.schatte", ct);
        schatte.Fachbereiche.ShouldBe([2, 3, 6, 7]);
        (await db.Benutzerkonten.SingleAsync(k => k.Login == "amy.behrens", ct)).Rollen.ShouldContain(Rolle.Systemverwaltung);
    }

    [Fact]
    public async Task Fehlende_Konten_werden_nachgeladen_vorhandene_bleiben()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var daten = new StartdatenDatei
        {
            Fachbereiche = [new FachbereichEintrag { Nr = 1, Name = "Umwelt" }],
            Benutzer = [new BenutzerEintrag { Login = "a.b", Name = "Anna Beta", Rollen = ["Teilnehmerservice"] }],
        };
        await using (var db = t.NeuerKontext())
        {
            await t.Lader(db).LadenAsync(daten, ct);
        }

        daten.Benutzer.Add(new BenutzerEintrag { Login = "c.d", Name = "Carl Delta", Rollen = ["Buchhaltung"] });
        await using var db2 = t.NeuerKontext();
        var ergebnis = await t.Lader(db2).LadenAsync(daten, ct);

        ergebnis.ShouldBe(new StartdatenErgebnis(false, 0, 1, 1));
        (await db2.Benutzerkonten.CountAsync(ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Unbekannte_Rolle_in_den_Startdaten_wird_abgelehnt()
    {
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using var db = t.NeuerKontext();
        var daten = new StartdatenDatei
        {
            Benutzer = [new BenutzerEintrag { Login = "x.y", Name = "X Y", Rollen = ["Hausmeister"] }],
        };

        await Should.ThrowAsync<InvalidDataException>(() => t.Lader(db).LadenAsync(daten, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Dr. Kai Lüdeking", "Dr.", "Kai", "Lüdeking")]
    [InlineData("Gisela Junkerholt", null, "Gisela", "Junkerholt")]
    [InlineData("Prof. Dr. Anna Maria Becker", "Prof. Dr.", "Anna Maria", "Becker")]
    public void Namen_werden_in_Titel_Vorname_Nachname_zerlegt(string name, string? titel, string vorname, string nachname)
    {
        StartdatenLader.NameZerlegen(name).ShouldBe((titel, vorname, nachname));
    }
}
