using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Adapters.Mocks.Testdaten;
using VibeVHS.Application.Personen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Tests;

public class PersonenAbfrageTests
{
    private static async Task<(TestDatenbank Db, Massendatensatz Daten)> MitMassendatenAsync(int anzahl)
    {
        var t = await TestDatenbank.ErzeugenAsync();
        var daten = new Massendaten().Erzeugen(anzahl, t.Uhr.GetLocalNow());
        await using var db = t.NeuerKontext();
        db.Organisationen.AddRange(daten.Organisationen);
        db.Personen.AddRange(daten.Personen);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (t, daten);
    }

    private static PersonenAbfrage Abfrage(VibeVhsDbContext db) => new(db, new DublettenSuche(db));

    [Fact]
    public async Task Keyset_liefert_alle_Treffer_ohne_Luecken_und_Doppelte_in_Namensfolge()
    {
        var ct = TestContext.Current.CancellationToken;
        var (t, daten) = await MitMassendatenAsync(400);
        await using var _ = t;
        await using var db = t.NeuerKontext();
        var abfrage = Abfrage(db);

        var gesehen = new List<PersonZeile>();
        Seitenschluessel? nach = null;
        while (true)
        {
            var seite = await abfrage.SeiteAsync(new PersonenFilter(), nach, 50, ct);
            gesehen.AddRange(seite);
            if (seite.Count < 50)
            {
                break;
            }

            nach = Seitenschluessel.Von(seite[^1]);
        }

        gesehen.Count.ShouldBe(daten.Personen.Count);
        gesehen.Select(z => z.Id).Distinct().Count().ShouldBe(gesehen.Count);
        gesehen.Select(z => z.Seite).ShouldBe(gesehen.Select(z => z.Seite).Order(Comparer<Seitenschluessel>.Create(Seitenschluessel.Vergleichen)));
    }

    [Fact]
    public async Task Suche_findet_normalisierte_Schreibweisen_und_Praefixe()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using (var db = t.NeuerKontext())
        {
            var jetzt = t.Uhr.GetLocalNow();
            db.Personen.AddRange(
                Person.Anlegen("Mehmet", "Yılmaz", new DateOnly(2026, 1, 1), jetzt, new DateOnly(1988, 5, 3)),
                Person.Anlegen("Hans", "Müller", new DateOnly(2026, 1, 1), jetzt),
                Person.Anlegen("Petra", "Großmann", new DateOnly(2026, 1, 1), jetzt, null, "petra@example.org"));
            await db.SaveChangesAsync(ct);
        }

        await using var db2 = t.NeuerKontext();
        var abfrage = Abfrage(db2);
        async Task<string[]> Suche(string s) => (await abfrage.SeiteAsync(new PersonenFilter(s), null, 50, ct)).Select(z => z.Anzeigename).ToArray();

