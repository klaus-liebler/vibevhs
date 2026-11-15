using Bogus;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using Person = VibeVHS.Domain.Personen.Person;

namespace VibeVHS.Adapters.Mocks.Testdaten;

/// <summary>Ergebnis des Generators.</summary>
public sealed record Massendatensatz(IReadOnlyList<Organisation> Organisationen, IReadOnlyList<Person> Personen, int GeplanteDubletten);

/// <summary>
/// Erzeugt reproduzierbare, fiktive Testpersonen (fester Startwert: gleiche Eingabe → gleiche Daten) für eine VHS
/// im Raum Emsdetten/Greven/Saerbeck: Rollen, Status, Fachbereiche, Adressen, Rufnummern, Einwilligungen,
/// Organisationen, Vollmachten, Namensänderungen und absichtlich angelegte Dubletten (z. B. "Yilmaz"/"Yılmaz").
/// Alle Adressen, Nummern und E-Mail-Adressen sind erfunden (example.org, Nummern 0000…).
/// </summary>
public sealed class Massendaten(int startwert = 20261115)
{
    private const string Von = "Testdaten";

    private static readonly (string Sprache, string[] Vornamen, string[] Nachnamen)[] Herkunft =
    [
        ("tr", ["Ayşe", "Fatma", "Elif", "Zeynep", "Mehmet", "Mustafa", "Emre", "Ali"], ["Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Öztürk", "Aydın"]),
        ("uk", ["Olena", "Iryna", "Daria", "Dmytro", "Oleksandr", "Andrij"], ["Kovalenko", "Shevchenko", "Bondarenko", "Melnyk", "Tkachenko"]),
        ("ar", ["Nadia", "Fatima", "Amira", "Ahmad", "Samir", "Omar", "Yusuf"], ["Haddad", "Al-Hassan", "Nouri", "Khalil", "Mansour"]),
        ("fa", ["Zahra", "Maryam", "Reza", "Ali"], ["Rahimi", "Hosseini", "Ahmadi", "Karimi"]),
        ("pl", ["Agnieszka", "Ewa", "Piotr", "Tomasz"], ["Nowak", "Kowalski", "Wiśniewska", "Wójcik"]),
        ("en", ["Grace", "James", "Emily"], ["Okafor", "Smith", "Brown"]),
    ];

    private static readonly (string Ort, string Plz)[] Orte =
    [
        ("Emsdetten", "48282"), ("Emsdetten", "48282"), ("Emsdetten", "48282"), ("Greven", "48268"), ("Greven", "48268"),
        ("Saerbeck", "48369"), ("Nordwalde", "48356"), ("Reckenfeld", "48268"), ("Hembergen", "48282"),
    ];

    private static readonly string[] Strassen =
    [
        "Lindenstraße", "Kirchstraße", "Bahnhofstraße", "Mühlenweg", "Emsstraße", "Grevener Damm", "Am Sportplatz",
        "Schulstraße", "Birkenweg", "Ahornweg", "Rheiner Straße", "Marktplatz", "Gartenstraße", "Wiesenweg",
    ];

