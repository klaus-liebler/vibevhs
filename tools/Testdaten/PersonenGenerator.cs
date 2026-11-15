using Bogus;
using Person = VibeVHS.Domain.Personen.Person;

namespace VibeVHS.Testdaten;

/// <summary>
/// Erzeugt reproduzierbare Testpersonen (fester Startwert: gleiche Eingabe, gleiche Personen).
/// Die Namensmischung entspricht grob einer VHS-Kundschaft inkl. Integrationskursen.
/// </summary>
public sealed class PersonenGenerator(int startwert = 20261115)
{
    private static readonly (string Sprache, string[] Vornamen, string[] Nachnamen)[] Herkunft =
    [
        ("tr", ["Ayşe", "Fatma", "Elif", "Zeynep", "Mehmet", "Mustafa", "Emre", "Ali"], ["Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Öztürk", "Aydın"]),
        ("uk", ["Olena", "Iryna", "Daria", "Dmytro", "Oleksandr", "Andrij"], ["Kovalenko", "Shevchenko", "Bondarenko", "Melnyk", "Tkachenko"]),
        ("ar", ["Nadia", "Fatima", "Amira", "Ahmad", "Samir", "Omar", "Yusuf"], ["Haddad", "Al-Hassan", "Nouri", "Khalil", "Mansour"]),
        ("fa", ["Zahra", "Maryam", "Reza", "Ali"], ["Rahimi", "Hosseini", "Ahmadi", "Karimi"]),
        ("pl", ["Agnieszka", "Ewa", "Piotr", "Tomasz"], ["Nowak", "Kowalski", "Wiśniewska", "Wójcik"]),
        ("en", ["Grace", "James", "Emily"], ["Okafor", "Smith", "Brown"]),
    ];

    public IReadOnlyList<Person> Erzeugen(int anzahl, DateTimeOffset jetzt)
    {
        Randomizer.Seed = new Random(startwert);
        var f = new Faker("de");
        var personen = new List<Person>(anzahl);
        var heute = DateOnly.FromDateTime(jetzt.Date);

        for (var i = 0; i < anzahl; i++)
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
            var geburt = heute.AddYears(-f.Random.Int(17, 85)).AddDays(-f.Random.Int(0, 364));
            var mail = f.Random.Bool(0.85f) ? f.Internet.Email(vorname, nachname, "example.org") : null;

            var person = Person.Anlegen(vorname, nachname, erfasstTag, erfasst, geburt, mail, sprache);

            // Etwa 5 % haben eine spätere Namensänderung (z. B. Heirat), 2 % eine Korrektur.
            if (f.Random.Bool(0.05f) && erfasstTag < heute.AddDays(-60))
            {
                var ab = erfasstTag.AddDays(f.Random.Int(30, heute.DayNumber - erfasstTag.DayNumber - 1));
                var gemeldet = erfasst.AddDays(ab.DayNumber - erfasstTag.DayNumber + 10);
                person.NameAendernAb(vorname, f.Name.LastName(), ab, "Heirat (Testdaten)", gemeldet < jetzt ? gemeldet : jetzt);
            }
            else if (f.Random.Bool(0.02f))
            {
                person.NameKorrigieren(vorname, nachname + "n", erfasstTag, "Tippfehler (Testdaten)", erfasst.AddDays(2));
            }

            personen.Add(person);
        }

        return personen;
    }
}