        (await Suche("yilmaz")).ShouldBe(["Mehmet Yılmaz"]);
        (await Suche("YIL meh")).ShouldBe(["Mehmet Yılmaz"]);
        (await Suche("mueller")).ShouldBe(["Hans Müller"]);
        (await Suche("grossm")).ShouldBe(["Petra Großmann"]);
        (await Suche("petra@example")).ShouldBe(["Petra Großmann"]);
        (await Suche("03.05.1988")).ShouldBe(["Mehmet Yılmaz"]);
        (await Suche("schmidt")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Namensaenderung_aktualisiert_Suchindex_frueherer_Name_bleibt_auffindbar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        Guid id;
        await using (var db = t.NeuerKontext())
        {
            var p = Person.Anlegen("Anna", "Schmidt", new DateOnly(2019, 3, 1), t.Uhr.GetLocalNow().AddYears(-3));
            id = p.Id;
            db.Personen.Add(p);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var p = await db.Personen.SingleAsync(x => x.Id == id, ct);
            p.NameAendernAb("Anna", "Becker", new DateOnly(2025, 6, 14), "Heirat", t.Uhr.GetLocalNow());
            await db.SaveChangesAsync(ct);
        }

        await using var db3 = t.NeuerKontext();
        var abfrage = Abfrage(db3);
        (await abfrage.SeiteAsync(new PersonenFilter("becker"), null, 10, ct)).ShouldHaveSingleItem().Anzeigename.ShouldBe("Anna Becker");
        (await abfrage.SeiteAsync(new PersonenFilter("schmidt"), null, 10, ct)).ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task Filterzahlen_passen_zu_den_gefilterten_Listen()
    {
        var ct = TestContext.Current.CancellationToken;
        var (t, daten) = await MitMassendatenAsync(300);
        await using var _ = t;
        await using var db = t.NeuerKontext();
        var abfrage = Abfrage(db);

        var zahlen = await abfrage.ZaehlenAsync(new PersonenFilter(), ct);

        zahlen.Gesamt.ShouldBe(daten.Personen.Count);
        zahlen.Rollen[Personenrolle.Dozierende].ShouldBe(daten.Personen.Count(p => p.Rollen.HasFlag(Personenrolle.Dozierende)));
        zahlen.Status[Personenstatus.Ruhend].ShouldBe(daten.Personen.Count(p => p.Status == Personenstatus.Ruhend));
        zahlen.Fachbereiche[4].ShouldBe(daten.Personen.Count(p => p.Fachbereiche.Contains(4)));
        zahlen.DublettenHinweise.ShouldBeGreaterThanOrEqualTo(2);

        var dozierendeFb4 = await abfrage.ZaehlenAsync(new PersonenFilter(Rolle: Personenrolle.Dozierende, Fachbereich: 4), ct);
        dozierendeFb4.Gesamt.ShouldBe(daten.Personen.Count(p => p.Rollen.HasFlag(Personenrolle.Dozierende) && p.Fachbereiche.Contains(4)));
        var liste = await abfrage.SeiteAsync(new PersonenFilter(Rolle: Personenrolle.Dozierende, Fachbereich: 4), null, 500, ct);
        liste.Count.ShouldBe(dozierendeFb4.Gesamt);
    }

    [Fact]
    public async Task Dubletten_werden_erkannt_und_lassen_sich_ausschliessen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var jetzt = t.Uhr.GetLocalNow();
        var a = Person.Anlegen("Mehmet", "Yılmaz", new DateOnly(2026, 1, 1), jetzt, new DateOnly(1988, 5, 3));
        var b = Person.Anlegen("Mehmet", "Yilmaz", new DateOnly(2026, 1, 1), jetzt, new DateOnly(1988, 5, 3));
        var c = Person.Anlegen("Mehmet", "Yilmaz", new DateOnly(2026, 1, 1), jetzt, new DateOnly(1990, 1, 1));
        var d = Person.Anlegen("Yilmaz", "Mehmet", new DateOnly(2026, 1, 1), jetzt, new DateOnly(1988, 5, 3));
        await using (var db = t.NeuerKontext())
        {
            db.Personen.AddRange(a, b, c, d);
            await db.SaveChangesAsync(ct);
        }

        await using var db2 = t.NeuerKontext();
        var suche = new DublettenSuche(db2);

        var kandidaten = await suche.KandidatenAsync("Mehmet", "Yilmaz", new DateOnly(1988, 5, 3), null, ct);
        kandidaten.Select(k => k.Id).ShouldBe([a.Id, b.Id, d.Id], ignoreOrder: true);
        kandidaten.Single(k => k.Id == d.Id).Grund.ShouldBe("Vor- und Nachname vertauscht");
        (await suche.KandidatenAsync("Mehmet", "Yilmaz", new DateOnly(1999, 1, 1), null, ct)).ShouldBeEmpty();

        (await suche.PaareAsync(ct)).Count.ShouldBe(3); // a-b, a-d, b-d; c hat ein anderes Geburtsdatum

        db2.DublettenAusschluesse.Add(new DublettenAusschluss(a.Id, d.Id, jetzt, "Test"));
        await db2.SaveChangesAsync(ct);
        (await suche.PaareAsync(ct)).Count.ShouldBe(2);
        (await suche.KandidatenAsync("Mehmet", "Yılmaz", new DateOnly(1988, 5, 3), a.Id, ct)).Select(k => k.Id).ShouldBe([b.Id]);
    }

