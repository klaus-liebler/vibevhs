using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Domain.Tests.Anmeldung;

/// <summary>Gemeinsame Testdaten: ein Herbstsemester, Kurse ab Dezember 2026, "heute" ist der 15.11.2026.</summary>
internal static class Testkurse
{
    public static readonly DateTimeOffset Jetzt = new(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(1));
    public static readonly DateOnly Heute = DateOnly.FromDateTime(Jetzt.Date);
    public static readonly Semester Herbst = new("Herbst 2026", "26H", new DateOnly(2026, 8, 24), new DateOnly(2027, 1, 31));
    public static readonly Guid Raum = Guid.NewGuid();
    public static readonly Buchender Tresen = new("Katja Friedsen", "katja.friedsen", "Teilnehmerservice");

    public static Kurs Kurs(Buchungsrichtlinie? richtlinie = null, Sichtbarkeit sichtbarkeit = Sichtbarkeit.Oeffentlich, int max = 12,
        Kursstatus status = Kursstatus.Veroeffentlicht, DateOnly? start = null, int termine = 6, long cent = 9800)
    {
        var art = new Kursart("Sprachkurs", Nutzungsart.Seminar, [], richtlinie ?? Buchungsrichtlinie.Oeffentlich, sichtbarkeit);
        var kurs = new Kurs("26H-501", "Englisch A2", Herbst, 5, art, 3, max);
        kurs.EntgeltSetzenAb(new Geld(cent), new Geld(cent / 2), Herbst.Beginn, Erfassung.Erst(Jetzt.AddMonths(-3)));
        kurs.SerieFestlegen(new Serienregel
        {
            Start = start ?? new DateOnly(2026, 12, 1), Anzahl = termine, Wochentage = [DayOfWeek.Tuesday], Beginn = new TimeOnly(18, 0), DauerMinuten = 90,
            OrtIds = [Raum],
        }, Kalender.OhneFerien, new DateOnly(2027, 6, 30));
        kurs.StatusSetzen(status);
        return kurs;
    }

    public static Person Person(string vorname = "Olena", string nachname = "Kovalenko")
    {
        var p = VibeVHS.Domain.Personen.Person.Anlegen(vorname, nachname, new DateOnly(2025, 3, 1), Jetzt.AddYears(-1), new DateOnly(1991, 1, 21), "o@example.org", "uk");
        p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2025, 3, 1), Erfassung.Erst(Jetzt.AddYears(-1)));
        return p;
    }

    public static Buchungsergebnis Pruefen(Kurs kurs, Person person, Buchungsweg weg = Buchungsweg.Tresen, Guid? bev = null, int belegt = 0, int wartend = 0,
        Belegungsstatus? bestehend = null, string? auftraggeber = null)
        => Buchungspruefung.Pruefen(kurs, person, weg, bev, new Buchungslage(belegt, wartend, bestehend, auftraggeber), Jetzt);
}

public class BuchungspruefungTests
{
    private readonly Person _olena = Testkurse.Person();

    [Fact]
    public void Offener_Kurs_ist_buchbar()
        => Testkurse.Pruefen(Testkurse.Kurs(), _olena).Code.ShouldBe(Buchungscode.Buchbar);

    [Fact]
    public void Abgesagt()
    {
        var e = Testkurse.Pruefen(Testkurse.Kurs(status: Kursstatus.Abgesagt), _olena);
        e.Code.ShouldBe(Buchungscode.Abgesagt);
        e.Erklaerung.ShouldContain("abgesagt");
        e.Weg.ShouldNotBeNull();
    }

    [Fact]
    public void Entwurf_nennt_den_Weg_ueber_die_Fachbereichsleitung()
    {
        var e = Testkurse.Pruefen(Testkurse.Kurs(status: Kursstatus.Entwurf), _olena);
        e.Code.ShouldBe(Buchungscode.Entwurf);
        e.Weg!.ShouldContain("Fachbereichsleitung");
    }

    [Fact]
    public void Archiviert()
    {
        var kurs = Testkurse.Kurs();
        kurs.Archivieren(Testkurse.Jetzt);
        Testkurse.Pruefen(kurs, _olena).Code.ShouldBe(Buchungscode.Archiviert);
    }

