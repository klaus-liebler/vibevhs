using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Domain.Tests.Programm;

/// <summary>Lehrfreigabe für Sprachkurse: Anforderung je Sprache, mehrere Sprachen je Lehrkraft, Sprache aus dem Kurstitel.</summary>
public class SprachfreigabeTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateOnly Tag = new(2026, 12, 1);

    private static readonly Merkmal Freigabe = new(Sprachfreigabe.Schluessel, "Lehrfreigabe Sprachkurs (Eignungsprüfung der vhs)", "Freigabe",
        nachweisPflicht: true, bezug: MerkmalBezug.Person, werte: Sprachfreigabe.Sprachen, mehrfach: true);

    private static readonly IReadOnlyDictionary<string, Merkmal> Katalog = new Dictionary<string, Merkmal> { [Freigabe.Schluessel] = Freigabe };

    private static Person Lehrkraft(string? sprachen)
    {
        var p = Person.Anlegen("Lena", "Lingua", new DateOnly(2019, 1, 1), Jetzt.AddYears(-7));
        p.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(2019, 1, 1), Erfassung.Erst(Jetzt.AddYears(-7)));
        if (sprachen is not null)
        {
            p.QualifikationSetzenAb(Freigabe, sprachen, "Hospitation 2024", "vhs", new DateOnly(2024, 8, 1), null, Erfassung.Erst(Jetzt.AddYears(-2)));
        }

        return p;
    }

    [Theory]
    [InlineData("Englisch A2 – Konversation", "Englisch")]
    [InlineData("Spanisch A1 – Einstieg", "Spanisch")]
    [InlineData("Deutsch B1 – Prüfungsvorbereitung", Sprachfreigabe.DeutschWert)]
    [InlineData("Plattdeutsch snacken", "Plattdeutsch")]
    [InlineData("Niederländisch für die Grenzregion", "Niederländisch")]
    [InlineData("Konversationsrunde international", null)]
    public void Sprache_aus_dem_Titel(string titel, string? sprache) => Sprachfreigabe.AusTitel(titel).ShouldBe(sprache);

    [Fact]
    public void Konkretisieren_ersetzt_die_allgemeine_Freigabe_und_behaelt_andere_Anforderungen()
    {
        Sprachfreigabe.Konkretisieren(["sprachfreigabe", "erste-hilfe"], "Englisch A2").ShouldBe(["erste-hilfe", "sprachfreigabe=Englisch"], ignoreOrder: true);
        Sprachfreigabe.Konkretisieren(["sprachfreigabe"], "Konversationsrunde").ShouldBe(["sprachfreigabe"]);
        Sprachfreigabe.Konkretisieren(["sprachfreigabe", "sprachfreigabe=Spanisch"], "Englisch A2").ShouldBe(["sprachfreigabe=Spanisch"]);
        Sprachfreigabe.Konkretisieren(["bamf-lk"], "Integrationskurs Deutsch").ShouldBe(["bamf-lk"]);
    }

    [Fact]
    public void Freigabe_fuer_mehrere_Sprachen_passt_nur_zur_eigenen_Sprache()
    {
        var p = Lehrkraft("Englisch, Spanisch");
        p.QualifikationAm(Sprachfreigabe.Schluessel, Tag, Jetzt)!.WertText.ShouldBe("Englisch, Spanisch");

        Dozentenpruefung.AmTag(p, ["sprachfreigabe=Englisch"], Katalog, Tag, Jetzt).Code.ShouldBe(Eignungscode.Geeignet);
        Dozentenpruefung.AmTag(p, ["sprachfreigabe=spanisch"], Katalog, Tag, Jetzt).Code.ShouldBe(Eignungscode.Geeignet);
        Dozentenpruefung.AmTag(p, ["sprachfreigabe"], Katalog, Tag, Jetzt).Code.ShouldBe(Eignungscode.Geeignet);

        var falsch = Dozentenpruefung.AmTag(p, ["sprachfreigabe=Französisch"], Katalog, Tag, Jetzt);
        falsch.Code.ShouldBe(Eignungscode.MerkmalFehlt);
        falsch.Begruendung.ShouldBe("„Lehrfreigabe Sprachkurs (Eignungsprüfung der vhs)“ liegt nur für Englisch, Spanisch vor, nicht für Französisch.");
        falsch.Fehlend.ShouldBe(["sprachfreigabe=Französisch"]);
        falsch.AusnahmeMoeglich.ShouldBeTrue(); // interne Freigabe, nicht gesetzlich
    }

    [Fact]
    public void Ohne_Freigabe_fehlt_sie_mit_Sprache_im_Text_und_Ausnahme_der_Fachbereichsleitung_hilft()
    {
        var p = Lehrkraft(null);
        var e = Dozentenpruefung.AmTag(p, ["sprachfreigabe=Englisch"], Katalog, Tag, Jetzt);
        e.Code.ShouldBe(Eignungscode.MerkmalFehlt);
        e.Begruendung.ShouldBe("„Lehrfreigabe Sprachkurs (Eignungsprüfung der vhs): Englisch“ fehlt.");

        var ausnahme = new Qualifikationsausnahme(Guid.NewGuid(), p.Id, Freigabe, "Muttersprachlerin, Hospitation folgt im Januar", "Heike Steinhorst", Jetzt);
        Dozentenpruefung.AmTag(p, ["sprachfreigabe=Englisch"], Katalog, Tag, Jetzt, [ausnahme]).Code.ShouldBe(Eignungscode.Ausnahme);
    }

    [Fact]
    public void Nur_Werte_aus_dem_Katalog_und_Doppelte_werden_zusammengefasst()
    {
        var p = Lehrkraft(null);
        var e = Erfassung.Erst(Jetzt);
        Should.Throw<ArgumentException>(() => p.QualifikationSetzenAb(Freigabe, "Englisch, Klingonisch", "Hospitation", "vhs", new DateOnly(2024, 1, 1), null, e));
        p.QualifikationSetzenAb(Freigabe, "Englisch; Englisch / Italienisch", "Hospitation", "vhs", new DateOnly(2024, 1, 1), null, e).WertText.ShouldBe("Englisch, Italienisch");
    }

    [Fact]
    public void Anzeige_nennt_die_Sprache()
        => Anforderung.Anzeige("sprachfreigabe=Englisch", Katalog).ShouldBe("Lehrfreigabe Sprachkurs (Eignungsprüfung der vhs): Englisch");
}
