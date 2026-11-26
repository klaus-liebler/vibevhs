using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Anmeldung;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

public class AnmeldungTests
{
    private static readonly Benutzer Tresen = new("katja.friedsen", "Katja Friedsen", Rolle.Teilnehmerservice);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private static AnmeldungDienst Dienst(TestDatenbank t, VibeVhsDbContext db, Benutzer? wer = null)
    {
        var benutzer = new TestBenutzer(wer ?? Tresen);
        var protokoll = new AuditProtokoll(db, benutzer, t.Uhr);
        var post = new PostausgangDienst(new NachrichtenSpeicher(db), protokoll, benutzer, new Uebersetzungslinks(new Uri("https://vhs.example/")), t.Uhr);
        var speicher = new AnmeldungSpeicher(db);
        var zugang = new VibeVHS.Application.Durchfuehrung.CheckinZugang(new VibeVHS.Infrastructure.Durchfuehrung.ZugangstokenSpeicher(db),
            new VibeVHS.Application.Durchfuehrung.OeffentlicheAdresse(new Uri("https://vhs.example/")), protokoll, benutzer, t.Uhr);
        return new AnmeldungDienst(speicher, new ProgrammSpeicher(db), new PersonenSpeicher(db), speicher, new StammdatenSpeicher(db), post, protokoll, benutzer, t.Uhr,
            zugang, new VibeVHS.Infrastructure.Durchfuehrung.QrCodeErzeuger());
    }

