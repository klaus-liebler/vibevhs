using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Domain.Tests.Programm;

public class DozentenpruefungTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(1));

    private static readonly Merkmal Bamf = new("bamf-lk", "BAMF-Lehrkraftzulassung Integrationskurs", "Qualifikation", nachweisPflicht: true,
        bezug: MerkmalBezug.Person, gesetzlich: true, werte: ["direkt", "mit Auflagen", "nach Zusatzqualifizierung"]);

    private static readonly Merkmal Telc = new("telc-pruefer", "Prüferlizenz telc", "Prüfung", nachweisPflicht: true, bezug: MerkmalBezug.Person);

    private static readonly IReadOnlyDictionary<string, Merkmal> Katalog = new Dictionary<string, Merkmal> { [Bamf.Schluessel] = Bamf, [Telc.Schluessel] = Telc };

    private static Person Lehrkraft()
    {
        var p = Person.Anlegen("Maria", "Rossi", new DateOnly(2019, 1, 1), Jetzt.AddYears(-7));
        p.RolleHinzufuegen(Personenrolle.Dozierende, new DateOnly(2019, 1, 1), Erfassung.Erst(Jetzt.AddYears(-7)));
        return p;
    }

    private static Person MitZulassungBis(DateOnly bis)
    {
        var p = Lehrkraft();
        p.QualifikationSetzenAb(Bamf, "direkt", "Bescheid Az. 81-4711", "BAMF", new DateOnly(2026, 1, 1), bis, Erfassung.Erst(Jetzt.AddMonths(-10)));
        return p;
    }

    [Fact]
    public void Geprueft_wird_am_Veranstaltungstag()
    {
        var p = MitZulassungBis(new DateOnly(2026, 12, 31));
        Dozentenpruefung.AmTag(p, ["bamf-lk"], Katalog, new DateOnly(2026, 12, 31), Jetzt).Code.ShouldBe(Eignungscode.Geeignet);

        var danach = Dozentenpruefung.AmTag(p, ["bamf-lk"], Katalog, new DateOnly(2027, 1, 5), Jetzt);
        danach.Code.ShouldBe(Eignungscode.Abgelaufen);
        danach.Begruendung!.ShouldContain("abgelaufen am 31.12.2026");
        danach.AusnahmeMoeglich.ShouldBeFalse(); // gesetzlich

        Dozentenpruefung.AmTag(p, ["bamf-lk"], Katalog, new DateOnly(2025, 12, 1), Jetzt).Code.ShouldBe(Eignungscode.NochNichtGueltig);
        var ohne = Dozentenpruefung.AmTag(Lehrkraft(), ["bamf-lk"], Katalog, new DateOnly(2026, 12, 1), Jetzt);
        ohne.Code.ShouldBe(Eignungscode.MerkmalFehlt);
        ohne.Begruendung.ShouldBe("„BAMF-Lehrkraftzulassung Integrationskurs“ fehlt.");
    }

    [Fact]
    public void Ablauf_mitten_in_einer_Serie_betrifft_nur_die_spaeteren_Termine()
    {
        var p = MitZulassungBis(new DateOnly(2026, 12, 31));
        var tage = Enumerable.Range(0, 12).Select(i => new DateOnly(2026, 11, 17).AddDays(7 * i)).ToList(); // 17.11.2026 … 02.02.2027
        var e = Dozentenpruefung.Pruefen(p, ["bamf-lk"], Katalog, tage, Jetzt);

        e.Geeignet.ShouldBeFalse();
        e.ErsterFehltag!.Tag.ShouldBe(new DateOnly(2027, 1, 5));
        e.Tage.Count(t => t.Geeignet).ShouldBe(7);
        e.Begruendung!.ShouldContain("Betroffen sind 5 von 12 Terminen ab 05.01.2027");
        e.Begruendung!.ShouldContain("nicht zulässig");

        // Verlängerung ab 01.01.2027 schließt die Lücke
        p.QualifikationSetzenAb(Bamf, "direkt", "Verlängerung Az. 81-4711-2", "BAMF", new DateOnly(2027, 1, 1), new DateOnly(2029, 12, 31),
            Erfassung.Aenderung(Jetzt, "Verlängerung"));
        Dozentenpruefung.Pruefen(p, ["bamf-lk"], Katalog, tage, Jetzt).Geeignet.ShouldBeTrue();
    }

    [Fact]
    public void Ausnahme_ist_nur_bei_nicht_gesetzlichen_Merkmalen_erlaubt()
    {
        var kursId = Guid.NewGuid();
        var p = Lehrkraft();
        Should.Throw<InvalidOperationException>(() => new Qualifikationsausnahme(kursId, p.Id, Bamf, "Kein anderer Ersatz", "FBL", Jetzt))
            .Message.ShouldContain("gesetzlich");

        var tag = new DateOnly(2026, 12, 1);
        var ohneAusnahme = Dozentenpruefung.AmTag(p, ["telc-pruefer"], Katalog, tag, Jetzt);
        ohneAusnahme.Geeignet.ShouldBeFalse();
        ohneAusnahme.AusnahmeMoeglich.ShouldBeTrue();

        var ausnahme = new Qualifikationsausnahme(kursId, p.Id, Telc, "Prüfung nur als Zweitprüferin unter Aufsicht", "Varvara Schatz (Fachbereichsleitung)", Jetzt);
        var mit = Dozentenpruefung.AmTag(p, ["telc-pruefer"], Katalog, tag, Jetzt, [ausnahme]);
        mit.Code.ShouldBe(Eignungscode.Ausnahme);
        mit.Geeignet.ShouldBeTrue();

        // Die Ausnahme hilft nicht beim gesetzlichen Merkmal
        Dozentenpruefung.AmTag(p, ["telc-pruefer", "bamf-lk"], Katalog, tag, Jetzt, [ausnahme]).Geeignet.ShouldBeFalse();
    }

    [Fact]
    public void Beschaeftigungsart_wechselt_ab_Datum_und_bleibt_fuer_die_Vergangenheit_erhalten()
    {
        var p = Lehrkraft();
        p.BeschaeftigungAendernAb(Beschaeftigungsart.Freiberuflich, new DateOnly(2019, 1, 1), Erfassung.Erst(Jetzt.AddYears(-7)));
        var gemeldet = new DateTimeOffset(2025, 12, 10, 10, 0, 0, TimeSpan.FromHours(1));
        p.BeschaeftigungAendernAb(Beschaeftigungsart.Festangestellt, new DateOnly(2026, 1, 1), Erfassung.Aenderung(gemeldet, "Arbeitsvertrag vom 08.12.2025"));

        p.BeschaeftigungAm(new DateOnly(2025, 12, 31), Jetzt).ShouldBe(Beschaeftigungsart.Freiberuflich);
        p.BeschaeftigungAm(new DateOnly(2026, 1, 1), Jetzt).ShouldBe(Beschaeftigungsart.Festangestellt);
        p.BeschaeftigungAm(new DateOnly(2026, 2, 1), gemeldet.AddDays(-1)).ShouldBe(Beschaeftigungsart.Freiberuflich); // Wissensstand vor der Meldung
        p.StandAm(new DateOnly(2026, 6, 1), Jetzt).Beschaeftigung.ShouldBe(Beschaeftigungsart.Festangestellt);
        p.AlleVersionen.Count(v => v.Datenart == "Beschäftigung").ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Qualifikation_verlangt_Nachweis_erlaubten_Wert_und_ein_Personenmerkmal()
    {
        var p = Lehrkraft();
        var e = Erfassung.Erst(Jetzt);
        Should.Throw<ArgumentException>(() => p.QualifikationSetzenAb(Bamf, "direkt", null, "BAMF", new DateOnly(2026, 1, 1), null, e)).Message.ShouldContain("Nachweis");
        Should.Throw<ArgumentException>(() => p.QualifikationSetzenAb(Bamf, "irgendwie", "Az. 1", "BAMF", new DateOnly(2026, 1, 1), null, e)).Message.ShouldContain("mit Auflagen");
        var ortsmerkmal = new Merkmal("bamf", "Kursort für Integrationskurse", "Zulassung");
        Should.Throw<ArgumentException>(() => p.QualifikationSetzenAb(ortsmerkmal, null, "Az. 1", null, new DateOnly(2026, 1, 1), null, e)).Message.ShouldContain("Orte");

        var q = p.QualifikationSetzenAb(Bamf, "mit Auflagen", "Az. 1", "BAMF", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), e);
        q.GueltigBisEinschliesslich.ShouldBe(new DateOnly(2026, 12, 31));
        q.Datenart.ShouldBe("Qualifikationen");
        q.Wert.ShouldContain("mit Auflagen");
    }
}