    [Fact]
    public void Beendeter_Kurs()
        => Testkurse.Pruefen(Testkurse.Kurs(start: new DateOnly(2026, 9, 1), termine: 4), _olena).Code.ShouldBe(Buchungscode.Beendet);

    [Fact]
    public void Gesperrte_oder_archivierte_Person()
    {
        var p = Testkurse.Person();
        p.Archivieren(Testkurse.Jetzt);
        var e = Testkurse.Pruefen(Testkurse.Kurs(), p);
        e.Code.ShouldBe(Buchungscode.PersonNichtAktiv);
        e.Weg!.ShouldContain("reaktivieren");
    }

    [Fact]
    public void Interner_Kurs_ohne_Berechtigung_nennt_das_Integrationskurs_Team()
    {
        var e = Testkurse.Pruefen(Testkurse.Kurs(Buchungsrichtlinie.Integrationskurs, Sichtbarkeit.Intern), _olena, Buchungsweg.Tresen);
        e.Code.ShouldBe(Buchungscode.InternOhneBerechtigung);
        e.Erklaerung.ShouldContain("Integrationskurs-Team");
        Testkurse.Pruefen(Testkurse.Kurs(Buchungsrichtlinie.Integrationskurs, Sichtbarkeit.Intern), _olena, Buchungsweg.IntegrationskursTeam)
            .Code.ShouldBe(Buchungscode.Buchbar);
    }

    [Fact]
    public void Weg_nicht_erlaubt()
    {
        var nurTresen = new Buchungsrichtlinie { Rechte = [new(Buchungsweg.Tresen, true)] };
        var e = Testkurse.Pruefen(Testkurse.Kurs(nurTresen), _olena, Buchungsweg.Auftraggeber);
        e.Code.ShouldBe(Buchungscode.WegNichtErlaubt);
        e.Erklaerung.ShouldContain("Teilnehmerservice");
    }

    [Fact]
    public void Selbstregistrierte_buchen_nur_oeffentliche_Kurse_unabhaengig_von_der_Richtlinie()
    {
        var auchOnline = new Buchungsrichtlinie { Rechte = [new(Buchungsweg.Tresen, true), new(Buchungsweg.Online, true)] };
        Testkurse.Pruefen(Testkurse.Kurs(auchOnline), _olena, Buchungsweg.Online).Code.ShouldBe(Buchungscode.Buchbar);
        Testkurse.Pruefen(Testkurse.Kurs(auchOnline, Sichtbarkeit.Intern), _olena, Buchungsweg.Online).Code.ShouldBe(Buchungscode.NurOeffentlich);

        var org = new Organisation("Stadtwerke", Organisationsart.Firma);
        var geschlossen = Testkurse.Kurs(auchOnline).MitSichtbarkeit(Sichtbarkeit.Geschlossen, org.Id);
        _olena.OrganisationZuordnenAb(org, null, new DateOnly(2026, 1, 1), Erfassung.Erst(Testkurse.Jetzt));
        Testkurse.Pruefen(geschlossen, _olena, Buchungsweg.Online).Code.ShouldBe(Buchungscode.NurOeffentlich);
    }

    [Fact]
    public void Geschlossener_Kurs_nur_fuer_die_Zielgruppe()
    {
        var org = new Organisation("Stadtwerke Beispielstadt", Organisationsart.Firma);
        var kurs = Testkurse.Kurs(Buchungsrichtlinie.Firmenkurs).MitSichtbarkeit(Sichtbarkeit.Geschlossen, org.Id);
        var e = Testkurse.Pruefen(kurs, _olena, auftraggeber: org.Name); // Name zur Laufzeit, keine Kopie am Kurs
        e.Code.ShouldBe(Buchungscode.GeschlossenZielgruppe);
        e.Erklaerung.ShouldContain("Stadtwerke Beispielstadt");

        _olena.OrganisationZuordnenAb(org, "Mitarbeitende", new DateOnly(2026, 1, 1), Erfassung.Erst(Testkurse.Jetzt));
        Testkurse.Pruefen(kurs, _olena).Code.ShouldBe(Buchungscode.Buchbar);
    }

