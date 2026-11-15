using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Anmeldung;
using VibeVHS.Infrastructure.Aufgaben;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Datenschutz;
using VibeVHS.Infrastructure.Personen;
using VibeVHS.Infrastructure.Programm;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Aufgabenmodul (I5): Regeln, Gruppenkorb, Übernehmen/Zuweisen, Eskalation, automatisches Schließen, Routinen.</summary>
public class AufgabenTests
{
    private static readonly Benutzer Tresen = new("katja.friesen", "Katja Friesen", Rolle.Teilnehmerservice);
    private static readonly Benutzer FblSprachen = new("varvara.schatz", "Varvara Schatz", Rolle.Fachbereichsleitung, [Rolle.Fachbereichsleitung], [8]);
    private static readonly Benutzer FblSprachen2 = new("tom.berg", "Tom Berg", Rolle.Fachbereichsleitung, [Rolle.Fachbereichsleitung], [8]);
    private static readonly Benutzer FblGesundheit = new("ina.wolf", "Ina Wolf", Rolle.Fachbereichsleitung, [Rolle.Fachbereichsleitung], [3]);
    private static readonly Benutzer Leitung = new("eva.lang", "Eva Lang", Rolle.Leitung);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private sealed record Dienste(AufgabenDienst Aufgaben, AnmeldungDienst Anmeldung, Routinen Routinen);

    private static Dienste Erzeugen(TestDatenbank t, VibeVhsDbContext db, Benutzer? wer = null)
    {
        var benutzer = new TestBenutzer(wer ?? Benutzer.Automatik);
        var protokoll = new AuditProtokoll(db, benutzer, t.Uhr);
        var post = new PostausgangDienst(new NachrichtenSpeicher(db), protokoll, benutzer, new Uebersetzungslinks(new Uri("https://vhs.example/")), t.Uhr);
        var speicher = new AnmeldungSpeicher(db);
        var zugang = new VibeVHS.Application.Durchfuehrung.CheckinZugang(new VibeVHS.Infrastructure.Durchfuehrung.ZugangstokenSpeicher(db),
            new VibeVHS.Application.Durchfuehrung.OeffentlicheAdresse(new Uri("https://vhs.example/")), protokoll, benutzer, t.Uhr);
        var anmeldung = new AnmeldungDienst(speicher, new ProgrammSpeicher(db), new PersonenSpeicher(db), speicher, new StammdatenSpeicher(db), post, protokoll,
            benutzer, t.Uhr, zugang, new VibeVHS.Infrastructure.Durchfuehrung.QrCodeErzeuger());
        var aufgaben = new AufgabenDienst(new AufgabenSpeicher(db), new StammdatenSpeicher(db), protokoll, benutzer, t.Uhr);
        var routinen = new Routinen(aufgaben, anmeldung, speicher, new ProgrammSpeicher(db), new NachrichtenSpeicher(db), new DatenschutzSpeicher(db), protokoll, t.Uhr);
        return new(aufgaben, anmeldung, routinen);
    }

    /// <summary>Kurs im Fachbereich 8, mindestens 3, höchstens 10 Plätze, Beginn Di 24.11.2026 (9 Tage nach „heute“).</summary>
    private static async Task<(Kurs Kurs, List<Person> Personen)> KursAsync(TestDatenbank t, int personen, DateOnly? start = null)
    {
        var jetzt = t.Uhr.GetLocalNow();
        var semester = new Semester("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 6, 30));
        var art = new Kursart("Sprachen", Nutzungsart.Seminar, [], Buchungsrichtlinie.Oeffentlich);
        var raum = new Ort(Ortsart.Raum, "Seminarraum", null, "A 1.12").MitKapazitaet(Nutzungsart.Seminar, 16);
        var kurs = new Kurs("26H-811", "Spanisch A1", semester, 8, art, 3, 10);
        kurs.EntgeltSetzenAb(Geld.Euro(90), null, semester.Beginn, Erfassung.Erst(jetzt.AddMonths(-3)));
        kurs.SerieFestlegen(new Serienregel
        {
            Start = start ?? new DateOnly(2026, 11, 24), Anzahl = 8, Wochentage = [(start ?? new DateOnly(2026, 11, 24)).DayOfWeek],
            Beginn = new TimeOnly(18, 0), DauerMinuten = 90, OrtIds = [raum.Id],
        }, Kalender.OhneFerien, _ => "A 1.12 Seminarraum", new DateOnly(2027, 6, 30));
        kurs.StatusSetzen(Kursstatus.Veroeffentlicht);
        var liste = Enumerable.Range(1, personen).Select(i =>
        {
            var p = Person.Anlegen($"Vorname{i}", $"Nachname{i}", new DateOnly(2025, 1, 1), jetzt.AddYears(-1), new DateOnly(1980, 1, i), $"p{i}@example.org");
            p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2025, 1, 1), Erfassung.Erst(jetzt.AddYears(-1)));
            return p;
        }).ToList();