    public Massendatensatz Erzeugen(int anzahl, DateTimeOffset jetzt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(anzahl, 1);
        Randomizer.Seed = new Random(startwert);
        var f = new Faker("de");
        var heute = DateOnly.FromDateTime(jetzt.Date);

        var organisationen = new List<Organisation>
        {
            new("Stadtwerke Beispielstadt GmbH (fiktiv)", Organisationsart.Firma, "Emsdetten"),
            new("Jobcenter Beispielkreis (fiktiv)", Organisationsart.Jobcenter, "Steinfurt"),
            new("Ausländerbehörde Beispielkreis (fiktiv)", Organisationsart.Behoerde, "Steinfurt"),
            new("Pflegedienst Sonnenschein (fiktiv)", Organisationsart.Firma, "Greven"),
            new("Sportverein Blau-Weiß (fiktiv)", Organisationsart.Verein, "Saerbeck"),
            new("Gesamtschule am Fluss (fiktiv)", Organisationsart.Schule, "Emsdetten"),
        };

        var personen = new List<Person>(anzahl);
        for (var i = 0; i < anzahl; i++)
        {
            personen.Add(EinePerson(f, jetzt, heute, organisationen, personen));
        }

        // Absichtlich angelegte Dubletten: andere Schreibweise, gleiches Geburtsdatum.
        var dubletten = Math.Max(1, anzahl / 250);
        for (var i = 0; i < dubletten && i < personen.Count; i++)
        {
            var original = personen[f.Random.Int(0, personen.Count - 1)];
            var stand = original.StandAm(heute, jetzt);
            if (stand.Name is null || original.Geburtsdatum is null)
            {
                continue;
            }

            var nachname = Schreibvariante(stand.Name.Nachname);
            var erfasst = jetzt.AddDays(-f.Random.Int(1, 400));
            var kopie = Person.Anlegen(stand.Name.Vorname, nachname, DateOnly.FromDateTime(erfasst.Date), erfasst, original.Geburtsdatum,
                null, original.Sprache, Von);
            kopie.RolleHinzufuegen(Personenrolle.Teilnehmende, DateOnly.FromDateTime(erfasst.Date), Erfassung.Erst(erfasst, Von));
            kopie.StatusSetzen(Personenstatus.Aktiv);
            personen.Add(kopie);
        }

        return new Massendatensatz(organisationen, personen, dubletten);
    }

    private static Person EinePerson(Faker f, DateTimeOffset jetzt, DateOnly heute, List<Organisation> organisationen, List<Person> bisher)
    {
        string vorname, nachname, sprache;
        if (f.Random.Bool(0.7f))
        {
            vorname = f.Name.FirstName();
            nachname = f.Name.LastName();
            sprache = "de";
        }
        else
        {
            var h = f.PickRandom(Herkunft);
            vorname = f.PickRandom(h.Vornamen);
            nachname = f.PickRandom(h.Nachnamen);
            sprache = f.Random.Bool(0.6f) ? h.Sprache : "de";
        }

        var erfasstTag = heute.AddDays(-f.Random.Int(0, 6 * 365));
        var erfasst = new DateTimeOffset(erfasstTag.ToDateTime(new TimeOnly(f.Random.Int(8, 16), f.Random.Int(0, 59))), jetzt.Offset);
        var e = Erfassung.Erst(erfasst, Von);
        var geburt = heute.AddYears(-f.Random.Int(17, 85)).AddDays(-f.Random.Int(0, 364));
        var mail = f.Random.Bool(0.85f) ? f.Internet.Email(vorname, nachname, "example.org") : null;

        var p = Person.Anlegen(vorname, nachname, erfasstTag, erfasst, geburt, mail, sprache, Von);

        // Rollen: überwiegend Teilnehmende, einige Dozierende (mit Fachbereich), wenige beides.
        var dozierend = f.Random.Bool(0.06f);
        p.RolleHinzufuegen(dozierend ? Personenrolle.Dozierende : Personenrolle.Teilnehmende, erfasstTag, e);
        if (dozierend)
        {
            if (f.Random.Bool(0.3f))
            {
                p.RolleHinzufuegen(Personenrolle.Teilnehmende, erfasstTag, e);
            }

            p.FachbereicheSetzen(f.Random.Bool(0.2f) ? [f.Random.Int(1, 8), f.Random.Int(1, 8)] : [f.Random.Int(1, 8)]);
        }

        // Status nach "letzter Aktivität": aktiv, ruhend, Löschung vorgemerkt.
        var los = f.Random.Double();
        p.StatusSetzen(los < 0.62 ? Personenstatus.Aktiv : los < 0.94 ? Personenstatus.Ruhend : Personenstatus.LoeschungVorgemerkt);

        var (ort, plz) = f.PickRandom(Orte);
        var adresse = p.AdresseHinzufuegen(null, $"{f.PickRandom(Strassen)} {f.Random.Int(1, 140)}", plz, ort, erfasstTag, e);
        p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, adresse, erfasstTag, e);

        if (f.Random.Bool(0.75f))
        {
            var dienste = Messagingdienst.Anruf | (f.Random.Bool(0.6f) ? Messagingdienst.Sms : 0) | (f.Random.Bool(0.4f) ? Messagingdienst.WhatsApp : 0)
                          | (f.Random.Bool(0.1f) ? Messagingdienst.Signal : 0);
            p.RufnummerHinzufuegen(RufnummerArt.Mobil, $"+49 15{f.Random.Int(1, 9)} 0000{f.Random.Int(1000, 9999)}", dienste,
                f.Random.Bool(0.7f), true, erfasstTag, e);
        }

