using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

/// <summary>Einheitliche Briefanrede: förmlich (Standard) oder informell, Titel vor dem Nachnamen, ohne Anrede mit vollem Namen.</summary>
public class BriefanredeTests
{
    [Theory]
    [InlineData("Frau", "Dr.", "Eva", "Muster", Anredestil.Foermlich, "Sehr geehrte Frau Dr. Muster")]
    [InlineData("Herr", "Prof. Dr.", "Kai", "Muster", Anredestil.Foermlich, "Sehr geehrter Herr Prof. Dr. Muster")]
    [InlineData("Herr", null, "Kai", "von Berg", Anredestil.Foermlich, "Sehr geehrter Herr von Berg")]
    [InlineData("Frau", "Dr.", "Eva", "Muster", Anredestil.Informell, "Guten Tag Frau Dr. Muster")]
    [InlineData(null, "Dr.", "Kim", "Muster", Anredestil.Foermlich, "Guten Tag Dr. Kim Muster")]
    [InlineData("Divers", null, "Kim", "Muster", Anredestil.Informell, "Guten Tag Kim Muster")]
    [InlineData("Keine Anrede", null, "Kim", "Muster", Anredestil.Foermlich, "Guten Tag Kim Muster")]
    public void Bilden(string? anrede, string? titel, string vorname, string nachname, Anredestil stil, string erwartet)
        => Briefanrede.Bilden(anrede, titel, vorname, nachname, stil).ShouldBe(erwartet);

    [Fact]
    public void Person_nutzt_Anrede_Titel_und_aktuellen_Namen()
    {
        var jetzt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(1));
        var p = Person.Anlegen("Eva", "Muster", new DateOnly(2020, 1, 1), jetzt.AddYears(-5));
        p.Briefanrede(jetzt).ShouldBe("Guten Tag Eva Muster");
        p.TitelSetzen("Dr.");
        p.AngabeAendern(MerkmalArt.Anrede, "Frau", new DateOnly(2020, 1, 1), VibeVHS.Domain.Gemeinsam.Erfassung.Erst(jetzt), korrektur: true);
        p.Briefanrede(jetzt).ShouldBe("Sehr geehrte Frau Dr. Muster");
        p.Briefanrede(jetzt, Anredestil.Informell).ShouldBe("Guten Tag Frau Dr. Muster");
    }

    [Theory]
    [InlineData("Frau", Anredestil.Informell, "Hallo Kim")]
    [InlineData("Frau", Anredestil.Foermlich, "Liebe Kim")]
    [InlineData("Herr", Anredestil.Foermlich, "Lieber Kim")]
    [InlineData(null, Anredestil.Foermlich, "Hallo Kim")]
    public void Du_spricht_mit_Vornamen_an(string? anrede, Anredestil stil, string erwartet)
        => Briefanrede.Bilden(anrede, "Dr.", "Kim", "Muster", stil, du: true).ShouldBe(erwartet);

    [Fact]
    public void Ansprache_wird_gelesen_und_geschrieben()
    {
        foreach (var a in new[] { new Ansprache(false, Anredestil.Foermlich), new Ansprache(true, Anredestil.Informell) })
        {
            Ansprache.Lesen(a.Speicherwert).ShouldBe(a);
        }

        Ansprache.Lesen(null).ShouldBeNull();
        Ansprache.Lesen("Quatsch").ShouldBeNull();
        new Ansprache(true, Anredestil.Informell).Text("Sie haben", "du hast").ShouldBe("du hast");
    }

    [Fact]
    public void Titel_ist_versioniert_Promotion_ab_Datum_und_Korrektur()
    {
        var jetzt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(1));
        var p = Person.Anlegen("Eva", "Muster", new DateOnly(2020, 1, 1), jetzt.AddYears(-5));
        p.AngabeAendern(MerkmalArt.Titel, "Dr.", new DateOnly(2026, 12, 1), VibeVHS.Domain.Gemeinsam.Erfassung.Aenderung(jetzt, "Promotion"), korrektur: false);
        p.StandAm(new DateOnly(2026, 11, 30), jetzt).Titel.ShouldBeNull();
        p.StandAm(new DateOnly(2026, 12, 1), jetzt).Titel.ShouldBe("Dr.");
        p.Titel.ShouldBeNull(); // heute noch ohne Titel …
        p.StichzeitSetzen(jetzt.AddDays(20)); // … ab 01.12. berechnet (keine Kopie, kein Nachziehen nötig)
        (p.Titel, p.Anzeigename).ShouldBe(("Dr.", "Dr. Eva Muster"));
        Should.Throw<InvalidOperationException>(() => p.TitelSetzen("Prof."));
    }

    [Fact]
    public void Anonymisierung_behaelt_nur_das_letzte_Geschlecht_ohne_Verlauf()
    {
        var jetzt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(1));
        var p = Person.Anlegen("Kim", "Muster", new DateOnly(2020, 1, 1), jetzt.AddYears(-5));
        p.TitelSetzen("Dr.");
        p.AngabeAendern(MerkmalArt.Geschlecht, "männlich", new DateOnly(2020, 1, 1), VibeVHS.Domain.Gemeinsam.Erfassung.Erst(jetzt.AddYears(-5)), korrektur: false);
        p.AngabeAendern(MerkmalArt.Geschlecht, "weiblich", new DateOnly(2025, 1, 1), VibeVHS.Domain.Gemeinsam.Erfassung.Aenderung(jetzt.AddYears(-2), "x"), korrektur: false);
        p.Anonymisieren(jetzt);
        p.Merkmale.Where(m => m.Art == MerkmalArt.Geschlecht).Select(m => m.Inhalt).Distinct().ShouldBe(["weiblich"]);
        p.Merkmale.Single(m => m.Art == MerkmalArt.Titel).Inhalt.ShouldBeEmpty();
        p.Titel.ShouldBeNull();
    }

    [Fact]
    public void Nur_ein_Name_laut_Ausweis_Anzeige_Sortierung_und_Anrede()
    {
        var jetzt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(1));
        var p = Person.Anlegen("", "Rahimi", new DateOnly(2025, 1, 1), jetzt.AddYears(-1));
        var name = p.AktuellerName(jetzt);
        (name.NurEinName, name.Anzeige, p.Anzeigename, p.Sortiername).ShouldBe((true, "Rahimi", "Rahimi", "rahimi"));
        p.Briefanrede(jetzt).ShouldBe("Guten Tag Rahimi");
        p.AngabeAendern(MerkmalArt.Anrede, "Herr", new DateOnly(2025, 1, 1), VibeVHS.Domain.Gemeinsam.Erfassung.Erst(jetzt), korrektur: true);
        p.Briefanrede(jetzt).ShouldBe("Sehr geehrter Herr Rahimi");
        Briefanrede.Bilden("Herr", null, "", "Rahimi", Anredestil.Informell, du: true).ShouldBe("Hallo Rahimi");
        Should.Throw<ArgumentException>(() => Person.Anlegen("Kai", "", new DateOnly(2025, 1, 1), jetzt)); // Nachname bleibt Pflicht
    }
}
