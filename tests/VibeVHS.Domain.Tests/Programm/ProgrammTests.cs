using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Domain.Tests.Programm;

public class FeiertageTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2038, 4, 25)]
    public void Ostersonntag_wird_richtig_berechnet(int jahr, int monat, int tag)
    {
        FeiertageNrw.Ostersonntag(jahr).ShouldBe(new DateOnly(jahr, monat, tag));
    }

    [Fact]
    public void Ostern_abhaengige_Feiertage_2026_in_NRW()
    {
        FeiertageNrw.Name(new DateOnly(2026, 4, 3)).ShouldBe("Karfreitag");
        FeiertageNrw.Name(new DateOnly(2026, 4, 6)).ShouldBe("Ostermontag");
        FeiertageNrw.Name(new DateOnly(2026, 5, 14)).ShouldBe("Christi Himmelfahrt");
        FeiertageNrw.Name(new DateOnly(2026, 5, 25)).ShouldBe("Pfingstmontag");
        FeiertageNrw.Name(new DateOnly(2026, 6, 4)).ShouldBe("Fronleichnam");
        FeiertageNrw.Name(new DateOnly(2026, 11, 1)).ShouldBe("Allerheiligen");
        FeiertageNrw.Name(new DateOnly(2026, 11, 2)).ShouldBeNull();
        FeiertageNrw.Fuer(2026).Count.ShouldBe(11);
    }
}

public class SeriengeneratorTests
{
    private static readonly Kalender Nrw = new([new Ferienzeitraum("Herbstferien", new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 31))]);

    private static Serienregel Mittwochs(int? anzahl = null, DateOnly? ende = null) => new()
    {
        Start = new DateOnly(2026, 9, 30),
        Anzahl = anzahl,
        Ende = ende,
        Wochentage = [DayOfWeek.Wednesday],
        Beginn = new TimeOnly(18, 0),
        DauerMinuten = 90,
        FerienAuslassen = true,
    };

    [Fact]
    public void Ferien_werden_ausgelassen_und_zaehlen_nicht_zur_Anzahl()
    {
        var termine = Seriengenerator.Erzeugen(Mittwochs(anzahl: 6), Nrw);

        termine.Where(t => t.Findet).Select(t => t.Datum).ShouldBe(
        [
            new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 14),
            new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 18),
        ]);
        termine.Where(t => !t.Findet).Select(t => t.AusgelassenWeil).ShouldBe(["Ferien: Herbstferien", "Ferien: Herbstferien"]);
        termine[0].Beginn.ShouldBe(new DateTime(2026, 9, 30, 18, 0, 0));
        termine[0].Ende.ShouldBe(new DateTime(2026, 9, 30, 19, 30, 0));
    }

    [Fact]
    public void Feiertage_werden_mit_Namen_ausgelassen()
    {
        var regel = new Serienregel
        {
            Start = new DateOnly(2026, 5, 1), Ende = new DateOnly(2026, 6, 5),
            Wochentage = [DayOfWeek.Thursday], Beginn = new TimeOnly(19, 0), DauerMinuten = 60,
        };

        var ausgelassen = Seriengenerator.Erzeugen(regel, Kalender.OhneFerien).Where(t => !t.Findet).ToList();

        ausgelassen.Select(t => (t.Datum, t.AusgelassenWeil)).ShouldBe(
        [
            (new DateOnly(2026, 5, 14), "Feiertag: Christi Himmelfahrt"),
            (new DateOnly(2026, 6, 4), "Feiertag: Fronleichnam"),
        ]);
    }

    [Fact]
    public void Ohne_Ferienregel_finden_Termine_auch_in_den_Ferien_statt()
    {
        var termine = Seriengenerator.Erzeugen(Mittwochs(ende: new DateOnly(2026, 10, 31)) with { FerienAuslassen = false }, Nrw);

        termine.ShouldAllBe(t => t.Findet);
        termine.Count.ShouldBe(5);
    }

    [Fact]
    public void Feiertage_werden_immer_ausgelassen_auch_wenn_die_Serie_in_den_Ferien_weiterlaeuft()
    {
        var kalender = new Kalender([new Ferienzeitraum("Weihnachtsferien", new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 6))]);
        var regel = new Serienregel
        {
            Start = new DateOnly(2026, 12, 21), Ende = new DateOnly(2027, 1, 8), Wochentage = [DayOfWeek.Friday, DayOfWeek.Saturday],
            Beginn = new TimeOnly(10, 0), DauerMinuten = 60, FerienAuslassen = false,
        };

        var termine = Seriengenerator.Erzeugen(regel, kalender);

        termine.Single(t => t.Datum == new DateOnly(2026, 12, 26)).AusgelassenWeil.ShouldBe("Feiertag: 2. Weihnachtstag");
        termine.Single(t => t.Datum == new DateOnly(2027, 1, 1)).AusgelassenWeil.ShouldBe("Feiertag: Neujahr");
        termine.Where(t => t.Findet).Select(t => t.Datum).ShouldBe(
            [new DateOnly(2027, 1, 2), new DateOnly(2027, 1, 8)]);
    }

    [Fact]
    public void Ferienpause_kommt_als_Vorgabe_aus_der_Kursart()
    {
        new Kursart("Sprachkurs", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich, ferienAuslassen: true).FerienAuslassen.ShouldBeTrue();
        new Kursart("Bewegung", Nutzungsart.Bewegung, [], Buchungsrichtlinie.Oeffentlich).FerienAuslassen.ShouldBeFalse();
    }

    [Fact]
    public void Mehrere_Wochentage_und_Intervall()
    {
        var regel = new Serienregel
        {
            Start = new DateOnly(2026, 11, 2), Anzahl = 4, Wochentage = [DayOfWeek.Thursday, DayOfWeek.Monday],
            Beginn = new TimeOnly(9, 0), DauerMinuten = 240, IntervallWochen = 2,
        };

        Seriengenerator.Erzeugen(regel, Kalender.OhneFerien).Select(t => t.Datum).ShouldBe(
            [new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 19)]);
    }

    [Fact]
    public void Unbefristete_Serie_braucht_Horizont_und_endet_dort()
    {
        var regel = Mittwochs() with { Start = new DateOnly(2026, 11, 4) };

        Should.Throw<ArgumentException>(() => Seriengenerator.Erzeugen(regel, Nrw));
        var termine = Seriengenerator.Erzeugen(regel, Nrw, new DateOnly(2027, 1, 27));
        termine[^1].Datum.ShouldBe(new DateOnly(2027, 1, 27));
        termine.Count.ShouldBe(13);
    }
}

