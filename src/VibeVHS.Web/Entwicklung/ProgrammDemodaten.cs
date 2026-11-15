using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Programm-Demodaten für die vhs Emsdetten Greven Saerbeck: echte Ortsnamen, fiktive Gebäude, Räume und Adressen,
/// 40 Kurse über die 8 Fachbereiche (u. a. Integrationskurs, hybrider Vortrag, unbefristeter Kurs).
/// </summary>
internal static class ProgrammDemodaten
{
    private const string Von = "Demodaten";

    private sealed record Plan(string Nr, string Titel, int Fb, string Art, string Ort, DayOfWeek[] Tage, string Beginn, int Minuten,
        string Start, int? Anzahl, int Euro, int Min, int Max, string? Hybrid = null, Kursstatus Status = Kursstatus.Veroeffentlicht);

    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (await db.Semester.AnyAsync())
        {
            return;
        }

        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var e = Erfassung.Erst(jetzt, Von);

        // Merkmalskatalog (pflegbar)
        var katalog = new List<Merkmal>
        {
            new("bf", "Barrierefreier Zugang (DIN 18040)", "Zugänglichkeit", ausschlussgruppe: "zugang"),
            new("ba", "Barrierearmer Zugang", "Zugänglichkeit", ausschlussgruppe: "zugang"),
            new("nbf", "Nicht barrierefrei", "Zugänglichkeit", ausschlussgruppe: "zugang"),
            new("lift", "Aufzug", "Zugänglichkeit"),
            new("wc", "Rollstuhlgerechtes WC", "Zugänglichkeit"),
            new("hoer", "Induktive Höranlage", "Zugänglichkeit", vererbbar: false),
            new("bamf", "Kursort für Integrationskurse (beim BAMF gemeldet)", "Zulassung", vererbbar: false, nachweisPflicht: true),
            new("telc", "Prüfungsort telc", "Zulassung", vererbbar: false, nachweisPflicht: true),
            new("beamer", "Beamer", "Ausstattung", vererbbar: false),
            new("pc", "PC-Arbeitsplätze", "Ausstattung", vererbbar: false),
            new("schwing", "Schwingboden", "Ausstattung", vererbbar: false),
            new("kueche", "Lehrküche", "Ausstattung", vererbbar: false),
            new("wasser", "Waschbecken", "Ausstattung", vererbbar: false),
            new("park", "Parkplätze", "Organisation"),
            new("oepnv", "ÖPNV-Haltestelle", "Organisation"),
        };
        db.Merkmale.AddRange(katalog);
        var m = katalog.ToDictionary(x => x.Schluessel);