        p.EinwilligungSetzenAb(Kontaktkanal.EMail, Kontaktzweck.Kursorganisation, true, erfasstTag, e);
        p.EinwilligungSetzenAb(Kontaktkanal.Sms, Kontaktzweck.Kursorganisation, f.Random.Bool(0.6f), erfasstTag, e);
        p.EinwilligungSetzenAb(Kontaktkanal.EMail, Kontaktzweck.Werbung, f.Random.Bool(0.35f), erfasstTag, e);

        var spaeter = erfasstTag < heute.AddDays(-60);
        if (spaeter && f.Random.Bool(0.05f))
        {
            var ab = erfasstTag.AddDays(f.Random.Int(30, heute.DayNumber - erfasstTag.DayNumber - 1));
            var gemeldet = Min(erfasst.AddDays(ab.DayNumber - erfasstTag.DayNumber + 10), jetzt);
            p.NameAendernAb(vorname, f.Name.LastName(), ab, "Heirat (Testdaten)", gemeldet, Von);
        }

        if (spaeter && f.Random.Bool(0.08f))
        {
            var ab = erfasstTag.AddDays(f.Random.Int(30, heute.DayNumber - erfasstTag.DayNumber - 1));
            var gemeldet = Min(erfasst.AddDays(ab.DayNumber - erfasstTag.DayNumber + 5), jetzt);
            var (ort2, plz2) = f.PickRandom(Orte);
            p.UmzugAb(adresse, null, $"{f.PickRandom(Strassen)} {f.Random.Int(1, 140)}", plz2, ort2, ab,
                Erfassung.Aenderung(gemeldet, "Umzug (Testdaten)", Von));
        }

        if (f.Random.Bool(0.04f))
        {
            var org = f.PickRandom(organisationen);
            p.OrganisationZuordnenAb(org, org.Art == Organisationsart.Jobcenter ? "Kundin/Kunde" : "Mitarbeitende", erfasstTag, e);
        }

        // Vollmachten: Elternteil/Betreuung (Person) oder Arbeitgeber/Jobcenter (Organisation).
        if (f.Random.Bool(0.02f) && bisher.Count > 10)
        {
            var bevollmaechtigt = bisher[f.Random.Int(0, bisher.Count - 1)];
            p.VollmachtErteilen(Bevollmaechtigtenart.Person, bevollmaechtigt.Id, bevollmaechtigt.Anzeigename,
                f.PickRandom("Sorgerecht (Geburtsurkunde)", "Betreuerausweis", "Schriftliche Vollmacht"), erfasstTag, null, e);
        }
        else if (f.Random.Bool(0.02f))
        {
            var org = f.PickRandom(organisationen.Where(o => o.Art is Organisationsart.Firma or Organisationsart.Jobcenter).ToList());
            p.VollmachtErteilen(Bevollmaechtigtenart.Organisation, org.Id, org.Name, "Bildungsgutschein / Kostenübernahme",
                erfasstTag, erfasstTag.AddMonths(12), e);
        }

        return p;
    }

    /// <summary>Andere Schreibweise desselben Namens, wie sie bei Dubletten vorkommt.</summary>
    public static string Schreibvariante(string nachname)
    {
        var varianten = new (string, string)[]
        {
            ("ı", "i"), ("ş", "s"), ("ç", "c"), ("ö", "oe"), ("ü", "ue"), ("ä", "ae"), ("ß", "ss"), ("ń", "n"), ("ś", "s"),
        };
        foreach (var (alt, neu) in varianten)
        {
            if (nachname.Contains(alt, StringComparison.Ordinal))
            {
                return nachname.Replace(alt, neu, StringComparison.Ordinal);
            }
        }

        // Kein Sonderzeichen: Tippfehler (zwei Buchstaben vertauscht).
        return nachname.Length > 3 ? nachname[..^3] + nachname[^2] + nachname[^3] + nachname[^1] : nachname + "e";
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