public class KursUndPlanungTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly Semester Herbst = new("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 1, 31));
    private static readonly Semester Fruehjahr = new("Frühjahr 2027", "27F", new DateOnly(2027, 2, 1), new DateOnly(2027, 7, 31));
    private static readonly Kursart Sprachkurs = new("Sprachkurs", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);

    private static (Ortsverzeichnis Verzeichnis, Ort Gebaeude, Ort Raum, Ort Online, Ort Treffpunkt) Orte()
    {
        var katalog = new[]
        {
            new Merkmal("bf", "Barrierefreier Zugang", "Zugänglichkeit", ausschlussgruppe: "zugang"),
            new Merkmal("nbf", "Nicht barrierefrei", "Zugänglichkeit", ausschlussgruppe: "zugang"),
            new Merkmal("bamf", "Kursort für Integrationskurse", "Zulassung", vererbbar: false, nachweisPflicht: true),
            new Merkmal("beamer", "Beamer", "Ausstattung"),
        };
        var e = Erfassung.Erst(Jetzt.AddYears(-1));
        var liegenschaft = new Ort(Ortsart.Liegenschaft, "Bildungszentrum").MitAdresse("Am Markt 1", "48282", "Emsdetten", 52.17, 7.53)
            .MitLage("Parken hinter dem Haus.");
        var gebaeude = new Ort(Ortsart.Gebaeude, "Haus A", liegenschaft.Id).MitLage("Haupteingang mit Rampe.");
        gebaeude.MerkmalSetzenAb(katalog[0], null, null, new DateOnly(2020, 1, 1), null, e);
        gebaeude.MerkmalSetzenAb(katalog[3], null, null, new DateOnly(2020, 1, 1), null, e);
        var raum = new Ort(Ortsart.Raum, "Seminarraum 2.14", gebaeude.Id, "R 2.14").MitLage("2. OG rechts.")
            .MitKapazitaet(Nutzungsart.Seminar, 16).MitKapazitaet(Nutzungsart.Vortrag, 40).MitKapazitaet(Nutzungsart.Integrationskurs, 20);
        raum.MerkmalSetzenAb(katalog[1], "Altbau-OG ohne Aufzug", null, new DateOnly(2020, 1, 1), null, e);
        raum.MerkmalSetzenAb(katalog[2], "gemeldet", "BAMF-Schreiben vom 01.10.2024", new DateOnly(2024, 10, 1), new DateOnly(2026, 12, 31), e);
        var online = new Ort(Ortsart.VirtuellerRaum, "Online-Raum 1").MitKapazitaet(Nutzungsart.Online, 100);
        var treffpunkt = new Ort(Ortsart.AllgemeinerOrt, "Treffpunkt Emsauen").MitKapazitaet(Nutzungsart.Exkursion, 25).MitKapazitaet(Nutzungsart.Seminar, 25);
        return (new Ortsverzeichnis([liegenschaft, gebaeude, raum, online, treffpunkt], katalog), gebaeude, raum, online, treffpunkt);
    }

    [Fact]
    public void Merkmale_werden_vererbt_eigenes_ueberschreibt_Ausschlussgruppe()
    {
        var (v, _, raum, _, _) = Orte();

        var merkmale = v.WirksameMerkmale(raum, new DateOnly(2026, 11, 20), Jetzt).ToDictionary(m => m.Merkmal.Schluessel);

        merkmale.Keys.Order().ShouldBe(["bamf", "beamer", "nbf"]);
        merkmale["beamer"].Geerbt.ShouldBeTrue();
        merkmale["beamer"].Herkunft.Name.ShouldBe("Haus A");
        merkmale["nbf"].Geerbt.ShouldBeFalse();
    }

    [Fact]
    public void Zulassung_gilt_nur_bis_zum_Ablauf_Eignung_nennt_den_Grund()
    {
        var (v, _, raum, _, _) = Orte();

        v.WirksameMerkmale(raum, new DateOnly(2026, 12, 31), Jetzt).ShouldContain(m => m.Merkmal.Schluessel == "bamf");
        v.WirksameMerkmale(raum, new DateOnly(2027, 1, 1), Jetzt).ShouldNotContain(m => m.Merkmal.Schluessel == "bamf");

        var imDezember = Ortsplanung.Pruefen(v, raum, Nutzungsart.Integrationskurs, ["bamf"], [new DateOnly(2026, 12, 14)], Jetzt);
        imDezember.Geeignet.ShouldBeTrue();
        imDezember.Kapazitaet.ShouldBe(20);

        var bisJanuar = Ortsplanung.Pruefen(v, raum, Nutzungsart.Integrationskurs, ["bamf"], [new DateOnly(2026, 12, 14), new DateOnly(2027, 1, 11)], Jetzt);
        bisJanuar.Geeignet.ShouldBeFalse();
        bisJanuar.Begruendung!.ShouldContain("11.01.2027");
    }

    [Fact]
    public void Gebaeude_und_fehlende_Kapazitaet_sind_nicht_geeignet()
    {
        var (v, gebaeude, raum, _, _) = Orte();

        Ortsplanung.Pruefen(v, gebaeude, Nutzungsart.Seminar, [], [new DateOnly(2026, 12, 1)], Jetzt).Begruendung!.ShouldContain("nicht buchbar");
        Ortsplanung.Pruefen(v, raum, Nutzungsart.PcKurs, [], [new DateOnly(2026, 12, 1)], Jetzt).Begruendung!.ShouldContain("PC-Kurs");
    }

    [Fact]
    public void Wegbeschreibung_setzt_sich_aus_allen_Ebenen_zusammen()
    {
        var (v, _, raum, _, _) = Orte();

        var weg = v.Wegbeschreibung(raum);

        weg.ShouldContain("Am Markt 1, 48282 Emsdetten");
        weg.ShouldContain("Haupteingang mit Rampe.");
        weg.ShouldContain("Zum Raum R 2.14: 2. OG rechts.");
        weg.ShouldContain("openstreetmap.org/?mlat=52.17&mlon=7.53");
        v.PfadAnzeige(raum).ShouldBe("Bildungszentrum › Haus A › R 2.14");
    }

    [Fact]
    public void Kapazitaet_hybrid_summiert_Orte_und_begrenzt_auf_Kurshoechstzahl()
    {
        var (v, _, raum, online, _) = Orte();
        var vortrag = new Kurs("26H-110", "Vortrag Klimaanpassung", Herbst, 1, new Kursart("Vortrag", Nutzungsart.Vortrag, [], Buchungsrichtlinie.Oeffentlich), 0, 150);
        var termin = vortrag.EinzelveranstaltungHinzufuegen(new DateTime(2026, 11, 26, 19, 0, 0), new DateTime(2026, 11, 26, 20, 30, 0),
            [new VeranstaltungsOrt(raum.Id, raum.Anzeige), new VeranstaltungsOrt(online.Id, online.Anzeige)], null);

        Ortsplanung.Kapazitaet(termin, vortrag, v).ShouldBe(140);

        var mitEigenerZahl = vortrag.EinzelveranstaltungHinzufuegen(new DateTime(2026, 11, 27, 19, 0, 0), new DateTime(2026, 11, 27, 20, 0, 0),
            [new VeranstaltungsOrt(raum.Id, raum.Anzeige), new VeranstaltungsOrt(online.Id, online.Anzeige, 200)], null);
        Ortsplanung.Kapazitaet(mitEigenerZahl, vortrag, v).ShouldBe(150);
    }

    [Fact]
    public void Konflikte_in_Raum_und_bei_Dozierenden_aber_nicht_am_Treffpunkt()
    {
        var (v, _, raum, _, treffpunkt) = Orte();
        var doz = Guid.NewGuid();
        Termin T(string kurs, int von, int bis, Guid ort, Guid? dozent = null, bool aus = false)
            => new(Guid.NewGuid(), Guid.NewGuid(), kurs, new DateTime(2026, 11, 17, von, 0, 0), new DateTime(2026, 11, 17, bis, 0, 0), [ort], dozent, aus);
        bool Exklusiv(Guid id) => v.Finden(id)!.Exklusiv;

        var konflikte = Konfliktpruefung.Finden(
        [
            T("Englisch", 18, 20, raum.Id, doz),
            T("Rhetorik", 19, 21, raum.Id),
            T("Spanisch", 20, 21, raum.Id),           // grenzt nur an Englisch: kein Konflikt mit Englisch
            T("Wandern A", 10, 12, treffpunkt.Id),
            T("Wandern B", 11, 13, treffpunkt.Id),
            T("Yoga", 19, 20, Guid.NewGuid(), doz),   // gleiche Dozentin, anderer Raum
            T("Abgesagt", 18, 21, raum.Id, aus: true),
        ], Exklusiv);

        konflikte.Count(k => k.Art == Konfliktart.Raum).ShouldBe(2); // Englisch↔Rhetorik, Rhetorik↔Spanisch
        konflikte.ShouldContain(k => k.Art == Konfliktart.Dozent && new[] { k.A.Kurs, k.B.Kurs }.Order().SequenceEqual(new[] { "Englisch", "Yoga" }));
        konflikte.ShouldNotContain(k => k.A.Kurs.StartsWith("Wandern"));
        konflikte.ShouldNotContain(k => k.A.Kurs == "Abgesagt" || k.B.Kurs == "Abgesagt");
    }

    [Fact]
    public void Nur_diese_aendern_bleibt_bei_Serienaenderung_alle_folgenden_erhalten()
    {
        var (v, _, raum, online, _) = Orte();
        var kalender = new Kalender([new Ferienzeitraum("Weihnachtsferien", new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 6))]);
        var kurs = new Kurs("26H-301", "Englisch A2", Herbst, 5, Sprachkurs, 8, 14);
        kurs.SerieFestlegen(new Serienregel
        {
            Start = new DateOnly(2026, 11, 3), Anzahl = 8, Wochentage = [DayOfWeek.Tuesday], Beginn = new TimeOnly(18, 0), DauerMinuten = 90,
            OrtIds = [raum.Id], FerienAuslassen = true,
        }, kalender, id => v.Finden(id)!.Anzeige, new DateOnly(2027, 3, 1));
        kurs.Veranstaltungen.Count.ShouldBe(8);
        kurs.Veranstaltungen.ShouldNotContain(x => x.Datum == new DateOnly(2026, 12, 29));

        var dritte = kurs.Veranstaltungen.OrderBy(x => x.Beginn).ElementAt(2);
        dritte.NurDieseAendern(dritte.Beginn.AddHours(1), dritte.Ende.AddHours(1), [new VeranstaltungsOrt(online.Id, online.Anzeige)], null, "online wegen Renovierung");

        kurs.SerieAendernAb(new DateOnly(2026, 11, 10), kurs.Serie! with { Beginn = new TimeOnly(17, 30) }, kalender, id => v.Finden(id)!.Anzeige, new DateOnly(2027, 3, 1));

        var liste = kurs.Veranstaltungen.OrderBy(x => x.Beginn).ToList();
        liste.Count.ShouldBe(8);
        liste[0].Beginn.TimeOfDay.ShouldBe(new TimeSpan(18, 0, 0));      // vor der Änderung: unverändert
        liste[1].Beginn.TimeOfDay.ShouldBe(new TimeSpan(17, 30, 0));     // alle folgenden: neue Uhrzeit
        liste[2].Ausnahme.ShouldBeTrue();                                // nur diese: bleibt, wie sie war
        liste[2].Beginn.TimeOfDay.ShouldBe(new TimeSpan(19, 0, 0));
        liste[2].Orte.Single().OrtId.ShouldBe(online.Id);
        liste.Select(x => x.Nummer).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8]);
    }

    [Fact]
    public void Unbefristeter_Kurs_wird_rollierend_fortgeschrieben()
    {
        var (v, _, raum, _, _) = Orte();
        var pilates = new Kurs("26H-540", "Pilates fortlaufend", Herbst, 7, Sprachkurs, 0, 14).MitRichtlinie(Buchungsrichtlinie.Kontingentkurs(10, 6));
        pilates.SerieFestlegen(new Serienregel
        {
            Start = new DateOnly(2026, 11, 2), Wochentage = [DayOfWeek.Monday], Beginn = new TimeOnly(18, 0), DauerMinuten = 60, OrtIds = [raum.Id],
            FerienAuslassen = false,
        }, Kalender.OhneFerien, id => v.Finden(id)!.Anzeige, new DateOnly(2026, 11, 30));

        pilates.Umfang.ShouldBe(Kursumfang.Unbefristet);
        pilates.Veranstaltungen.Count.ShouldBe(5);
        pilates.Fortschreiben(Kalender.OhneFerien, id => v.Finden(id)!.Anzeige, new DateOnly(2026, 12, 28)).ShouldBe(4);
        pilates.Veranstaltungen.Count.ShouldBe(9);
        pilates.Fortschreiben(Kalender.OhneFerien, id => v.Finden(id)!.Anzeige, new DateOnly(2026, 12, 28)).ShouldBe(0);
        pilates.Richtlinie.Belegung.ShouldBe(Belegungsart.Kontingent);
    }

    [Fact]
    public void Semesterkopie_verschiebt_um_ganze_Wochen_und_tauscht_das_Kursnummernpraefix()
    {
        var (v, _, raum, _, _) = Orte();
        var kurs = new Kurs("26H-301", "Englisch A2", Herbst, 5, Sprachkurs, 8, 14);
        kurs.EntgeltSetzenAb(Geld.Euro(98), Geld.Euro(73, 50), Herbst.Beginn, Erfassung.Erst(Jetzt));
        kurs.SerieFestlegen(new Serienregel
        {
            Start = new DateOnly(2026, 9, 1), Anzahl = 10, Wochentage = [DayOfWeek.Tuesday], Beginn = new TimeOnly(18, 0), DauerMinuten = 90,
            OrtIds = [raum.Id],
        }, Kalender.OhneFerien, id => v.Finden(id)!.Anzeige, new DateOnly(2027, 1, 31));

        var tage = Kurs.Verschiebung(Herbst, Fruehjahr);
        var kopie = kurs.KopieFuer(Fruehjahr, Kurs.NummerFuer(kurs.Kursnummer, Fruehjahr), tage, Kalender.OhneFerien, id => v.Finden(id)!.Anzeige,
            new DateOnly(2027, 7, 31), Erfassung.Erst(Jetzt));

        (tage % 7).ShouldBe(0);
        kopie.Kursnummer.ShouldBe("27F-301");
        kopie.Status.ShouldBe(Kursstatus.Entwurf);
        kopie.Veranstaltungen.Count.ShouldBe(10);
        kopie.Veranstaltungen.ShouldAllBe(x => x.Beginn.DayOfWeek == DayOfWeek.Tuesday);
        kopie.Veranstaltungen.Min(x => x.Datum).ShouldBe(new DateOnly(2026, 9, 1).AddDays(tage));
        kopie.EntgeltAm(Fruehjahr.Beginn, Jetzt)!.Betrag.ShouldBe(Geld.Euro(98));
    }
}