        await using var db = t.NeuerKontext();
        db.Semester.Add(semester);
        db.Kursarten.Add(art);
        db.Orte.Add(raum);
        db.Kurse.Add(kurs);
        db.Personen.AddRange(liste);
        db.Benutzerkonten.AddRange(
            new Benutzerkonto(FblSprachen.Kennung, FblSprachen.Name, null, [Rolle.Fachbereichsleitung], [8], null),
            new Benutzerkonto(FblSprachen2.Kennung, FblSprachen2.Name, null, [Rolle.Fachbereichsleitung], [8], null),
            new Benutzerkonto(FblGesundheit.Kennung, FblGesundheit.Name, null, [Rolle.Fachbereichsleitung], [3], null),
            new Benutzerkonto(Leitung.Kennung, Leitung.Name, null, [Rolle.Leitung], [], null));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (kurs, liste);
    }

    private static async Task<Belegung> AnmeldenAsync(TestDatenbank t, Kurs kurs, Person p)
    {
        await using var db = t.NeuerKontext();
        return (await Erzeugen(t, db, Tresen).Anmeldung.AnmeldenAsync(new AnmeldeAuftrag(p.Id, kurs.Id, Zahlart.Ueberweisung), TestContext.Current.CancellationToken)).Belegung;
    }

    private static async Task<Abgleichergebnis> MindestzahlPruefenAsync(TestDatenbank t)
    {
        await using var db = t.NeuerKontext();
        return (await Erzeugen(t, db).Routinen.MindestteilnehmerPruefenAsync(TestContext.Current.CancellationToken)).Ergebnis;
    }

    [Fact]
    public async Task Mindestzahl_legt_Aufgabe_im_Fachbereichskorb_an_und_schliesst_sie_selbst()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursAsync(t, 3);
        await AnmeldenAsync(t, kurs, p[0]);

        (await MindestzahlPruefenAsync(t)).ShouldBe(new Abgleichergebnis(1, 0, 0));
        (await MindestzahlPruefenAsync(t)).ShouldBe(Abgleichergebnis.Leer); // nicht doppelt

        await using (var db = t.NeuerKontext())
        {
            var a = await db.Aufgaben.SingleAsync(ct);
            a.Typ.ShouldBe(Aufgabentypen.Mindestteilnehmer);
            a.Gruppe.ShouldBe(nameof(Rolle.Fachbereichsleitung));
            a.Fachbereich.ShouldBe(8);
            a.Titel.ShouldContain("1 von mindestens 3");
            a.Faellig.ShouldBe(new DateOnly(2026, 11, 21));
            a.Ziel.ShouldBe($"kurse/{kurs.Id}");
        }

        // Mindestzahl erreicht → automatisch geschlossen, mit Begründung im Protokoll.
        await AnmeldenAsync(t, kurs, p[1]);
        var dritte = await AnmeldenAsync(t, kurs, p[2]);
        (await MindestzahlPruefenAsync(t)).ShouldBe(new Abgleichergebnis(0, 1, 0));
        await using (var db = t.NeuerKontext())
        {
            var a = await db.Aufgaben.SingleAsync(ct);
            a.Status.ShouldBe(Aufgabenstatus.Geschlossen);
            a.Vermerk.ShouldBe("Mindestzahl erreicht (3 von mindestens 3)");
            (await db.Protokoll.AnyAsync(e => e.Aktion == "Aufgabe.AutomatischGeschlossen" && e.Beschreibung.Contains("Mindestzahl erreicht"), ct)).ShouldBeTrue();
        }

        // Wieder unter der Mindestzahl → dieselbe Aufgabe wird wieder geöffnet.
        await using (var db = t.NeuerKontext())
        {
            await Erzeugen(t, db, Tresen).Anmeldung.StornierenAsync(dritte.Id, "verhindert", ct);
        }

        (await MindestzahlPruefenAsync(t)).ShouldBe(new Abgleichergebnis(0, 0, 1));
        await using (var db = t.NeuerKontext())
        {
            (await db.Aufgaben.SingleAsync(ct)).Status.ShouldBe(Aufgabenstatus.Offen);
            (await db.Protokoll.AnyAsync(e => e.Beschreibung.Contains("Vorname") || e.Beschreibung.Contains("Nachname"), ct)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Gruppenkorb_Uebernehmen_Zuweisen_und_Erledigen_mit_Rechten()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursAsync(t, 1);
        await AnmeldenAsync(t, kurs, p[0]);
        await MindestzahlPruefenAsync(t);

        Guid id;
        await using (var db = t.NeuerKontext())
        {
            var u = await Erzeugen(t, db, FblSprachen).Aufgaben.UebersichtAsync(ct);
            u.Gruppenkorb.Count.ShouldBe(1);
            u.Meine.ShouldBeEmpty();
            u.Korbname.ShouldBe("Fachbereichsleitung FB 8");
            id = u.Gruppenkorb[0].Id;
            (await Erzeugen(t, db, FblGesundheit).Aufgaben.UebersichtAsync(ct)).Gruppenkorb.ShouldBeEmpty(); // anderer Fachbereich
            var a = u.Gruppenkorb[0];
            var nein = AufgabenDienst.KannUebernehmen(Tresen, a);
            nein.Erlaubt.ShouldBeFalse();
            nein.Begruendung!.ShouldContain("Fachbereichsleitung FB 8");
            AufgabenDienst.KannErledigen(FblSprachen, a).Begruendung!.ShouldContain("zuerst übernehmen");
        }

        await using (var db = t.NeuerKontext())
        {
            await Should.ThrowAsync<InvalidOperationException>(() => Erzeugen(t, db, Tresen).Aufgaben.UebernehmenAsync(id, ct));
            await Erzeugen(t, db, FblSprachen).Aufgaben.UebernehmenAsync(id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db, FblSprachen).Aufgaben.UebersichtAsync(ct)).Meine.Single().Id.ShouldBe(id);
            var kollegin = await Erzeugen(t, db, FblSprachen2).Aufgaben.UebersichtAsync(ct);
            kollegin.Gruppenkorb.ShouldBeEmpty();
            kollegin.BeiKollegen.Single().BearbeiterName.ShouldBe("Varvara Schatz");
            await Should.ThrowAsync<InvalidOperationException>(() => Erzeugen(t, db, FblSprachen2).Aufgaben.ErledigenAsync(id, null, ct));

            var kandidaten = await Erzeugen(t, db, FblSprachen).Aufgaben.KandidatenAsync(id, ct);
            kandidaten.Select(k => k.Login).ShouldBe(["tom.berg", "varvara.schatz"], ignoreOrder: true); // nicht FB 3
            await Should.ThrowAsync<InvalidOperationException>(() => Erzeugen(t, db, FblSprachen).Aufgaben.ZuweisenAsync(id, Leitung.Kennung, ct));
            await Erzeugen(t, db, FblSprachen).Aufgaben.ZuweisenAsync(id, "tom.berg", ct);
        }

        await using (var db = t.NeuerKontext())
        {
            await Erzeugen(t, db, FblSprachen2).Aufgaben.ErledigenAsync(id, "Werbung im Newsletter", ct);
            var a = await db.Aufgaben.SingleAsync(ct);
            a.Status.ShouldBe(Aufgabenstatus.Erledigt);
            a.ErledigtVon.ShouldBe("Tom Berg");
            var aktionen = await db.Protokoll.Where(e => e.ObjektId == id).Select(e => e.Aktion).ToListAsync(ct);
            aktionen.ShouldBe(["Aufgabe.Angelegt", "Aufgabe.Uebernommen", "Aufgabe.Zugewiesen", "Aufgabe.Erledigt"], ignoreOrder: true);
        }

        // Von Menschen erledigt: Die Regel legt sie nicht erneut an.
        (await MindestzahlPruefenAsync(t)).ShouldBe(Abgleichergebnis.Leer);
    }

    [Fact]
    public async Task Ueberfaellige_Aufgaben_werden_an_die_Leitung_eskaliert()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursAsync(t, 1);
        await AnmeldenAsync(t, kurs, p[0]);
        await MindestzahlPruefenAsync(t);

        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db).Aufgaben.EskalierenAsync(ct)).ShouldBe(0); // fällig 21.11., heute 15.11.
        }

        t.Uhr.Advance(TimeSpan.FromDays(8)); // 23.11.: fällig seit 21.11., Eskalation nach 1 Tag
        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db).Aufgaben.EskalierenAsync(ct)).ShouldBe(1);
            (await Erzeugen(t, db).Aufgaben.EskalierenAsync(ct)).ShouldBe(0); // nur einmal
        }

        await using (var db = t.NeuerKontext())
        {
            var u = await Erzeugen(t, db, Leitung).Aufgaben.UebersichtAsync(ct);
            u.Eskaliert.Single().Dringlichkeit.ShouldBe(Dringlichkeit.Kritisch);
            u.UeberfaelligJeGruppe.Single().ShouldBe(new UeberfaelligeGruppe(nameof(Rolle.Fachbereichsleitung), 8, 1, 1, 1));
            AufgabenDienst.KannUebernehmen(Leitung, u.Eskaliert[0]).Erlaubt.ShouldBeTrue();
            (await db.Protokoll.CountAsync(e => e.Aktion == "Aufgabe.Eskaliert" && !e.Erfolgreich, ct)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Absage_aus_der_Aufgabe_benachrichtigt_Teilnehmende_und_erledigt_die_Aufgabe()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursAsync(t, 2);
        await AnmeldenAsync(t, kurs, p[0]);
        await AnmeldenAsync(t, kurs, p[1]);
        await MindestzahlPruefenAsync(t);

        Guid id;
        await using (var db = t.NeuerKontext())
        {
            id = (await db.Aufgaben.SingleAsync(ct)).Id;
            // Ohne Übernahme keine Absage – und es passiert nichts am Kurs.
            await Should.ThrowAsync<InvalidOperationException>(() => Erzeugen(t, db, FblSprachen).Routinen.MindestzahlEntscheidenAsync(id, true, null, ct));
            (await db.Kurse.AsNoTracking().SingleAsync(ct)).Status.ShouldBe(Kursstatus.Veroeffentlicht);
            await Erzeugen(t, db, FblSprachen).Aufgaben.UebernehmenAsync(id, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            await Erzeugen(t, db, FblSprachen).Routinen.MindestzahlEntscheidenAsync(id, true, null, ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var k = await db.Kurse.Include(x => x.Veranstaltungen).SingleAsync(ct);
            k.Status.ShouldBe(Kursstatus.Abgesagt);
            k.Veranstaltungen.ShouldAllBe(v => v.Ausgefallen);
            (await db.Belegungen.ToListAsync(ct)).ShouldAllBe(b => b.Status == Belegungsstatus.Storniert && b.GebuehrCent == 0);
            var mails = await db.Postausgang.Where(n => n.Betreff.StartsWith("Absage:")).ToListAsync(ct);
            mails.Count.ShouldBe(2);
            mails[0].Inhalt.ShouldContain("Mindestteilnehmerzahl nicht erreicht");
            mails[0].Inhalt.ShouldContain("/uebersetzen/");
            var a = await db.Aufgaben.SingleAsync(ct);
            a.Status.ShouldBe(Aufgabenstatus.Erledigt);
            a.Vermerk.ShouldBe("Kurs abgesagt, 2 Teilnehmende benachrichtigt");
        }

        // Abgesagt: kein neuer Vorschlag.
        (await MindestzahlPruefenAsync(t)).ShouldBe(Abgleichergebnis.Leer);
    }

    [Fact]
    public async Task Ist_die_Freigabe_abgeschaltet_sagt_die_Automatik_bei_Faelligkeit_selbst_ab()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursAsync(t, 1);
        await AnmeldenAsync(t, kurs, p[0]);

        await using (var db = t.NeuerKontext())
        {
            // Nur Leitung/Systemverwaltung stellen Regeln ein.
            await Should.ThrowAsync<InvalidOperationException>(() => Erzeugen(t, db, FblSprachen).Aufgaben
                .TypAendernAsync(Aufgabentypen.Mindestteilnehmer, Rolle.Fachbereichsleitung, true, null, 1, 10, false, ct));
            await Erzeugen(t, db, Leitung).Aufgaben.TypAendernAsync(Aufgabentypen.Mindestteilnehmer, Rolle.Fachbereichsleitung, true, null, 1, 10, false, ct);
        }

        (await MindestzahlPruefenAsync(t)).Neu.ShouldBe(1); // erst Vorschlag, fällig 21.11.
        t.Uhr.Advance(TimeSpan.FromDays(6)); // 21.11.
        await using (var db = t.NeuerKontext())
        {
            var (ergebnis, abgesagt) = await Erzeugen(t, db).Routinen.MindestteilnehmerPruefenAsync(ct);
            abgesagt.ShouldBe(1);
            ergebnis.Geschlossen.ShouldBe(1);
        }

        await using (var db = t.NeuerKontext())
        {
            (await db.Kurse.SingleAsync(ct)).Status.ShouldBe(Kursstatus.Abgesagt);
            (await db.Aufgaben.SingleAsync(ct)).Vermerk!.ShouldContain("automatisch abgesagt");
            (await db.Protokoll.AnyAsync(e => e.Aktion == "Kurs.Abgesagt" && e.BenutzerKennung == "automatik", ct)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Erinnerung_vor_Kursbeginn_geht_einmal_an_jede_angemeldete_Person()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var (kurs, p) = await KursAsync(t, 3, start: new DateOnly(2026, 11, 17));
        await AnmeldenAsync(t, kurs, p[0]);
        await AnmeldenAsync(t, kurs, p[1]);

        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db).Routinen.ErinnerungenAsync(1, ct)).ShouldBe(0); // beginnt erst übermorgen
            (await Erzeugen(t, db).Routinen.ErinnerungenAsync(2, ct)).ShouldBe(2);
        }

        await AnmeldenAsync(t, kurs, p[2]);
        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db).Routinen.ErinnerungenAsync(2, ct)).ShouldBe(1); // nur die neu Angemeldete
            var mails = await db.Postausgang.Where(n => n.Betreff.StartsWith("Bald geht es los")).ToListAsync(ct);
            mails.Count.ShouldBe(3);
            mails.ShouldAllBe(m => m.Inhalt.Contains("Di 17.11.2026, 18:00 Uhr") && m.Inhalt.Contains("/uebersetzen/"));
        }
    }

    [Fact]
    public async Task Fehlgeschlagene_Nachrichten_werden_zur_Aufgabe_der_Systemverwaltung()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await KursAsync(t, 0);
        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db).Routinen.PostausgangPruefenAsync(ct)).ShouldBe(Abgleichergebnis.Leer);
        }

        Guid nachricht;
        await using (var db = t.NeuerKontext())
        {
            var n = new AusgehendeNachricht(Kanal.EMail, "x@example.org", null, "Test", "Text", "de", t.Uhr.GetUtcNow(), "Test");
            while (n.Status != NachrichtenStatus.Fehlgeschlagen)
            {
                n.FehlversuchVermerken("Postfach voll");
            }

            db.Postausgang.Add(n);
            await db.SaveChangesAsync(ct);
            nachricht = n.Id;
        }

        await using (var db = t.NeuerKontext())
        {
            (await Erzeugen(t, db).Routinen.PostausgangPruefenAsync(ct)).Neu.ShouldBe(1);
            var a = await db.Aufgaben.SingleAsync(ct);
            a.Gruppe.ShouldBe(nameof(Rolle.Systemverwaltung));
            a.Titel.ShouldStartWith("1 fehlgeschlagene");
        }

        await using (var db = t.NeuerKontext())
        {
            (await db.Postausgang.SingleAsync(n => n.Id == nachricht, ct)).ErneutVersuchen();
            await db.SaveChangesAsync(ct);
            (await Erzeugen(t, db).Routinen.PostausgangPruefenAsync(ct)).Geschlossen.ShouldBe(1);
        }
    }
}
