using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Dokumente;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Anmeldung;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Durchfuehrung;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

public class DurchfuehrungTests
{
    private static readonly Benutzer Tresen = new("katja.friesen", "Katja Friesen", Rolle.Teilnehmerservice);
    private static readonly TimeSpan Mez = TimeSpan.FromHours(1);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private sealed class LeereKonten : IBenutzerkonten
    {
        public Task<IReadOnlyList<Benutzerkonto>> AlleAsync(CancellationToken abbruch = default) => Task.FromResult<IReadOnlyList<Benutzerkonto>>([]);

        public Task<Benutzerkonto?> FindenAsync(string login, CancellationToken abbruch = default) => Task.FromResult<Benutzerkonto?>(null);
    }

    private static CheckinZugang Zugang(TestDatenbank t, VibeVhsDbContext db)
        => new(new ZugangstokenSpeicher(db), new OeffentlicheAdresse(new Uri("https://vhs.example/")), new AuditProtokoll(db, new TestBenutzer(Tresen), t.Uhr),
            new TestBenutzer(Tresen), t.Uhr);

    private static AnwesenheitDienst Anwesenheit(TestDatenbank t, VibeVhsDbContext db, Benutzer? wer = null)
    {
        var benutzer = new TestBenutzer(wer ?? Tresen);
        return new AnwesenheitDienst(new DurchfuehrungSpeicher(db), new AnmeldungSpeicher(db), new ProgrammSpeicher(db), new PersonenSpeicher(db), new LeereKonten(),
            new Termincodes(new ZugangstokenSpeicher(db), t.Uhr), Zugang(t, db), new AuditProtokoll(db, benutzer, t.Uhr), benutzer, t.Uhr);
    }

    private static DokumentDienst Dokumente(TestDatenbank t, VibeVhsDbContext db)
    {
        var benutzer = new TestBenutzer(Tresen);
        return new DokumentDienst(new DokumentSpeicher(db), new AnmeldungSpeicher(db), new ProgrammSpeicher(db), new PersonenSpeicher(db), new DurchfuehrungSpeicher(db),
            new StammdatenSpeicher(db), new BriefPdf(new QrCodeErzeuger()), new Uebersetzungslinks(new Uri("https://vhs.example/")), Zugang(t, db),
            new AuditProtokoll(db, benutzer, t.Uhr), benutzer, t.Uhr);
    }

    private static string Geheimnis(Uri link) => link.Segments[^1];