    [Fact]
    public void Vollmacht_fehlt_abgelaufen_oder_gueltig()
    {
        var mutter = Testkurse.Person("Ayşe", "Demir");
        var kind = Testkurse.Person("Elif", "Demir");
        Testkurse.Pruefen(Testkurse.Kurs(), kind, bev: mutter.Id).Code.ShouldBe(Buchungscode.VollmachtFehlt);

        kind.VollmachtErteilen(Bevollmaechtigtenart.Person, mutter.Id, "Ayşe Demir", "Sorgerecht", new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1),
            Erfassung.Erst(Testkurse.Jetzt.AddYears(-2)));
        var abgelaufen = Testkurse.Pruefen(Testkurse.Kurs(), kind, bev: mutter.Id);
        abgelaufen.Code.ShouldBe(Buchungscode.VollmachtAbgelaufen);
        abgelaufen.Weg!.ShouldContain("Vollmacht");

        var id = kind.VollmachtErteilen(Bevollmaechtigtenart.Person, mutter.Id, "Ayşe Demir", "Sorgerecht, Geburtsurkunde", new DateOnly(2026, 1, 1), null,
            Erfassung.Erst(Testkurse.Jetzt));
        var ok = Testkurse.Pruefen(Testkurse.Kurs(), kind, bev: mutter.Id);
        ok.Code.ShouldBe(Buchungscode.Buchbar);
        ok.VollmachtId.ShouldBe(id);
    }

    [Fact]
    public void Bereits_angemeldet_oder_wartend()
    {
        Testkurse.Pruefen(Testkurse.Kurs(), _olena, bestehend: Belegungsstatus.Angemeldet).Code.ShouldBe(Buchungscode.BereitsAngemeldet);
        Testkurse.Pruefen(Testkurse.Kurs(), _olena, bestehend: Belegungsstatus.Nachrueckangebot).Code.ShouldBe(Buchungscode.BereitsAngemeldet);
        Testkurse.Pruefen(Testkurse.Kurs(), _olena, bestehend: Belegungsstatus.Warteliste).Code.ShouldBe(Buchungscode.BereitsWartend);
        Testkurse.Pruefen(Testkurse.Kurs(), _olena, bestehend: null).Code.ShouldBe(Buchungscode.Buchbar);
    }

    [Fact]
    public void Ausgebucht_fuehrt_zur_Warteliste_mit_Platznummer_und_Ueberbuchungsrecht_am_Tresen()
    {
        var e = Testkurse.Pruefen(Testkurse.Kurs(max: 12), _olena, belegt: 12, wartend: 2);
        e.Code.ShouldBe(Buchungscode.Warteliste);
        e.Moeglich.ShouldBeTrue();
        e.Begruendung!.ShouldContain("Platz 3");
        e.Ueberbuchbar.ShouldBeTrue(); // Tresen darf bei öffentlichen Kursen überbuchen
        Testkurse.Pruefen(Testkurse.Kurs(max: 12), _olena, Buchungsweg.Online, belegt: 12).Ueberbuchbar.ShouldBeFalse();
    }

    [Fact]
    public void Ausgebucht_ohne_Warteliste()
    {
        var e = Testkurse.Pruefen(Testkurse.Kurs(Buchungsrichtlinie.Firmenkurs, max: 8), _olena, belegt: 8);
        e.Code.ShouldBe(Buchungscode.Ausgebucht);
        e.Moeglich.ShouldBeFalse();
    }

    [Fact]
    public void Kontingentkurse_pruefen_die_Kapazitaet_erst_bei_der_Einbuchung()
        => Testkurse.Pruefen(Testkurse.Kurs(Buchungsrichtlinie.Kontingentkurs(10, 6), max: 14), _olena, belegt: 30).Code.ShouldBe(Buchungscode.Buchbar);

    [Fact]
    public void Jeder_Code_ausser_Buchbar_hat_eine_Begruendung()
    {
        var kurs = Testkurse.Kurs(status: Kursstatus.Abgesagt);
        foreach (var e in new[]
                 {
                     Testkurse.Pruefen(kurs, _olena),
                     Testkurse.Pruefen(Testkurse.Kurs(), _olena, bestehend: Belegungsstatus.Angemeldet),
                     Testkurse.Pruefen(Testkurse.Kurs(max: 3), _olena, belegt: 3),
                 })
        {
            e.Erklaerung.ShouldNotBeNullOrWhiteSpace();
        }
    }
}