    [Fact]
    public async Task Zusammengefuehrte_Dubletten_sind_aus_der_Liste_verschwunden()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var jetzt = t.Uhr.GetLocalNow();
        var a = Person.Anlegen("Mehmet", "Yılmaz", new DateOnly(2026, 1, 1), jetzt, new DateOnly(1988, 5, 3));
        var b = Person.Anlegen("Mehmet", "Yilmaz", new DateOnly(2026, 1, 1), jetzt, new DateOnly(1988, 5, 3));
        await using (var db = t.NeuerKontext())
        {
            db.Personen.AddRange(a, b);
            await db.SaveChangesAsync(ct);
            a.DubletteUebernehmen(b, Erfassung.Aenderung(jetzt.AddMinutes(1), "Test"));
            await db.SaveChangesAsync(ct);
        }

        await using var db2 = t.NeuerKontext();
        var abfrage = Abfrage(db2);
        (await abfrage.SeiteAsync(new PersonenFilter("yilmaz"), null, 10, ct)).ShouldHaveSingleItem().Id.ShouldBe(a.Id);
        (await abfrage.SeiteAsync(new PersonenFilter(Status: Personenstatus.Zusammengefuehrt), null, 10, ct)).ShouldHaveSingleItem().Id.ShouldBe(b.Id);
        (await new DublettenSuche(db2).PaareAsync(ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Excel_Export_enthaelt_alle_Treffer()
    {
        var ct = TestContext.Current.CancellationToken;
        var (t, _) = await MitMassendatenAsync(120);
        await using var _t = t;
        await using var db = t.NeuerKontext();

        var bytes = await Abfrage(db).ExcelAsync(new PersonenFilter(Rolle: Personenrolle.Teilnehmende), ct);

        bytes.Length.ShouldBeGreaterThan(1000);
        bytes[0].ShouldBe((byte)'P'); // xlsx ist ein ZIP-Archiv
        using var mappe = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var zeilen = mappe.Worksheet(1).RowsUsed().Count() - 1;
        zeilen.ShouldBe((await db.Personen.AsNoTracking().ToListAsync(ct)).Count(p => (p.Rollen & Personenrolle.Teilnehmende) == Personenrolle.Teilnehmende
                                                          && p.Status != Personenstatus.Zusammengefuehrt));
    }

    [Fact]
    public async Task Ladezeit_mit_3000_Personen()
    {
        var ct = TestContext.Current.CancellationToken;
        var (t, daten) = await MitMassendatenAsync(3000);
        await using var _ = t;
        await using var db = t.NeuerKontext();
        var abfrage = Abfrage(db);
        await abfrage.SeiteAsync(new PersonenFilter(), null, 50, ct); // Aufwärmen

        var uhr = Stopwatch.StartNew();
        var erste = await abfrage.SeiteAsync(new PersonenFilter(), null, 50, ct);
        var tErste = uhr.Elapsed.TotalMilliseconds;

        uhr.Restart();
        Seitenschluessel nach = Seitenschluessel.Von(erste[^1]);
        for (var i = 0; i < 40; i++)
        {
            var s = await abfrage.SeiteAsync(new PersonenFilter(), nach, 50, ct);
            nach = Seitenschluessel.Von(s[^1]);
        }

        var tTief = uhr.Elapsed.TotalMilliseconds / 40;

        uhr.Restart();
        var suche = await abfrage.SeiteAsync(new PersonenFilter("yil"), null, 50, ct);
        var tSuche = uhr.Elapsed.TotalMilliseconds;

        uhr.Restart();
        var zahlen = await abfrage.ZaehlenAsync(new PersonenFilter(), ct);
        var tZahlen = uhr.Elapsed.TotalMilliseconds;

        TestContext.Current.SendDiagnosticMessage(
            $"Ladezeit {daten.Personen.Count} Personen: erste Seite {tErste:F1} ms, Seite ab Position 2.000 Ø {tTief:F1} ms, Suche 'yil' {tSuche:F1} ms ({suche.Count} Treffer), Filterzahlen {tZahlen:F1} ms");
        Console.WriteLine($"MESSUNG erste={tErste:F1} tief={tTief:F1} suche={tSuche:F1} zahlen={tZahlen:F1} personen={zahlen.Gesamt}");

        erste.Count.ShouldBe(50);
        tErste.ShouldBeLessThan(500);
        tTief.ShouldBeLessThan(500);
    }
}