    private static async Task<(Kurs Kurs, List<Person> Personen)> KursMitPersonenAsync(TestDatenbank t, int max, int personen, Buchungsrichtlinie? richtlinie = null)
    {
        var jetzt = t.Uhr.GetLocalNow();
        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 6, 30));
        var art = new Kursart("Bewegung", Nutzungsart.Bewegung, [], richtlinie ?? Buchungsrichtlinie.Oeffentlich);
        var raum = new Ort(Ortsart.Raum, "Bewegungsraum", null, "B 1.01").MitKapazitaet(Nutzungsart.Bewegung, 16);
        var kurs = new Kurs("26H-801", "Yoga am Abend", semester, 8, art, 2, max);
        kurs.EntgeltSetzenAb(Geld.Euro(84), Geld.Euro(63), semester.Beginn, Erfassung.Erst(jetzt.AddMonths(-3)));
        kurs.SerieFestlegen(new Serienregel
        {
            Start = new DateOnly(2026, 12, 3), Anzahl = richtlinie?.Belegung == Belegungsart.Kontingent ? 20 : 6, Wochentage = [DayOfWeek.Thursday],
            Beginn = new TimeOnly(19, 0), DauerMinuten = 90, OrtIds = [raum.Id],
        }, Kalender.OhneFerien, new DateOnly(2027, 6, 30));
        kurs.StatusSetzen(Kursstatus.Veroeffentlicht);

        var liste = Enumerable.Range(1, personen).Select(i =>
        {
            var p = Person.Anlegen($"Vorname{i}", $"Nachname{i}", new DateOnly(2025, 1, 1), jetzt.AddYears(-1), new DateOnly(1980, 1, i), $"p{i}@example.org", i == 1 ? "tr" : "de");
            p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2025, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
            return p;
        }).ToList();

        await using var db = t.NeuerKontext();
        db.Semester.Add(semester);
        db.Kursarten.Add(art);
        db.Orte.Add(raum);
        db.Kurse.Add(kurs);
        db.Personen.AddRange(liste);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (kurs, liste);
    }

    [Fact]
    public async Task Warteliste_Nachruecken_mit_Frist_und_Verfall_an_die_naechste_Person()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursMitPersonenAsync(t, max: 2, personen: 4);

        Belegung a, c, d;
        await using (var db = t.NeuerKontext())
        {
            var dienst = Dienst(t, db);
            a = (await dienst.AnmeldenAsync(new AnmeldeAuftrag(p[0].Id, kurs.Id, Zahlart.Lastschrift), ct)).Belegung;
            await dienst.AnmeldenAsync(new AnmeldeAuftrag(p[1].Id, kurs.Id, Zahlart.Bar), ct);
            c = (await dienst.AnmeldenAsync(new AnmeldeAuftrag(p[2].Id, kurs.Id, Zahlart.Bar), ct)).Belegung;
            t.Uhr.Advance(TimeSpan.FromMinutes(5));
            d = (await dienst.AnmeldenAsync(new AnmeldeAuftrag(p[3].Id, kurs.Id, Zahlart.Bar), ct)).Belegung;
        }

        a.Status.ShouldBe(Belegungsstatus.Angemeldet);
        c.Status.ShouldBe(Belegungsstatus.Warteliste);
        d.Status.ShouldBe(Belegungsstatus.Warteliste);

        await using (var db = t.NeuerKontext())
        {
            var regel = await Dienst(t, db).StornierenAsync(a.Id, "krank", ct);
            regel.GebuehrCent.ShouldBe(0); // mehr als 7 Tage vor Beginn
        }

        await using (var db = t.NeuerKontext())
        {
            var cNeu = await db.Belegungen.SingleAsync(b => b.Id == c.Id, ct);
            cNeu.Status.ShouldBe(Belegungsstatus.Nachrueckangebot);
            cNeu.AngebotBis.ShouldNotBeNull();
            (await db.Belegungen.SingleAsync(b => b.Id == d.Id, ct)).Status.ShouldBe(Belegungsstatus.Warteliste);
            var mail = await db.Postausgang.SingleAsync(n => n.PersonId == p[2].Id && n.Betreff.StartsWith("Ein Platz ist frei"), ct);
            mail.Inhalt.ShouldContain("/uebersetzen/");
            (await db.Protokoll.Where(e => e.Aktion == "Anmeldung.Nachrueckangebot").CountAsync(ct)).ShouldBe(1);
            (await db.Protokoll.AnyAsync(e => e.Beschreibung.Contains("Vorname"), ct)).ShouldBeFalse(); // nur Pseudonyme
        }

        // Frist verstreicht: Angebot verfällt, die nächste Person bekommt es.
        t.Uhr.Advance(TimeSpan.FromDays(5));
        await using (var db = t.NeuerKontext())
        {
            (await Dienst(t, db).AbgelaufeneAngeboteAsync(ct)).ShouldBe(1);
        }

        await using (var db = t.NeuerKontext())
        {
            (await db.Belegungen.SingleAsync(b => b.Id == c.Id, ct)).Status.ShouldBe(Belegungsstatus.AngebotVerfallen);
            (await db.Belegungen.SingleAsync(b => b.Id == d.Id, ct)).Status.ShouldBe(Belegungsstatus.Nachrueckangebot);
            await Dienst(t, db).AngebotAnnehmenAsync(d.Id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            (await db.Belegungen.SingleAsync(b => b.Id == d.Id, ct)).Status.ShouldBe(Belegungsstatus.Angemeldet);
        }
    }

    [Fact]
    public async Task Anmeldung_legt_Bestaetigung_mit_Kalenderdatei_und_Uebersetzungslink_in_den_Postausgang()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursMitPersonenAsync(t, max: 10, personen: 1);
        await using var db = t.NeuerKontext();
        var e = await Dienst(t, db).AnmeldenAsync(new AnmeldeAuftrag(p[0].Id, kurs.Id, Zahlart.Lastschrift), ct);

        var mail = await db.Postausgang.SingleAsync(n => n.Id == e.NachrichtId, ct);
        mail.Sprache.ShouldBe("tr");
        mail.Uebersetzungslink!.ShouldContain("sprache=tr");
        mail.Inhalt.ShouldContain("Bu mesajı Türkçe okuyun");
        mail.AnhangName.ShouldBe("26H-801-termine.ics");
        mail.AnhangInhalt!.ShouldContain("BEGIN:VCALENDAR");
        mail.AnhangInhalt!.Split("BEGIN:VEVENT").Length.ShouldBe(7);
        mail.Inhalt.ShouldContain("84,00 €");
        mail.Inhalt.ShouldContain("Guten Tag Vorname1 Nachname1,"); // ohne gepflegte Anrede: voller Name
    }

    [Fact]
    public async Task Entgelt_bleibt_nach_Preisaenderung_im_Kurs_eingefroren()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursMitPersonenAsync(t, max: 10, personen: 2);
        Guid erste;
        await using (var db = t.NeuerKontext())
        {
            erste = (await Dienst(t, db).AnmeldenAsync(new AnmeldeAuftrag(p[0].Id, kurs.Id, Zahlart.Bar), ct)).Belegung.Id;
            var k = await db.Kurse.SingleAsync(x => x.Id == kurs.Id, ct);
            k.EntgeltSetzenAb(Geld.Euro(99), null, new DateOnly(2026, 8, 24), Erfassung.Aenderung(t.Uhr.GetLocalNow(), "Preiserhöhung"));
            await db.SaveChangesAsync(ct);
        }

        t.Uhr.Advance(TimeSpan.FromHours(1));
        await using (var db = t.NeuerKontext())
        {
            var zweite = (await Dienst(t, db).AnmeldenAsync(new AnmeldeAuftrag(p[1].Id, kurs.Id, Zahlart.Bar), ct)).Belegung;
            zweite.Entgelt.ShouldBe(Geld.Euro(99));
            (await db.Belegungen.SingleAsync(b => b.Id == erste, ct)).Entgelt.ShouldBe(Geld.Euro(84));
        }
    }

    [Fact]
    public async Task Online_selbst_nur_oeffentliche_Kurse_und_Buchung_ohne_Rolle_wird_abgelehnt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursMitPersonenAsync(t, max: 10, personen: 2);
        await using (var db = t.NeuerKontext())
        {
            var k = await db.Kurse.SingleAsync(x => x.Id == kurs.Id, ct);
            k.MitSichtbarkeit(Sichtbarkeit.Intern);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var ex = await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db).AnmeldenAsync(new AnmeldeAuftrag(p[0].Id, kurs.Id, Zahlart.Bar, Weg: Buchungsweg.Online), ct));
            ex.Message.ShouldContain("öffentliche Kurse");
        }

        await using (var db = t.NeuerKontext())
        {
            var fbl = new Benutzer("varvara.schatte", "Varvara Schatte", Rolle.Fachbereichsleitung);
            var ex = await Should.ThrowAsync<InvalidOperationException>(() => Dienst(t, db, fbl).AnmeldenAsync(new AnmeldeAuftrag(p[1].Id, kurs.Id, Zahlart.Bar), ct));
            ex.Message.ShouldContain("Teilnehmerservice");
        }
    }

    [Fact]
    public async Task Vorab_Einbuchung_haelt_die_Kapazitaet_und_Austragen_laesst_die_Warteliste_nachruecken()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var r = Buchungsrichtlinie.Kontingentkurs(10, 6);
        var (kurs, p) = await KursMitPersonenAsync(t, max: 2, personen: 3, r);
        var belegungen = new List<Guid>();
        await using (var db = t.NeuerKontext())
        {
            var dienst = Dienst(t, db);
            foreach (var person in p)
            {
                belegungen.Add((await dienst.AnmeldenAsync(new AnmeldeAuftrag(person.Id, kurs.Id, Zahlart.Bar), ct)).Belegung.Id);
            }
        }

        var v = kurs.Veranstaltungen.OrderBy(x => x.Beginn).First();
        var ort = v.Orte.Single().OrtId;
        await using (var db = t.NeuerKontext())
        {
            var dienst = Dienst(t, db);
            (await dienst.EinbuchenAsync(belegungen[0], v.Id, ort, ct)).Code.ShouldBe(Einbuchungscode.Eingebucht);
            (await dienst.EinbuchenAsync(belegungen[1], v.Id, ort, ct)).Code.ShouldBe(Einbuchungscode.Eingebucht);
            (await dienst.EinbuchenAsync(belegungen[2], v.Id, ort, ct)).Code.ShouldBe(Einbuchungscode.Warteliste); // Kapazität 2 erreicht
        }

        await using (var db = t.NeuerKontext())
        {
            await Dienst(t, db).AustragenAsync(belegungen[0], v.Id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var dritte = await db.Belegungen.SingleAsync(b => b.Id == belegungen[2], ct);
            dritte.Einbuchungen.Keys.ShouldContain(v.Id);
            dritte.Kontingent.Verbleibend.ShouldBe(9);
            (await db.Belegungen.SingleAsync(b => b.Id == belegungen[0], ct)).Kontingent.Verbleibend.ShouldBe(10);
            (await db.Postausgang.AnyAsync(n => n.PersonId == p[2].Id && n.Betreff.StartsWith("Platz frei"), ct)).ShouldBeTrue();
            (await db.KontingentEreignisse.CountAsync(ct)).ShouldBe(3 + 2 + 1 + 1 + 1); // Erworben ×3, Eingebucht ×2, Warteliste, Ausgetragen, Nachgerückt
        }
    }

    [Fact]
    public async Task Umbuchung_mit_Vorschlag_friert_das_neue_Entgelt_ein()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursMitPersonenAsync(t, max: 10, personen: 1);
        Kurs zweiter;
        await using (var db = t.NeuerKontext())
        {
            var art = await db.Kursarten.OrderBy(k => k.Name).ThenBy(k => k.Id).FirstAsync(ct);
            var semester = await db.Semester.OrderBy(s => s.Kuerzel).ThenBy(s => s.Id).FirstAsync(ct);
            zweiter = new Kurs("26H-802", "Yoga am Morgen", semester, 8, art, 2, 10);
            zweiter.EntgeltSetzenAb(Geld.Euro(70), null, semester.Beginn, Erfassung.Erst(t.Uhr.GetLocalNow().AddMonths(-1)));
            zweiter.SerieFestlegen(new Serienregel
            {
                Start = new DateOnly(2026, 12, 2), Anzahl = 5, Wochentage = [DayOfWeek.Wednesday], Beginn = new TimeOnly(9, 0), DauerMinuten = 60,
                OrtIds = [kurs.Serie!.OrtIds[0]],
            }, Kalender.OhneFerien, new DateOnly(2027, 6, 30));
            zweiter.StatusSetzen(Kursstatus.Veroeffentlicht);
            db.Kurse.Add(zweiter);
            await db.SaveChangesAsync(ct);
        }

        Guid alt;
        await using (var db = t.NeuerKontext())
        {
            alt = (await Dienst(t, db).AnmeldenAsync(new AnmeldeAuftrag(p[0].Id, kurs.Id, Zahlart.Lastschrift), ct)).Belegung.Id;
        }

        await using (var db = t.NeuerKontext())
        {
            var dienst = Dienst(t, db);
            var vorschlaege = await dienst.UmbuchungsvorschlaegeAsync(alt, abbruch: ct);
            vorschlaege.Select(v => v.Kurs.Kursnummer).ShouldContain("26H-802");
            var neu = await dienst.UmbuchenAsync(alt, zweiter.Id, ct);
            neu.Entgelt.ShouldBe(Geld.Euro(70));
            neu.UmgebuchtVonId.ShouldBe(alt);
        }

        await using (var db2 = t.NeuerKontext())
        {
            var b = await db2.Belegungen.SingleAsync(x => x.Id == alt, ct);
            b.Status.ShouldBe(Belegungsstatus.Umgebucht);
            b.UmgebuchtNachId.ShouldNotBeNull();
        }
    }
}