public class BelegungTests
{
    [Fact]
    public void Entgelt_bleibt_bei_spaeterer_Preiserhoehung_eingefroren()
    {
        var kurs = Testkurse.Kurs(cent: 9800);
        var person = Testkurse.Person();
        var b = Belegung.Anlegen(kurs, person.Id, false, Buchungsweg.Tresen, Testkurse.Tresen, null, Zahlart.Lastschrift, Testkurse.Jetzt);
        b.EntgeltCent.ShouldBe(9800);

        kurs.EntgeltSetzenAb(new Geld(12000), null, Testkurse.Herbst.Beginn, Erfassung.Aenderung(Testkurse.Jetzt.AddDays(1), "Preiserhöhung"));
        b.EntgeltCent.ShouldBe(9800);
        Belegung.Anlegen(kurs, Guid.NewGuid(), false, Buchungsweg.Tresen, Testkurse.Tresen, null, Zahlart.Bar, Testkurse.Jetzt.AddDays(2))
            .EntgeltCent.ShouldBe(12000);
    }

    [Fact]
    public void Ermaessigung_mit_Nachweis_wird_angewendet_und_vermerkt()
    {
        var kurs = Testkurse.Kurs(cent: 9800);
        var p = Testkurse.Person();
        var erm = new Ermaessigung(p.Id, "Bürgergeld / Grundsicherung", "Bescheid vom 02.09.2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 3, 31),
            Testkurse.Jetzt, "Test");
        var b = Belegung.Anlegen(kurs, p.Id, false, Buchungsweg.Tresen, Testkurse.Tresen, erm, Zahlart.Ueberweisung, Testkurse.Jetzt);
        b.EntgeltCent.ShouldBe(4900);
        b.Ermaessigt.ShouldBeTrue();
        b.ErmaessigungNachweis!.ShouldContain("Bescheid vom 02.09.2026");
        erm.GiltAm(new DateOnly(2027, 4, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Belegung_speichert_wer_fuer_wen_mit_welcher_Vollmacht_ueber_welchen_Weg()
    {
        var vollmacht = Guid.NewGuid();
        var mutter = Guid.NewGuid();
        var b = Belegung.Anlegen(Testkurse.Kurs(), Guid.NewGuid(), false, Buchungsweg.Tresen,
            Testkurse.Tresen with { BevollmaechtigterId = mutter, BevollmaechtigterName = "Ayşe Demir", VollmachtId = vollmacht }, null, Zahlart.Bar,
            Testkurse.Jetzt);
        b.GebuchtVon.ShouldBe("Katja Friedsen");
        b.GebuchtInRolle.ShouldBe("Teilnehmerservice");
        b.BevollmaechtigterId.ShouldBe(mutter);
        b.VollmachtId.ShouldBe(vollmacht);
        b.Weg.ShouldBe(Buchungsweg.Tresen);
    }

    [Fact]
    public void Storno_rechtzeitig_kostenlos_danach_bleibt_das_Entgelt_faellig()
    {
        var kurs = Testkurse.Kurs(start: new DateOnly(2026, 12, 1));
        var b = Belegung.Anlegen(kurs, Guid.NewGuid(), false, Buchungsweg.Tresen, Testkurse.Tresen, null, Zahlart.Bar, Testkurse.Jetzt);
        Buchungspruefung.Storno(b, kurs, Testkurse.Jetzt).GebuehrCent.ShouldBe(0);
        var spaet = Buchungspruefung.Storno(b, kurs, new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.FromHours(1)));
        spaet.GebuehrCent.ShouldBe(b.EntgeltCent);
        spaet.Erklaerung.ShouldContain("AGB");
    }

    [Fact]
    public void Warteliste_Nachrueckangebot_mit_Frist_annehmen_oder_verfallen()
    {
        var kurs = Testkurse.Kurs();
        var b = Belegung.Anlegen(kurs, Guid.NewGuid(), true, Buchungsweg.Tresen, Testkurse.Tresen, null, Zahlart.Bar, Testkurse.Jetzt);
        b.Status.ShouldBe(Belegungsstatus.Warteliste);
        b.HaeltPlatz.ShouldBeFalse();
        b.NachrueckenAnbieten(Testkurse.Jetzt.AddDays(3));
        b.HaeltPlatz.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => b.AngebotAnnehmen(kurs.Richtlinie, Testkurse.Jetzt.AddDays(4), "Test")).Message.ShouldContain("abgelaufen");
        b.AngebotAnnehmen(kurs.Richtlinie, Testkurse.Jetzt.AddDays(1), "Test");
        b.Status.ShouldBe(Belegungsstatus.Angemeldet);
    }
}

public class KontingentTests
{
    private static readonly Buchungsrichtlinie Zehnerkarte = Buchungsrichtlinie.Kontingentkurs(10, 6);