    /// <summary>Kurs (dienstags 18–19:30 ab <paramref name="start"/>) mit angemeldeten Personen; optional zusätzlicher Online-Raum.</summary>
    private static async Task<(Kurs Kurs, List<Person> Personen, List<Belegung> Belegungen, Ort Raum, Ort Online)> KursAsync(TestDatenbank t, DateOnly start, int termine,
        Buchungsrichtlinie? richtlinie = null, int anzahl = 1, Person? person = null, Person? leitung = null, DateTimeOffset? gebucht = null)
    {
        var raum = new Ort(Ortsart.Raum, "Kursraum", null, "A 1.12").MitKapazitaet(Nutzungsart.Seminar, 14);
        var online = new Ort(Ortsart.VirtuellerRaum, "vhs.cloud Raum 1", kurzname: "Online 1").MitKapazitaet(Nutzungsart.Online, 50);
        var semester = new Semester("Test", "TS", start.AddDays(-30), start.AddDays(365));
        var art = new Kursart("Sprachkurs", Nutzungsart.Seminar, [], richtlinie ?? Buchungsrichtlinie.Oeffentlich);
        var kurs = new Kurs("TS-501", "Englisch A1", semester, 5, art, 1, 14);
        if (leitung is not null)
        {
            kurs.MitTeam([new KursDozent(leitung.Id, true)]);
        }

        kurs.EntgeltSetzenAb(Geld.Euro(90), null, semester.Beginn, Erfassung.Erst(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        kurs.SerieFestlegen(new Serienregel
        {
            Start = start, Anzahl = termine, Wochentage = [DayOfWeek.Tuesday], Beginn = new TimeOnly(18, 0), DauerMinuten = 90, OrtIds = [raum.Id, online.Id],
        }, Kalender.OhneFerien, start.AddDays(365));
        kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
        var personen = person is not null
            ? [person]
            : Enumerable.Range(1, anzahl).Select(i => Person.Anlegen($"Vorname{i}", $"Nachname{i}", new DateOnly(2020, 1, 1), new DateTimeOffset(2020, 1, 1, 9, 0, 0, TimeSpan.Zero))).ToList();
        var belegungen = personen.Select(p => Belegung.Anlegen(kurs, p.Id, false, Buchungsweg.Tresen, new Buchender("Test", null, null), null, Zahlart.Bar,
            gebucht ?? new DateTimeOffset(start.AddDays(-10).ToDateTime(new TimeOnly(10, 0)), Mez))).ToList();

        await using var db = t.NeuerKontext();
        db.Orte.AddRange(raum, online);
        db.Semester.Add(semester);
        db.Kursarten.Add(art);
        db.Kurse.Add(kurs);
        db.Personen.AddRange(personen);
        if (leitung is not null)
        {
            db.Personen.Add(leitung);
        }

        db.Belegungen.AddRange(belegungen);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (kurs, personen, belegungen, raum, online);
    }

    private static Veranstaltung Erste(Kurs k) => k.Veranstaltungen.OrderBy(v => v.Beginn).First();

    private static void Um(TestDatenbank t, DateTime wanduhr) => t.Uhr.SetUtcNow(new DateTimeOffset(wanduhr, Mez));

    [Fact]
    public async Task Termin_Code_ist_zufaellig_und_nur_im_Zeitfenster_und_fuer_den_Raum_gueltig()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, _, _, raum, online) = await KursAsync(t, new DateOnly(2026, 11, 17), 4);
        var v = Erste(kurs);
        Um(t, v.Beginn.AddMinutes(-40));
        await using var db = t.NeuerKontext();
        var dienst = Anwesenheit(t, db);
        var code = await dienst.RaumTokenAsync(v.Id, raum.Id, ct);
        code.ShouldNotContain(v.Id.ToString("N")[..8]); // zufälliger Verweis, keine Daten im Code
        (await dienst.RaumTokenAsync(v.Id, raum.Id, ct)).ShouldBe(code);
        (await Should.ThrowAsync<InvalidOperationException>(() => dienst.RaumTokenAsync(v.Id, online.Id, ct))).Message.ShouldContain("Online-Teilnahme");

        var codes = new Termincodes(new ZugangstokenSpeicher(db), t.Uhr);
        (await codes.PruefenAsync(code, abbruch: ct)).Code.ShouldBe(Tokencode.NochNichtGueltig);
        Um(t, v.Beginn.AddMinutes(-10));
        (await codes.PruefenAsync(code, v.Id, raum.Id, ct)).Code.ShouldBe(Tokencode.Gueltig);
        (await codes.PruefenAsync(code, v.Id, online.Id, ct)).Code.ShouldBe(Tokencode.FalscherRaum);
        (await codes.PruefenAsync("erfundenerCode123", abbruch: ct)).Code.ShouldBe(Tokencode.Ungueltig);
        Um(t, v.Beginn.AddMinutes(31));
        (await codes.PruefenAsync(Termincodes.AusScan($"https://vhs.example/checkin/{code}"), abbruch: ct)).Code.ShouldBe(Tokencode.Abgelaufen);
    }