        // Schulferien NRW nach der amtlichen Ferienordnung (erster und letzter Ferientag)
        const string Ferienordnung = "https://www.schulministerium.nrw/service/ferienordnung-nrw-bis-2029-30";
        var ferien = new List<Ferienzeitraum>
        {
            new("Sommerferien 2026", new DateOnly(2026, 7, 20), new DateOnly(2026, 9, 1), quelle: Ferienordnung),
            new("Herbstferien 2026", new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 31), quelle: Ferienordnung),
            new("Weihnachtsferien 2026/27", new DateOnly(2026, 12, 23), new DateOnly(2027, 1, 6), quelle: Ferienordnung),
            new("Osterferien 2027", new DateOnly(2027, 3, 22), new DateOnly(2027, 4, 3), quelle: Ferienordnung),
            new("Pfingstferien 2027", new DateOnly(2027, 5, 18), new DateOnly(2027, 5, 18), quelle: Ferienordnung),
            new("Sommerferien 2027", new DateOnly(2027, 7, 19), new DateOnly(2027, 8, 31), quelle: Ferienordnung),
            new("Herbstferien 2027", new DateOnly(2027, 10, 23), new DateOnly(2027, 11, 6), quelle: Ferienordnung),
            new("Weihnachtsferien 2027/28", new DateOnly(2027, 12, 24), new DateOnly(2028, 1, 8), quelle: Ferienordnung),
            new("Osterferien 2028", new DateOnly(2028, 4, 10), new DateOnly(2028, 4, 22), quelle: Ferienordnung),
        };
        db.Ferien.AddRange(ferien);
        var kalender = new Kalender(ferien);

        var herbst = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 1, 31));
        var fruehjahr = new Semester("Frühjahr 2027", "27F", new DateOnly(2027, 2, 1), new DateOnly(2027, 7, 31));
        db.Semester.AddRange(herbst, fruehjahr);

        // Orte (echte Ortsnamen, fiktive Gebäude, Adressen und Räume)
        var orte = new List<Ort>();
        Ort Neu(Ort o)
        {
            orte.Add(o);
            return o;
        }

        void Merk(Ort o, string schluessel, string? wert = null, string? nachweis = null, DateOnly? bis = null)
            => o.MerkmalSetzenAb(m[schluessel], wert, nachweis, new DateOnly(2024, 1, 1), bis, e);

        var ems = Neu(new Ort(Ortsart.Liegenschaft, "vhs-Haus Emsdetten (fiktiv)").MitAdresse("Bildungsweg 1", "48282", "Emsdetten", 52.1730, 7.5268)
            .MitLage("Zufahrt über den Bildungsweg; Parkplatz hinter Haus B, Bushaltestelle 100 m entfernt."));
        Merk(ems, "park", "20 Plätze");
        Merk(ems, "oepnv", "Bus 230/231");
        var hausA = Neu(new Ort(Ortsart.Gebaeude, "Haus A", ems.Id).MitLage("Haus A liegt direkt an der Straße; Haupteingang mit Rampe, Tresen im Erdgeschoss links."));
        Merk(hausA, "bf");
        Merk(hausA, "lift");
        Merk(hausA, "wc", "Erdgeschoss");
        var hausB = Neu(new Ort(Ortsart.Gebaeude, "Haus B (Altbau)", ems.Id).MitLage("Haus B steht hinter Haus A am Parkplatz; Eingang über den Hof."));
        Merk(hausB, "ba", "eine Stufe (4 cm), Klingel am Eingang");

        var r = new Dictionary<string, Ort>
        {
            ["A 0.01"] = Neu(new Ort(Ortsart.Raum, "Vortragssaal", hausA.Id, "A 0.01").MitLage("Im Erdgeschoss geradeaus durch die Glastür.")
                .MitKapazitaet(Nutzungsart.Vortrag, 80).MitKapazitaet(Nutzungsart.Seminar, 40).MitKapazitaet(Nutzungsart.Pruefung, 30)),
            ["A 0.05"] = Neu(new Ort(Ortsart.Raum, "Lehrküche", hausA.Id, "A 0.05").MitLage("Im Erdgeschoss rechts, letzte Tür.")
                .MitKapazitaet(Nutzungsart.Kueche, 12)),
            ["A 1.10"] = Neu(new Ort(Ortsart.Raum, "Kursraum", hausA.Id, "A 1.10").MitLage("1. OG, mit Aufzug oder Treppe, dann links.")
                .MitKapazitaet(Nutzungsart.Seminar, 20).MitKapazitaet(Nutzungsart.Integrationskurs, 20).MitKapazitaet(Nutzungsart.Pruefung, 15)),
            ["A 1.12"] = Neu(new Ort(Ortsart.Raum, "Kursraum", hausA.Id, "A 1.12").MitLage("1. OG, rechts, zweite Tür.")
                .MitKapazitaet(Nutzungsart.Seminar, 16).MitKapazitaet(Nutzungsart.Vortrag, 24)),
            ["A 2.01"] = Neu(new Ort(Ortsart.Raum, "EDV-Raum", hausA.Id, "A 2.01").MitLage("2. OG, links die erste Tür.")
                .MitKapazitaet(Nutzungsart.PcKurs, 12).MitKapazitaet(Nutzungsart.Seminar, 16)),
            ["B 1.01"] = Neu(new Ort(Ortsart.Raum, "Bewegungsraum", hausB.Id, "B 1.01").MitLage("Erdgeschoss, erste Tür rechts. Straßenschuhe bitte im Vorraum ausziehen.")
                .MitKapazitaet(Nutzungsart.Bewegung, 16).MitKapazitaet(Nutzungsart.Vortrag, 40)),
            ["B 1.03"] = Neu(new Ort(Ortsart.Raum, "Atelier", hausB.Id, "B 1.03").MitLage("Über die Treppe im Hof ins 1. OG, Tür geradeaus.")
                .MitKapazitaet(Nutzungsart.Kreativ, 12).MitKapazitaet(Nutzungsart.Seminar, 12)),
            ["B 1.05"] = Neu(new Ort(Ortsart.Raum, "Kursraum", hausB.Id, "B 1.05").MitLage("Über die Treppe im Hof ins 1. OG, zweite Tür links.")
                .MitKapazitaet(Nutzungsart.Seminar, 20).MitKapazitaet(Nutzungsart.Integrationskurs, 18)),
        };
        Merk(r["A 0.01"], "hoer");
        Merk(r["A 0.01"], "beamer");
        Merk(r["A 0.05"], "kueche");
        Merk(r["A 0.05"], "wasser");
        Merk(r["A 1.10"], "bamf", "gemeldet", "BAMF-Meldung Kursort (fiktiv)", new DateOnly(2027, 12, 31));
        Merk(r["A 1.10"], "telc", "Lizenz", "telc-Prüfungszentrum (fiktiv)", new DateOnly(2027, 6, 30));
        Merk(r["A 1.12"], "beamer");
        Merk(r["A 2.01"], "pc", "12");
        Merk(r["A 2.01"], "beamer");
        Merk(r["B 1.01"], "schwing");
        Merk(r["B 1.03"], "nbf", "1. OG ohne Aufzug");
        Merk(r["B 1.03"], "wasser");
        Merk(r["B 1.05"], "nbf", "1. OG ohne Aufzug");
        Merk(r["B 1.05"], "bamf", "gemeldet", "BAMF-Meldung Kursort (fiktiv)", new DateOnly(2026, 12, 31)); // läuft bald ab → Aufgabe

        var greven = Neu(new Ort(Ortsart.Liegenschaft, "Bildungszentrum Greven (fiktiv)").MitAdresse("Am Bildungshaus 3", "48268", "Greven", 52.0917, 7.6086)
            .MitLage("Eingang an der Rückseite, Fahrradstellplätze am Eingang."));
        Merk(greven, "oepnv", "Bahnhof Greven, 5 Minuten zu Fuß");
        var gHaupt = Neu(new Ort(Ortsart.Gebaeude, "Hauptgebäude", greven.Id).MitLage("Ein Gebäude, alle Räume ebenerdig oder mit Aufzug."));
        Merk(gHaupt, "bf");
        Merk(gHaupt, "lift");
        r["G 1.01"] = Neu(new Ort(Ortsart.Raum, "Seminarraum", gHaupt.Id, "G 1.01").MitLage("1. OG, gegenüber dem Aufzug.")
            .MitKapazitaet(Nutzungsart.Seminar, 20).MitKapazitaet(Nutzungsart.Vortrag, 40));
        Merk(r["G 1.01"], "beamer");
        r["G 1.02"] = Neu(new Ort(Ortsart.Raum, "Kursraum", gHaupt.Id, "G 1.02").MitLage("1. OG, links neben G 1.01.")
            .MitKapazitaet(Nutzungsart.Seminar, 14).MitKapazitaet(Nutzungsart.Kreativ, 12));
        r["G 0.10"] = Neu(new Ort(Ortsart.Raum, "Bewegungsraum", gHaupt.Id, "G 0.10").MitLage("Erdgeschoss, am Ende des Flurs.")
            .MitKapazitaet(Nutzungsart.Bewegung, 18));

        var saerbeck = Neu(new Ort(Ortsart.Liegenschaft, "Bürgerhaus Saerbeck (fiktiv)").MitAdresse("Dorfplatz 2", "48369", "Saerbeck", 52.1745, 7.6330)
            .MitLage("Am Dorfplatz; Parkplätze vor dem Haus."));
        Merk(saerbeck, "park");
        var sHaus = Neu(new Ort(Ortsart.Gebaeude, "Bürgerhaus", saerbeck.Id).MitLage("Haupteingang ebenerdig."));
        Merk(sHaus, "bf");
        r["S Saal"] = Neu(new Ort(Ortsart.Raum, "Saal", sHaus.Id, "S Saal").MitLage("Erdgeschoss, große Flügeltür.")
            .MitKapazitaet(Nutzungsart.Vortrag, 60).MitKapazitaet(Nutzungsart.Seminar, 25).MitKapazitaet(Nutzungsart.Bewegung, 20));
        r["S Clubraum"] = Neu(new Ort(Ortsart.Raum, "Clubraum", sHaus.Id, "S Clubraum").MitLage("Erdgeschoss, links neben dem Saal.")
            .MitKapazitaet(Nutzungsart.Seminar, 12));

        r["Online 1"] = Neu(new Ort(Ortsart.VirtuellerRaum, "vhs.cloud Raum 1", kurzname: "Online 1").MitKapazitaet(Nutzungsart.Online, 100)
            .MitKapazitaet(Nutzungsart.Seminar, 30));
        r["Online 2"] = Neu(new Ort(Ortsart.VirtuellerRaum, "vhs.cloud Raum 2", kurzname: "Online 2").MitKapazitaet(Nutzungsart.Online, 50)
            .MitKapazitaet(Nutzungsart.Seminar, 20));
        r["Treffpunkt"] = Neu(new Ort(Ortsart.AllgemeinerOrt, "Treffpunkt Emsufer (Parkplatz an der Brücke)", kurzname: "Treffpunkt Ems")
            .MitAdresse("Am Emsufer", "48282", "Emsdetten", 52.1782, 7.5321).MitLage("Treffpunkt am Infoschild auf dem Parkplatz an der Brücke.")
            .MitKapazitaet(Nutzungsart.Exkursion, 25).MitKapazitaet(Nutzungsart.Bewegung, 20));
        db.Orte.AddRange(orte);

        // Kursarten mit Nutzungsart, Merkmalen und Vorlage für die Buchungsrichtlinie
        var arten = new Dictionary<string, Kursart>
        {
            ["Sprache"] = new("Sprachkurs", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich, ferienAuslassen: true),
            ["Seminar"] = new("Seminar", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich, ferienAuslassen: true),
            ["Vortrag"] = new("Vortrag", Nutzungsart.Vortrag, [], Buchungsrichtlinie.Oeffentlich),
            ["Integration"] = new("Integrationskurs", Nutzungsart.Integrationskurs, ["bamf"], Buchungsrichtlinie.Integrationskurs, Sichtbarkeit.Intern, ferienAuslassen: true),
            ["EDV"] = new("EDV-Kurs", Nutzungsart.PcKurs, ["pc"], Buchungsrichtlinie.Oeffentlich),
            ["Bewegung"] = new("Bewegungskurs", Nutzungsart.Bewegung, [], Buchungsrichtlinie.Oeffentlich),
            ["Kochen"] = new("Kochkurs", Nutzungsart.Kueche, ["kueche"], Buchungsrichtlinie.Oeffentlich),
            ["Kreativ"] = new("Kreativkurs", Nutzungsart.Kreativ, [], Buchungsrichtlinie.Oeffentlich),
            ["Exkursion"] = new("Exkursion", Nutzungsart.Exkursion, [], Buchungsrichtlinie.Oeffentlich),
            ["Firma"] = new("Firmenkurs", Nutzungsart.Seminar, [], Buchungsrichtlinie.Firmenkurs, Sichtbarkeit.Geschlossen),
            ["Fortlaufend"] = new("Fortlaufender Kurs (Kontingent)", Nutzungsart.Bewegung, [], Buchungsrichtlinie.Kontingentkurs(10, 6)),
        };
        db.Kursarten.AddRange(arten.Values);

        var mo = DayOfWeek.Monday;
        var di = DayOfWeek.Tuesday;
        var mi = DayOfWeek.Wednesday;
        var don = DayOfWeek.Thursday;
        var fr = DayOfWeek.Friday;
        var sa = DayOfWeek.Saturday;
        Plan[] plaene =
        [
            new("26H-101", "Klimaanpassung im eigenen Garten", 1, "Vortrag", "A 0.01", [don], "19:00", 90, "2026-11-26", 1, 0, 5, 120, Hybrid: "Online 1"),
            new("26H-102", "Exkursion: Vögel an der Ems", 1, "Exkursion", "Treffpunkt", [sa], "08:00", 180, "2026-11-21", 1, 12, 6, 20),
            new("26H-103", "Repair-Café: Reparieren lernen", 1, "Kreativ", "B 1.03", [sa], "10:00", 240, "2026-11-28", 2, 25, 6, 12),
            new("26H-104", "Nachhaltig einkaufen und kochen", 1, "Kochen", "A 0.05", [mi], "18:00", 180, "2026-12-02", 2, 39, 6, 12),
            new("26H-201", "Aquarellmalerei für Fortgeschrittene", 2, "Kreativ", "B 1.03", [mo], "18:30", 150, "2026-10-05", 8, 132, 6, 10),
            new("26H-202", "Fotografie: Grundlagen", 2, "Kreativ", "G 1.02", [sa], "10:00", 360, "2026-11-21", 2, 115, 6, 12),
            new("26H-203", "Töpfern am Abend", 2, "Kreativ", "B 1.03", [di], "19:00", 150, "2026-09-29", 8, 128, 6, 10),
            new("26H-204", "Gitarre für Einsteiger", 2, "Seminar", "S Clubraum", [don], "18:00", 60, "2026-09-24", 10, 89, 5, 8),
            new("27F-205", "Kalligrafie", 2, "Kreativ", "B 1.03", [mi], "18:00", 120, "2027-02-24", 6, 79, 6, 10, Status: Kursstatus.Entwurf),
            new("26H-301", "Rhetorik für Ehrenamtliche", 3, "Seminar", "A 1.12", [di], "18:30", 90, "2026-11-17", 5, 49, 6, 14),
            new("26H-302", "Kommunalpolitik verstehen", 3, "Vortrag", "S Saal", [mi], "19:30", 90, "2026-11-25", 1, 0, 5, 60, Hybrid: "Online 2"),
            new("26H-303", "Erziehungskompetenz stärken", 3, "Seminar", "G 1.02", [mo], "19:00", 120, "2026-10-05", 6, 59, 6, 14),
            new("26H-304", "Erste Hilfe am Kind", 3, "Seminar", "G 1.01", [sa], "09:00", 480, "2026-12-05", 1, 45, 8, 16),
            new("26H-401", "Integrationskurs allgemein, Modul 3", 4, "Integration", "A 1.10", [mo, di, mi, don, fr], "08:30", 240, "2026-11-02", 25, 0, 12, 20),
            new("26H-402", "Alphabetisierungskurs, Modul 1", 4, "Integration", "B 1.05", [mo, di, mi, don], "13:00", 180, "2026-10-12", 32, 0, 8, 15),
            new("26H-403", "Deutsch B1 – Prüfungsvorbereitung", 4, "Sprache", "A 1.12", [mi], "17:00", 90, "2026-09-30", 10, 98, 6, 14),
            new("26H-404", "Deutsch am Arbeitsplatz (Firmenkurs)", 4, "Firma", "G 1.02", [mi], "14:00", 90, "2026-10-07", 10, 0, 6, 12),
            new("26H-501", "Englisch A2 – Konversation", 5, "Sprache", "A 1.12", [di], "18:00", 90, "2026-09-29", 12, 98, 8, 14),
            new("26H-502", "Spanisch A1 – Einstieg", 5, "Sprache", "G 1.01", [mi], "19:30", 90, "2026-11-18", 10, 98, 8, 14),
            new("26H-503", "Niederländisch für die Grenzregion", 5, "Sprache", "A 1.12", [don], "18:00", 90, "2026-09-24", 12, 98, 6, 14),
            new("26H-504", "Französisch B1", 5, "Sprache", "G 1.02", [don], "18:00", 90, "2026-09-24", 12, 98, 8, 14, Status: Kursstatus.Abgesagt),
            new("26H-505", "Plattdeutsch snacken", 5, "Sprache", "S Clubraum", [mo], "19:00", 90, "2026-10-05", 8, 59, 5, 12),
            new("26H-506", "Italienisch A1 online", 5, "Sprache", "Online 2", [di], "19:00", 90, "2026-10-06", 10, 89, 6, 12),
            new("27F-501", "Englisch B1", 5, "Sprache", "A 1.12", [di], "18:00", 90, "2027-02-23", 12, 98, 8, 14, Status: Kursstatus.Entwurf),
            new("26H-601", "Excel für Einsteiger", 6, "EDV", "A 2.01", [sa], "09:00", 360, "2026-11-21", 2, 129, 6, 12),
            new("26H-602", "Bildungsurlaub: Projektmanagement kompakt", 6, "Seminar", "G 1.01", [mo, di, mi, don, fr], "09:00", 420, "2026-11-30", 5, 345, 8, 16),
            new("26H-603", "Smartphone-Sprechstunde für Ältere", 6, "EDV", "A 2.01", [don], "10:00", 90, "2026-10-01", 8, 45, 4, 10),
            new("26H-604", "Buchführung Grundlagen", 6, "Seminar", "A 1.12", [mo], "18:00", 180, "2026-10-05", 8, 149, 6, 14),
            new("27F-601", "Word für Einsteiger", 6, "EDV", "A 2.01", [sa], "09:00", 360, "2027-03-06", 2, 129, 6, 12, Status: Kursstatus.Entwurf),
            new("26H-701", "Rückenfit", 7, "Bewegung", "B 1.01", [di], "17:00", 60, "2026-10-06", 10, 62, 8, 14),
            new("26H-702", "Progressive Muskelentspannung", 7, "Bewegung", "G 0.10", [mi], "18:00", 75, "2026-09-30", 8, 59, 6, 14),
            new("26H-703", "Gesund kochen mit wenig Geld", 7, "Kochen", "A 0.05", [don], "18:00", 180, "2026-11-19", 3, 49, 6, 12),
            new("26H-704", "Pilates fortlaufend (10er-Karte)", 7, "Fortlaufend", "B 1.01", [mo], "18:00", 60, "2026-09-07", null, 89, 0, 14),
            new("26H-801", "Yoga am Abend", 8, "Bewegung", "B 1.01", [don], "19:00", 90, "2026-10-08", 10, 84, 8, 16),
            new("26H-802", "Zumba", 8, "Bewegung", "G 0.10", [mo], "19:00", 60, "2026-10-05", 10, 59, 8, 18),
            new("26H-803", "Nordic Walking an der Ems", 8, "Bewegung", "Treffpunkt", [sa], "09:00", 90, "2026-09-26", 8, 49, 6, 15),
            new("26H-804", "Qigong", 8, "Bewegung", "B 1.01", [mi], "10:00", 75, "2026-09-30", 10, 69, 6, 14),
            new("26H-805", "Rückengymnastik in Saerbeck", 8, "Bewegung", "S Saal", [di], "18:00", 60, "2026-10-06", 10, 59, 8, 16),
            new("26H-806", "Fit ab 60", 8, "Bewegung", "G 0.10", [don], "10:00", 60, "2026-10-01", 10, 59, 6, 14),
            new("27F-801", "Yoga am Morgen", 8, "Bewegung", "B 1.01", [di], "09:00", 90, "2027-02-23", 10, 84, 8, 16, Status: Kursstatus.Entwurf),
        ];

        // Dozierende aus den Testdaten, passend zum Fachbereich
        var dozierende = await db.Personen.IgnoreAutoIncludes().AsNoTracking()
            .Where(p => (p.Rollen & Personenrolle.Dozierende) == Personenrolle.Dozierende && p.Status != Personenstatus.Zusammengefuehrt)
            .OrderBy(p => p.Sortierschluessel)
            .Select(p => new { p.Id, p.Anzeigename, p.Fachbereiche })
            .ToListAsync();
        var stadtwerke = await db.Organisationen.FirstOrDefaultAsync(o => o.Name.StartsWith("Stadtwerke"));
        var horizont = DateOnly.FromDateTime(jetzt.Date).AddDays(12 * 7);
        var zaehler = 0;
        foreach (var p in plaene)
        {
            var semester = p.Nr.StartsWith("27F", StringComparison.Ordinal) ? fruehjahr : herbst;
            var kurs = new Kurs(p.Nr, p.Titel, semester, p.Fb, arten[p.Art], p.Min, p.Max);
            var passend = dozierende.Where(d => d.Fachbereiche.Contains(p.Fb)).ToList();
            var auswahl = passend.Count > 0 ? passend : dozierende;
            if (auswahl.Count > 0)
            {
                var d = auswahl[zaehler++ % auswahl.Count];
                kurs.MitTeam([new KursDozent(d.Id, d.Anzeigename, true)]);
            }

            if (p.Art == "Firma" && stadtwerke is not null)
            {
                kurs.MitSichtbarkeit(Sichtbarkeit.Geschlossen, stadtwerke.Id, stadtwerke.Name);
            }

            kurs.EntgeltSetzenAb(Geld.Euro(p.Euro), p.Euro > 0 ? Geld.Euro(p.Euro * 3 / 4) : null, semester.Beginn, e);
            var ortIds = new List<Guid> { r[p.Ort].Id };
            if (p.Hybrid is not null)
            {
                ortIds.Add(r[p.Hybrid].Id);
            }

            kurs.SerieFestlegen(new Serienregel
            {
                Start = DateOnly.Parse(p.Start, System.Globalization.CultureInfo.InvariantCulture),
                Anzahl = p.Anzahl,
                Wochentage = [.. p.Tage],
                Beginn = TimeOnly.Parse(p.Beginn, System.Globalization.CultureInfo.InvariantCulture),
                DauerMinuten = p.Minuten,
                OrtIds = ortIds,
                FerienAuslassen = arten[p.Art].FerienAuslassen,
            }, kalender, id => orte.First(o => o.Id == id).Anzeige, horizont);
            kurs.StatusSetzen(p.Status);
            db.Kurse.Add(kurs);
        }

        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, "Demodaten.Programm", null, null,
            $"Programm-Demodaten angelegt: 2 Semester, {orte.Count} Orte, {katalog.Count} Merkmale, {arten.Count} Kursarten, {plaene.Length} Kurse"));
        await db.SaveChangesAsync();
    }
}