    private static (Kurs Kurs, Belegung Belegung) Pilates(Buchungsrichtlinie? r = null)
    {
        var kurs = Testkurse.Kurs(r ?? Zehnerkarte, max: 14, start: new DateOnly(2026, 9, 1), termine: 30);
        var b = Belegung.Anlegen(kurs, Guid.NewGuid(), false, Buchungsweg.Tresen, Testkurse.Tresen, null, Zahlart.Lastschrift,
            new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.FromHours(2)));
        return (kurs, b);
    }

    private static DateTimeOffset VorBeginn(Veranstaltung v) => new(v.Beginn.AddDays(-2), TimeSpan.FromHours(1));

    [Fact]
    public void Kontingentstand_ist_die_Summe_der_Ereignisse()
    {
        var (kurs, b) = Pilates();
        var v = kurs.Veranstaltungen.OrderBy(x => x.Beginn).ToList();
        foreach (var x in v.Take(5))
        {
            b.Einbuchen(x, Testkurse.Raum, 0, 14, kurs.Richtlinie, VorBeginn(x), "Test").Code.ShouldBe(Einbuchungscode.Eingebucht);
        }

        b.AnwesenheitFesthalten(v[0].Id, true, kurs.Richtlinie, VorBeginn(v[0]).AddDays(3), "Test");
        b.AnwesenheitFesthalten(v[1].Id, true, kurs.Richtlinie, VorBeginn(v[1]).AddDays(3), "Test");
        b.AnwesenheitFesthalten(v[2].Id, false, kurs.Richtlinie, VorBeginn(v[2]).AddDays(3), "Test"); // Nichterscheinen zählt
        b.Austragen(v[3].Id, v[3].Beginn, kurs.Richtlinie, VorBeginn(v[3]), "Test");

        var s = b.Kontingent;
        (s.Anzahl, s.Besucht, s.Verfallen, s.Eingebucht, s.Verbleibend).ShouldBe((10, 2, 1, 1, 6));
        b.Ereignisse.Count.ShouldBe(1 + 5 + 3 + 1);
        b.Ereignisse.Select(e => e.Laufnummer).ShouldBe(Enumerable.Range(1, 10));
    }

    [Fact]
    public void Nichterscheinen_zaehlt_nicht_wenn_der_Kurs_es_so_regelt()
    {
        var (kurs, b) = Pilates(Zehnerkarte with { NichterscheinenZaehlt = false });
        var v = kurs.Veranstaltungen.OrderBy(x => x.Beginn).First();
        b.Einbuchen(v, Testkurse.Raum, 0, 14, kurs.Richtlinie, VorBeginn(v), "Test");
        b.AnwesenheitFesthalten(v.Id, false, kurs.Richtlinie, VorBeginn(v).AddDays(3), "Test");
        b.Kontingent.Verbleibend.ShouldBe(10);
        b.Kontingent.Verfallen.ShouldBe(0);
    }