    [Fact]
    public async Task Persoenlicher_Link_plus_Termin_QR_checkt_mit_einem_Scan_ein_doppelt_und_gesperrt_werden_abgelehnt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _, raum, _) = await KursAsync(t, new DateOnly(2026, 11, 17), 4);
        var v = Erste(kurs);
        string link, code;
        await using (var db = t.NeuerKontext())
        {
            link = Geheimnis(Zugang(t, db).LinkErzeugen(p[0].Id));
            await db.SaveChangesAsync(ct);
            Um(t, v.Beginn.AddMinutes(-20));
            code = await Anwesenheit(t, db).RaumTokenAsync(v.Id, raum.Id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var a = await Anwesenheit(t, db).LinkCheckinAsync(link, $"https://vhs.example/checkin/{code}", ct);
            a.Ergebnis.Ok.ShouldBeTrue();
            (await Anwesenheit(t, db).LinkCheckinAsync(link, code, ct)).Ergebnis.Code.ShouldBe(Checkincode.BereitsEingecheckt);
        }

        await using (var db = t.NeuerKontext())
        {
            var anw = await db.Set<Anwesenheit>().SingleAsync(ct);
            (anw.Status, anw.Weg, anw.Minuten, anw.OrtId).ShouldBe((Anwesenheitsstatus.Anwesend, Erfassungsweg.RaumQr, 90, raum.Id));
            await Zugang(t, db).AlleWiderrufenAsync(p[0].Id, "Handy verloren", abbruch: ct);
            (await Anwesenheit(t, db).LinkCheckinAsync(link, code, ct)).Ergebnis.Text.ShouldContain("gesperrt");
        }
    }

    [Fact]
    public async Task Gemerktes_Geraet_Familie_waehlt_aus_ohne_Geraet_kommt_ein_Hinweis()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _, raum, _) = await KursAsync(t, new DateOnly(2026, 11, 17), 4, anzahl: 2);
        var v = Erste(kurs);
        string g1, g2, code;
        await using (var db = t.NeuerKontext())
        {
            var z = Zugang(t, db);
            var l1 = Geheimnis(z.LinkErzeugen(p[0].Id));
            var l2 = Geheimnis(z.LinkErzeugen(p[1].Id));
            await db.SaveChangesAsync(ct);
            g1 = (await z.GeraetVerbindenAsync(l1, ct))!.Value.Geheimnis;
            g2 = (await z.GeraetVerbindenAsync(l2, ct))!.Value.Geheimnis;
            Um(t, v.Beginn.AddMinutes(-5));
            code = await Anwesenheit(t, db).RaumTokenAsync(v.Id, raum.Id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var dienst = Anwesenheit(t, db);
            (await dienst.RaumCheckinAsync(code, [], abbruch: ct)).Status.ShouldBe(RaumCheckinStatus.KeinGeraet);
            var familie = await dienst.RaumCheckinAsync(code, [g1, g2], abbruch: ct);
            familie.Status.ShouldBe(RaumCheckinStatus.AuswahlNoetig);
            familie.Personen.Count.ShouldBe(2);
            (await dienst.RaumCheckinAsync(code, [g1, g2], p[1].Id, ct)).Status.ShouldBe(RaumCheckinStatus.Eingecheckt);
            (await dienst.RaumCheckinAsync(code, [g1], abbruch: ct)).Status.ShouldBe(RaumCheckinStatus.Eingecheckt);
        }

        await using (var db = t.NeuerKontext())
        {
            (await db.Set<Anwesenheit>().CountAsync(ct)).ShouldBe(2);
            await Zugang(t, db).AlleWiderrufenAsync(p[0].Id, "Test", abbruch: ct);
            (await Anwesenheit(t, db).RaumCheckinAsync(code, [g1], abbruch: ct)).Status.ShouldBe(RaumCheckinStatus.KeinGeraet);
        }
    }

    [Fact]
    public async Task Kontingent_Checkin_ohne_Einbuchung_wird_mit_Grund_abgelehnt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, b, raum, _) = await KursAsync(t, new DateOnly(2026, 11, 17), 10, Buchungsrichtlinie.Kontingentkurs(10, 6));
        var v = Erste(kurs);
        string link, code;
        await using (var db = t.NeuerKontext())
        {
            link = Geheimnis(Zugang(t, db).LinkErzeugen(p[0].Id));
            await db.SaveChangesAsync(ct);
            Um(t, v.Beginn.AddMinutes(-5));
            code = await Anwesenheit(t, db).RaumTokenAsync(v.Id, raum.Id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var a = await Anwesenheit(t, db).LinkCheckinAsync(link, code, ct);
            a.Ergebnis.Code.ShouldBe(Checkincode.KeineEinbuchung);
            a.Ergebnis.Text.ShouldContain("keine Einbuchung");
            (await db.Set<Anwesenheit>().CountAsync(ct)).ShouldBe(0);
        }

        await using (var db = t.NeuerKontext())
        {
            var bel = await db.Belegungen.SingleAsync(x => x.Id == b[0].Id, ct);
            bel.Einbuchen(v, raum.Id, 0, 14, kurs.Richtlinie, new DateTimeOffset(v.Beginn.AddDays(-3), Mez), "Test");
            await db.SaveChangesAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            (await Anwesenheit(t, db).LinkCheckinAsync(link, code, ct)).Ergebnis.Ok.ShouldBeTrue();
            (await db.Belegungen.SingleAsync(x => x.Id == b[0].Id, ct)).Kontingent.Besucht.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Abrechnungsrelevante_Kurse_Selbst_Checkin_mit_Bestaetigung_oder_abgeschaltet()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var r = Buchungsrichtlinie.Oeffentlich with { SelbstCheckin = SelbstCheckin.MitBestaetigung };
        var (kurs, p, b, raum, _) = await KursAsync(t, new DateOnly(2026, 11, 17), 4, r);
        var v = Erste(kurs);
        string link, code;
        await using (var db = t.NeuerKontext())
        {
            link = Geheimnis(Zugang(t, db).LinkErzeugen(p[0].Id));
            await db.SaveChangesAsync(ct);
            Um(t, v.Beginn.AddMinutes(-5));
            code = await Anwesenheit(t, db).RaumTokenAsync(v.Id, raum.Id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var a = await Anwesenheit(t, db).LinkCheckinAsync(link, code, ct);
            a.Ergebnis.Text.ShouldContain("Kursleitung bestätigt");
            (await db.Set<Anwesenheit>().SingleAsync(ct)).Status.ShouldBe(Anwesenheitsstatus.Unbestaetigt);
            await Anwesenheit(t, db).ErfassenAsync(v.Id, b[0].Id, Anwesenheitsstatus.Anwesend, null, null, ct); // Kursleitung bestätigt
        }

        await using (var db = t.NeuerKontext())
        {
            var anw = await db.Set<Anwesenheit>().SingleAsync(ct);
            (anw.Status, anw.Weg).ShouldBe((Anwesenheitsstatus.Anwesend, Erfassungsweg.RaumQr));
            anw.Bemerkung!.ShouldContain("bestätigt");
            var k = await db.Kurse.SingleAsync(x => x.Id == kurs.Id, ct);
            k.MitRichtlinie(k.Richtlinie with { SelbstCheckin = SelbstCheckin.Aus });
            await db.SaveChangesAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => Anwesenheit(t, db).RaumTokenAsync(v.Id, raum.Id, ct))).Message.ShouldContain("abgeschaltet");
            (await Anwesenheit(t, db).LinkCheckinAsync(link, code, ct)).Ergebnis.Text.ShouldContain("Kursleitung");
        }
    }

    [Fact]
    public async Task Ausweis_QR_enthaelt_nur_ein_Pseudonym_und_ist_widerrufbar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p, _, _, _) = await KursAsync(t, new DateOnly(2026, 11, 17), 4, anzahl: 2);
        var v = Erste(kurs);
        string a1, a2;
        await using (var db = t.NeuerKontext())
        {
            var z = Zugang(t, db);
            a1 = z.AusweisErzeugen(p[0].Id);
            a2 = z.AusweisErzeugen(p[1].Id);
            await db.SaveChangesAsync(ct);
        }

        a1.ShouldStartWith(CheckinZugang.AusweisPraefix);
        a1.ShouldNotContain(p[0].Id.ToString("N"));
        a1.ShouldNotContain("Vorname1");
        Um(t, v.Beginn.AddMinutes(5));
        await using (var db = t.NeuerKontext())
        {
            var dienst = Anwesenheit(t, db);
            (await dienst.AusweisCheckinAsync(v.Id, a1, ct)).Ergebnis.Ok.ShouldBeTrue();
            (await dienst.AusweisCheckinAsync(v.Id, a1, ct)).Ergebnis.Code.ShouldBe(Checkincode.BereitsEingecheckt);
            var token = (await Zugang(t, db).DerPersonAsync(p[1].Id, ct)).Single();
            await Zugang(t, db).WiderrufenAsync(token.Id, "Ausweis verloren", ct);
            (await dienst.AusweisCheckinAsync(v.Id, a2, ct)).Ergebnis.Text.ShouldContain("Kein gültiger Teilnehmerausweis");
            (await db.Set<Zugangstoken>().AllAsync(z => z.Hash.Length == 64, ct)).ShouldBeTrue(); // nur Hashes gespeichert
        }
    }

    [Fact]
    public async Task Abschluss_wendet_die_Nichterscheinen_Regel_an()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, _, b, raum, _) = await KursAsync(t, new DateOnly(2026, 11, 17), 10, Buchungsrichtlinie.Kontingentkurs(10, 6));
        var v = Erste(kurs);
        await using (var db = t.NeuerKontext())
        {
            var bel = await db.Belegungen.SingleAsync(x => x.Id == b[0].Id, ct);
            bel.Einbuchen(v, raum.Id, 0, 14, kurs.Richtlinie, t.Uhr.GetLocalNow(), "Test");
            await db.SaveChangesAsync(ct);
        }

        Um(t, v.Ende.AddHours(2));
        await using (var db = t.NeuerKontext())
        {
            (await Anwesenheit(t, db).AbschliessenAsync(v.Id, ct)).ShouldBe(1);
        }

        await using (var db = t.NeuerKontext())
        {
            (await db.Belegungen.SingleAsync(x => x.Id == b[0].Id, ct)).Kontingent.Verfallen.ShouldBe(1);
            (await db.Set<Anwesenheit>().SingleAsync(ct)).Status.ShouldBe(Anwesenheitsstatus.Abwesend);
            await Should.ThrowAsync<InvalidOperationException>(() => Anwesenheit(t, db).AbschliessenAsync(v.Id, ct));
        }
    }

    [Fact]
    public async Task Bescheinigung_traegt_den_Namen_zum_Kurszeitpunkt_die_aktuelle_Adresse_und_ist_per_Hash_gesichert()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var erfasst = new DateTimeOffset(2025, 1, 10, 9, 0, 0, Mez);
        var teilnehmerin = Person.Anlegen("Julia", "Schmitz", new DateOnly(2020, 1, 1), erfasst.AddYears(-5), new DateOnly(1990, 4, 2));
        var adr = teilnehmerin.AdresseHinzufuegen(null, "Alte Straße 1", "48282", "Emsdetten", new DateOnly(2020, 1, 1), Erfassung.Erst(erfasst.AddYears(-5)));
        teilnehmerin.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, adr, new DateOnly(2020, 1, 1), Erfassung.Erst(erfasst.AddYears(-5)));
        var leitung = Person.Anlegen("Anna", "Schmidt", new DateOnly(2019, 3, 1), erfasst.AddYears(-6));

        var (kurs, _, b, raum, _) = await KursAsync(t, new DateOnly(2025, 3, 11), 4, person: teilnehmerin, leitung: leitung,
            gebucht: new DateTimeOffset(2025, 3, 1, 10, 0, 0, Mez));

        await using (var db = t.NeuerKontext())
        {
            var bel = await db.Belegungen.SingleAsync(x => x.Id == b[0].Id, ct);
            var k = await db.Kurse.Include(x => x.Veranstaltungen).SingleAsync(x => x.Id == kurs.Id, ct);
            foreach (var v in k.Veranstaltungen)
            {
                db.Add(new Anwesenheit(bel, v, raum.Id, Anwesenheitsstatus.Anwesend, 90, Erfassungsweg.Liste, "Anna Schmidt", new DateTimeOffset(v.Beginn, Mez)));
            }

            var p = await db.Personen.SingleAsync(x => x.Id == teilnehmerin.Id, ct);
            p.NameAendernAb("Julia", "Wagner", new DateOnly(2025, 8, 1), "Heirat", new DateTimeOffset(2025, 8, 5, 10, 0, 0, TimeSpan.FromHours(2)));
            p.UmzugAb(adr, null, "Neuer Weg 5", "48268", "Greven", new DateOnly(2026, 2, 1), Erfassung.Aenderung(new DateTimeOffset(2026, 2, 3, 9, 0, 0, Mez), "Umzug"));
            var l = await db.Personen.SingleAsync(x => x.Id == leitung.Id, ct);
            l.NameAendernAb("Anna", "Becker", new DateOnly(2025, 6, 14), "Heirat", new DateTimeOffset(2025, 7, 2, 14, 0, 0, TimeSpan.FromHours(2)));
            await db.SaveChangesAsync(ct);
        }

        Dokument dok;
        await using (var db = t.NeuerKontext())
        {
            dok = await Dokumente(t, db).BescheinigungErstellenAsync(b[0].Id, Dokumentart.Teilnahmebescheinigung, ct);
        }

        var daten = JsonSerializer.Deserialize<Bescheinigungsdaten>(dok.DatenJson)!;
        daten.Name.ShouldBe("Julia Schmitz");
        daten.Kursleitung.ShouldBe("Anna Schmidt");
        daten.Anschrift.ShouldContain("Neuer Weg 5");
        daten.TermineAnwesend.ShouldBe(4);
        daten.UeAnwesend.ShouldBe(8m);
        dok.Sha256.Length.ShouldBe(64);
        System.Text.Encoding.ASCII.GetString(dok.Pdf, 0, 4).ShouldBe("%PDF");

        await using (var db = t.NeuerKontext())
        {
            var dienst = Dokumente(t, db);
            await Should.ThrowAsync<InvalidOperationException>(() => dienst.BescheinigungErstellenAsync(b[0].Id, Dokumentart.Teilnahmebescheinigung, ct));
            (await dienst.NachdruckAsync(dok.Id, ct)).Pdf.ShouldBe(dok.Pdf);
            (await dienst.UebersetzungAsync(dok.UebersetzungsToken, ct)).Dokument.ShouldNotBeNull();
        }

        await using (var db = t.NeuerKontext())
        {
            var verfaelscht = (byte[])dok.Pdf.Clone();
            verfaelscht[^20] ^= 0xFF;
            await db.Database.ExecuteSqlAsync($"UPDATE Dokumente SET Pdf = {verfaelscht} WHERE Id = {VibeVHS.Infrastructure.Datenbank.Speicherformate.IdBytes(dok.Id)}", ct);
        }

        await using (var db = t.NeuerKontext())
        {
            (await Should.ThrowAsync<InvalidOperationException>(() => Dokumente(t, db).NachdruckAsync(dok.Id, ct))).Message.ShouldContain("Prüfsumme");
            (await db.Protokoll.AnyAsync(e => e.Aktion == "Dokument.HashFehler", ct)).ShouldBeTrue();
        }
    }
}
