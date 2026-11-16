using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Domain.Tests.Programm;

/// <summary>Werttypen der Merkmale: Prüfung beim Eintragen, Typsperre nach Verwendung, Bedingungen der Kursarten.</summary>
public sealed class MerkmalTypTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(1));

    private static Merkmal Pc => new("pc", "PC-Arbeitsplätze", "Ausstattung", vererbbar: false, typ: Merkmalstyp.Ganzzahl, einheit: null, min: 0, max: 200);

    private static Merkmal Tafel => new("tafel", "Tafel", "Ausstattung", vererbbar: false, werte: ["Kreide", "Whiteboard", "Smartboard"]);

    [Fact]
    public void Ohne_Angabe_ist_ein_Merkmal_JaNein_mit_Werten_eine_Auswahl()
    {
        new Merkmal("beamer", "Beamer", "Ausstattung").Typ.ShouldBe(Merkmalstyp.JaNein);
        Tafel.Typ.ShouldBe(Merkmalstyp.Auswahl);
    }

    [Fact]
    public void Werte_werden_je_Typ_geprueft_und_vereinheitlicht()
    {
        new Merkmal("beamer", "Beamer", "Ausstattung").WertPruefen("  ").ShouldBeNull();
        Tafel.WertPruefen("smartboard").ShouldBe("Smartboard");
        Should.Throw<ArgumentException>(() => Tafel.WertPruefen("Tageslichtprojektor")).Message.ShouldContain("nicht wählbar");
        Pc.WertPruefen(" 12 ").ShouldBe("12");
        Should.Throw<ArgumentException>(() => Pc.WertPruefen("zwölf")).Message.ShouldContain("ganze Zahl");
        Should.Throw<ArgumentException>(() => Pc.WertPruefen("500")).Message.ShouldContain("bis 200");
        var text = new Merkmal("zugang", "Hinweise zum Zugang", "Zugänglichkeit", typ: Merkmalstyp.Text);
        Should.Throw<ArgumentException>(() => text.WertPruefen(null)).Message.ShouldContain("Text");
        Should.Throw<ArgumentException>(() => text.WertPruefen(new string('x', Merkmal.TextMaxLaenge + 1))).Message.ShouldContain("zu lang");
        var sprachen = new Merkmal("sf", "Lehrfreigabe", "Freigabe", bezug: MerkmalBezug.Person, werte: ["Englisch", "Spanisch"], mehrfach: true);
        sprachen.WertPruefen("spanisch, Englisch").ShouldBe("Spanisch, Englisch");
    }

    [Fact]
    public void Typ_ist_nach_Verwendung_gesperrt_Werte_lassen_sich_ergaenzen_und_deaktivieren()
    {
        var t = Tafel;
        Should.Throw<InvalidOperationException>(() => t.TypFestlegen(Merkmalstyp.Text, verwendet: true)).Message.ShouldContain("nicht mehr änderbar");
        Should.Throw<InvalidOperationException>(() => t.TypFestlegen(Merkmalstyp.Auswahl, verwendet: true, werte: ["Kreide"])).Message.ShouldContain("deaktiviert");
        t.WertErgaenzen("Flipchart");
        t.WertDeaktivieren("Kreide");
        t.AktiveWerte.ShouldBe(["Whiteboard", "Smartboard", "Flipchart"]);
        Should.Throw<ArgumentException>(() => t.WertPruefen("Kreide"));
        t.WertErgaenzen("kreide"); // reaktiviert, kein Duplikat
        t.Werte.Count.ShouldBe(4);
        t.AktiveWerte.ShouldContain("Kreide");
        var frei = new Merkmal("x", "X", "Ausstattung");
        frei.TypFestlegen(Merkmalstyp.Ganzzahl, verwendet: false, einheit: "m²", min: 1);
        frei.Typ.ShouldBe(Merkmalstyp.Ganzzahl);
        frei.WertAnzeige("40").ShouldBe("40 m²");
    }

    [Fact]
    public void Bedingungen_lesen_anzeigen_und_gegen_den_Typ_pruefen()
    {
        var katalog = new Dictionary<string, Merkmal> { ["pc"] = Pc, ["tafel"] = Tafel, ["zugang"] = new("zugang", "Zugang", "Z", typ: Merkmalstyp.Text) };
        Anforderung.Lesen("pc>=12").ShouldBe(new Bedingung("pc", Vergleich.Mindestens, "12"));
        Anforderung.Basis("pc>=12").ShouldBe("pc");
        Anforderung.Zerlegen("tafel=Smartboard").ShouldBe(("tafel", "Smartboard"));
        Anforderung.Anzeige("pc>=12", katalog).ShouldBe("PC-Arbeitsplätze: mind. 12");
        Anforderung.Pruefen("tafel=Smartboard", katalog).ShouldBe("tafel=Smartboard");
        Should.Throw<ArgumentException>(() => Anforderung.Pruefen("zugang", katalog)).Message.ShouldContain("Textmerkmal");
        Should.Throw<ArgumentException>(() => Anforderung.Pruefen("tafel>=2", katalog)).Message.ShouldContain("Zahlenmerkmalen");
        Should.Throw<ArgumentException>(() => Anforderung.Pruefen("tafel=Overhead", katalog)).Message.ShouldContain("kein Wert");
        Anforderung.Abweichung("pc>=12", Pc, "8").ShouldBe("nur 8 statt mind. 12 PC-Arbeitsplätze.");
        Anforderung.Abweichung("pc>=12", Pc, "12").ShouldBeNull();
    }

    [Fact]
    public void Raumauswahl_nennt_konkret_was_fehlt_und_beachtet_Ueberschreiben_am_Raum()
    {
        var e = Erfassung.Erst(Jetzt.AddYears(-1));
        var katalog = new[] { Pc, Tafel };
        var gebaeude = new Ort(Ortsart.Gebaeude, "Haus A");
        var klein = new Ort(Ortsart.Raum, "EDV klein", gebaeude.Id, "A 1.12").MitKapazitaet(Nutzungsart.PcKurs, 8);
        var gross = new Ort(Ortsart.Raum, "EDV groß", gebaeude.Id, "A 2.01").MitKapazitaet(Nutzungsart.PcKurs, 14);
        klein.MerkmalSetzenAb(katalog[0], "8", null, new DateOnly(2024, 1, 1), null, e);
        gross.MerkmalSetzenAb(katalog[0], "14", null, new DateOnly(2024, 1, 1), null, e);
        gross.MerkmalSetzenAb(katalog[1], "Smartboard", null, new DateOnly(2024, 1, 1), null, e);
        var verzeichnis = new Ortsverzeichnis([gebaeude, klein, gross], katalog);
        var tage = new[] { new DateOnly(2026, 12, 1) };

        var k = Ortsplanung.Pruefen(verzeichnis, klein, Nutzungsart.PcKurs, ["pc>=12", "tafel=Smartboard"], tage, Jetzt);
        k.Geeignet.ShouldBeFalse();
        k.Gruende.ShouldContain("nur 8 statt mind. 12 PC-Arbeitsplätze.");
        k.Gruende.ShouldContain("Merkmal „Tafel“ fehlt.");
        Ortsplanung.Pruefen(verzeichnis, gross, Nutzungsart.PcKurs, ["pc>=12", "tafel=Smartboard"], tage, Jetzt).Geeignet.ShouldBeTrue();
        Ortsplanung.Pruefen(verzeichnis, gross, Nutzungsart.PcKurs, ["tafel=Kreide"], tage, Jetzt).Gruende.Single().ShouldContain("nicht für Kreide");
    }
}