    [Fact]
    public void Aufgebrauchtes_Kontingent_sperrt_weitere_Einbuchungen_mit_Begruendung()
    {
        var (kurs, b) = Pilates(Buchungsrichtlinie.Kontingentkurs(2, 6));
        var v = kurs.Veranstaltungen.OrderBy(x => x.Beginn).ToList();
        b.Einbuchen(v[0], Testkurse.Raum, 0, 14, kurs.Richtlinie, VorBeginn(v[0]), "Test");
        b.Einbuchen(v[1], Testkurse.Raum, 0, 14, kurs.Richtlinie, VorBeginn(v[1]), "Test");
        var e = b.EinbuchungPruefen(v[2], Testkurse.Raum, 0, 14, kurs.Richtlinie, VorBeginn(v[2]));
        e.Code.ShouldBe(Einbuchungscode.KontingentAufgebraucht);
        e.Begruendung!.ShouldContain("Neues Kontingent");
    }

    [Fact]
    public void Kapazitaetsgrenze_bei_Vorab_Einbuchung_fuehrt_zur_Warteliste_der_Veranstaltung()
    {
        var (kurs, b) = Pilates();
        var v = kurs.Veranstaltungen.OrderBy(x => x.Beginn).First();
        var e = b.Einbuchen(v, Testkurse.Raum, 14, 14, kurs.Richtlinie, VorBeginn(v), "Test");
        e.Code.ShouldBe(Einbuchungscode.Warteliste);
        e.Begruendung!.ShouldContain("14 von 14");
        b.Einbuchungen.ShouldBeEmpty();
        b.WartetAuf.Keys.ShouldContain(v.Id);
        b.Kontingent.Verbleibend.ShouldBe(10); // Warteliste verbraucht nichts

        // Wird ein Platz frei, bucht die Warteliste ein
        b.Einbuchen(v, Testkurse.Raum, 13, 14, kurs.Richtlinie, VorBeginn(v), "Automatik").Code.ShouldBe(Einbuchungscode.Eingebucht);
        b.WartetAuf.ShouldBeEmpty();
        b.Kontingent.Eingebucht.ShouldBe(1);
    }

    [Fact]
    public void Einbuchen_und_Austragen_nur_bis_zur_Frist()
    {
        var (kurs, b) = Pilates();
        var v = kurs.Veranstaltungen.OrderBy(x => x.Beginn).First();
        var zuSpaet = new DateTimeOffset(v.Beginn.AddHours(-2), TimeSpan.FromHours(2));
        b.EinbuchungPruefen(v, Testkurse.Raum, 0, 14, kurs.Richtlinie, zuSpaet).Code.ShouldBe(Einbuchungscode.FristAbgelaufen);
        b.Einbuchen(v, Testkurse.Raum, 0, 14, kurs.Richtlinie, VorBeginn(v), "Test");
        Should.Throw<InvalidOperationException>(() => b.Austragen(v.Id, v.Beginn, kurs.Richtlinie, zuSpaet, "Test")).Message.ShouldContain("Nichterscheinen");
    }

    [Fact]
    public void Kontingent_laeuft_ab_und_der_Rest_verfaellt()
    {
        var (kurs, b) = Pilates();
        var v = kurs.Veranstaltungen.OrderBy(x => x.Beginn).First();
        b.Einbuchen(v, Testkurse.Raum, 0, 14, kurs.Richtlinie, VorBeginn(v), "Test");
        b.AnwesenheitFesthalten(v.Id, true, kurs.Richtlinie, VorBeginn(v).AddDays(3), "Test");
        b.KontingentGueltigBis.ShouldBe(new DateOnly(2027, 2, 25));
        b.KontingentAblaufenLassen(new DateOnly(2027, 2, 26), Testkurse.Jetzt.AddMonths(4)).ShouldBe(9);
        b.Kontingent.Verbleibend.ShouldBe(0);
        b.KontingentAblaufenLassen(new DateOnly(2027, 3, 1), Testkurse.Jetzt.AddMonths(4)).ShouldBe(0); // nur einmal
    }
}
