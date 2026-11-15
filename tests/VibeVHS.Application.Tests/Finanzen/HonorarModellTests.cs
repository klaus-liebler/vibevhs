using Shouldly;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Finanzen.Honorare;
using VibeVHS.Domain.Finanzen;

namespace VibeVHS.Application.Tests.Finanzen;

public class HonorarModellTests
{
    private static readonly Guid Kurs = Guid.NewGuid();

    private static HonorarTermin Termin(int tag, Terminstatus status = Terminstatus.Stattgefunden, int minuten = 90, int? anwesend = 8, Guid? id = null)
    {
        var beginn = new DateTime(2026, 11, tag, 18, 0, 0);
        return new HonorarTermin(id ?? Guid.NewGuid(), Kurs, "26H-301", "Rhetorik", 3, DateOnly.FromDateTime(beginn), beginn, beginn.AddMinutes(minuten),
            Math.Round(minuten / 45m, 2), status, status == Terminstatus.Stattgefunden ? null : "krank", "A 1.12", null, 10, anwesend, 2);
    }

    private static HonorarKontext Kontext(Dictionary<string, string> parameter, IReadOnlyList<HonorarTermin> termine, IReadOnlyList<FruehereHonorarposition>? frueher = null)
        => new(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), parameter, new DateOnly(2026, 1, 1), null, termine, frueher ?? []);

    [Fact]
    public void ProUe_rechnet_UE_mal_Satz_und_vergutet_kurzfristige_Absagen_anteilig()
    {
        var modell = new HonorarProUe();
        var ergebnis = modell.Berechne(Kontext(new() { ["satzJeUe"] = "30", ["ausfallProzent"] = "50" },
            [Termin(3), Termin(10, minuten: 45), Termin(17, Terminstatus.KurzfristigAbgesagt), Termin(24, Terminstatus.Ausgefallen)]));
        ergebnis.Positionen.Select(p => p.Betrag).ShouldBe([60m, 30m, 30m]);
        ergebnis.Summe.ShouldBe(120m);
        ergebnis.Positionen[2].Art.ShouldBe("Ausfall");
        ergebnis.Hinweise.ShouldContain(h => h.Contains("24.11.2026") && h.Contains("keine Vergütung"));
    }

    [Fact]
    public void ProUe_ohne_Ausfallvergutung_und_mit_Fahrtkosten()
    {
        var modell = new HonorarProUe();
        var ergebnis = modell.Berechne(Kontext(new() { ["satzJeUe"] = "32.5", ["fahrtkosten"] = "km", ["kmSatz"] = "0.30", ["kmJeTermin"] = "24" },
            [Termin(3), Termin(17, Terminstatus.KurzfristigAbgesagt)]));
        ergebnis.Positionen.Count.ShouldBe(2);
        ergebnis.Positionen[0].Betrag.ShouldBe(65m);
        ergebnis.Positionen[1].Art.ShouldBe("Fahrtkosten");
        ergebnis.Positionen[1].Betrag.ShouldBe(7.20m);
        ergebnis.Positionen[1].TerminId.ShouldBe(ergebnis.Positionen[0].TerminId);
    }

    [Fact]
    public void Bereits_abgerechnete_Termine_erscheinen_nicht_doppelt()
    {
        var id = Guid.NewGuid();
        var frueher = new FruehereHonorarposition("HO-2026-00001", new DateOnly(2026, 11, 5), null, null, id, "alt", 60m, "pro-ue", "1.0");
        var ergebnis = new HonorarProUe().Berechne(Kontext(new() { ["satzJeUe"] = "30" }, [Termin(3, id: id), Termin(10)], [frueher]));
        ergebnis.Positionen.ShouldHaveSingleItem().Betrag.ShouldBe(60m);
        ergebnis.Hinweise.ShouldContain(h => h.Contains("bereits abgerechnet"));
    }

    [Fact]
    public void Basismodell_mit_Teilnehmenden_und_Grenzen()
    {
        var modell = new HonorarBasisUeTeilnehmende();
        var p = new Dictionary<string, string> { ["basisJeTermin"] = "20", ["satzJeUe"] = "10", ["satzJeTeilnehmer"] = "2" };
        modell.Berechne(Kontext(p, [Termin(3, anwesend: 5)])).Summe.ShouldBe(50m); // 20 + 2×10 + 5×2

        p["mindestJeTermin"] = "60";
        var mindest = modell.Berechne(Kontext(p, [Termin(3, anwesend: 5)]));
        mindest.Summe.ShouldBe(60m);
        mindest.Positionen[0].Text.ShouldContain("Mindestbetrag");

        p.Remove("mindestJeTermin");
        p["hoechstJeTermin"] = "40";
        modell.Berechne(Kontext(p, [Termin(3, anwesend: 5)])).Summe.ShouldBe(40m);

        var ohneListe = modell.Berechne(Kontext(p, [Termin(3, anwesend: null), Termin(10, Terminstatus.KurzfristigAbgesagt)]));
        ohneListe.Summe.ShouldBe(40m); // 20 + 20 = 40 (Höchstbetrag greift nicht), Absage ohne Vergütung
        ohneListe.Hinweise.ShouldContain(h => h.Contains("Anwesenheit nicht erfasst"));
        ohneListe.Hinweise.ShouldContain(h => h.Contains("kurzfristig abgesagt"));
    }

    [Fact]
    public void Berechnung_ist_deterministisch_und_rundet_auf_Cent()
    {
        var termine = new[] { Termin(3, minuten: 100) }; // 2,22 UE
        var p = new Dictionary<string, string> { ["satzJeUe"] = "33.33" };
        var a = new HonorarProUe().Berechne(Kontext(p, termine));
        var b = new HonorarProUe().Berechne(Kontext(p, termine));
        a.Summe.ShouldBe(b.Summe);
        a.Summe.ShouldBe(Math.Round(2.22m * 33.33m, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Parameterpruefung_normalisiert_und_meldet_Fehler()
    {
        var modell = new HonorarProUe();
        var (werte, fehler) = Parameterpruefung.Pruefen(modell, new Dictionary<string, string?> { ["satzJeUe"] = "32,50 €", ["ausfallProzent"] = "" });
        fehler.ShouldBeEmpty();
        werte["satzJeUe"].ShouldBe("32.50");
        werte["ausfallProzent"].ShouldBe("0"); // Standardwert
        werte["fahrtkosten"].ShouldBe("keine");

        var (_, f2) = Parameterpruefung.Pruefen(modell, new Dictionary<string, string?> { ["ausfallProzent"] = "150", ["fahrtkosten"] = "Taxi" });
        f2.Keys.ShouldBe(["satzJeUe", "ausfallProzent", "fahrtkosten"], ignoreOrder: true);
        f2["satzJeUe"].ShouldContain("erforderlich");
    }

    [Fact]
    public void Registrierung_waehlt_Modell_nach_Kennung()
    {
        var modelle = new HonorarModelle([new HonorarProUe(), new HonorarBasisUeTeilnehmende()]);
        modelle.Alle.Count.ShouldBe(2);
        modelle.Holen("basis-ue-tn").Version.ShouldBe("1.0");
        Should.Throw<InvalidOperationException>(() => modelle.Holen("gibt-es-nicht")).Message.ShouldContain("nicht installiert");
    }

    [Fact]
    public void Iban_Pruefung_und_Testdaten()
    {
        Iban.Gueltig("DE89 3704 0044 0532 0130 00").ShouldBeTrue();
        Iban.Fehler("DE89 3704 0044 0532 0130 01")!.ShouldContain("Prüfziffer");
        Iban.Fehler("DE89 3704")!.ShouldContain("Ländercode");
        Iban.Gueltig(Iban.AusBlzUndKonto("40351060", "72000011")).ShouldBeTrue();
        Iban.Maskiert("DE89370400440532013000").ShouldBe("DE•• •••• 3000");
        SepaXml.Text("Müller & Söhne – Straße", 70).ShouldBe("Mueller + Soehne - Strasse");
    }
}
