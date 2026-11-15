using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Belegungen für die Demo-Kurse: typische Auslastung, volle Kurse mit Warteliste und Nachrückangebot, ein abgesagter Kurs mit
/// Teilnehmenden, Integrationskurse über das Integrationskurs-Team, ein Firmenkurs über den Auftraggeber und
/// Pilates fortlaufend mit Kontingenten, Einbuchungen, Besuchen und einer vollen Veranstaltung mit Warteliste.
/// </summary>
internal static class AnmeldungDemodaten
{
    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (await db.Belegungen.AnyAsync() || !await db.Kurse.AnyAsync())
        {
            return;
        }

        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var zufall = new Random(4711);
        var kurse = await db.Kurse.Include(k => k.Veranstaltungen).Where(k => k.ArchiviertAm == null).ToListAsync();
        var konten = await db.Benutzerkonten.AsNoTracking().ToListAsync();
        string Name(string login, string ersatz) => konten.FirstOrDefault(k => k.Login == login)?.Name ?? ersatz;
        var tresen = new[] { Name("katja.friesen", "Katja Friesen"), Name("celine.wilken", "Celine Wilken"), Name("linda.lueke", "Linda Lüke") }
            .Select(n => new Buchender(n, null, "Teilnehmerservice")).ToArray();
        var ikTeam = new Buchender(Name("sylvia.beckmann", "Sylvia Beckmann"), "sylvia.beckmann", "Integrationskurs-Team");

        var teilnehmende = await db.Personen.IgnoreAutoIncludes().AsNoTracking()
            .Where(p => (p.Rollen & Personenrolle.Teilnehmende) == Personenrolle.Teilnehmende && p.Status == Personenstatus.Aktiv
                        && p.Datenschutz == VibeVHS.Domain.Datenschutz.Datenschutzstatus.Aktiv && p.Benutzerkennung == null)
            .OrderBy(p => p.Sortierschluessel).Select(p => new { p.Id, p.Anzeigename }).Take(4000).ToListAsync();
        if (teilnehmende.Count == 0)
        {
            return;
        }

        Guid? PersonNamens(string name) => teilnehmende.FirstOrDefault(p => p.Anzeigename == name)?.Id;
        var mehmet = PersonNamens("Mehmet Yılmaz");
        var olena = PersonNamens("Olena Kovalenko");
        var sabine = PersonNamens("Sabine Weber");
        var nadia = PersonNamens("Nadia Haddad");
        var reserviert = new HashSet<Guid?> { mehmet, olena, sabine, nadia };
        var pool = teilnehmende.Where(p => !reserviert.Contains(p.Id)).Select(p => p.Id).OrderBy(_ => zufall.Next()).ToList();
        if (pool.Count == 0)
        {
            // Ohne Massendaten (z. B. Testsystem) nur die benannten Demopersonen; keine Füllbuchungen.
            pool = teilnehmende.Select(p => p.Id).ToList();
        }
        var zeiger = 0;
        Guid Naechste() => pool[zeiger++ % pool.Count];

        var stadtwerke = await db.Organisationen.AsNoTracking().Where(o => o.Name.StartsWith("Stadtwerke")).OrderBy(o => o.Name).ThenBy(o => o.Id).FirstOrDefaultAsync();
        var mitarbeitende = stadtwerke is null
            ? []
            : await db.Set<Organisationszugehoerigkeit>().AsNoTracking().Where(o => o.OrganisationId == stadtwerke.Id)
                .Select(o => EF.Property<Guid>(o, "PersonId")).Distinct().OrderBy(id => id).Take(10).ToListAsync();

        // Ermäßigung für Olena (Nachweis Bürgergeld-Bescheid)
        Ermaessigung? olenaErm = null;
        if (olena is { } oid)
        {
            olenaErm = new Ermaessigung(oid, "Bürgergeld / Grundsicherung", "Bescheid Jobcenter vom 02.09.2026 (Kopie in der Akte)",
                new DateOnly(2026, 9, 1), new DateOnly(2027, 3, 31), jetzt, "Demodaten");
            db.Ermaessigungen.Add(olenaErm);
        }

        var anzahl = 0;
        DateTimeOffset Buchungszeit(Kurs k)
        {
            var beginn = k.ErsterTermin ?? jetzt.DateTime;
            var start = new DateTime(2026, 8, 10, 9, 0, 0);
            var ende = beginn < jetzt.DateTime ? beginn.AddDays(-1) : jetzt.DateTime.AddDays(-1);
            if (ende <= start)
            {
                ende = start.AddDays(1);
            }

            var t = start.AddMinutes(zufall.NextDouble() * (ende - start).TotalMinutes);
            return new DateTimeOffset(new DateTime(t.Year, t.Month, t.Day, 8 + zufall.Next(10), zufall.Next(60), 0), jetzt.Offset);
        }

        Belegung Buchen(Kurs k, Guid personId, bool warteliste, Buchender wer, Zahlart zahlart, Buchungsweg weg = Buchungsweg.Tresen,
            DateTimeOffset? am = null, Ermaessigung? erm = null)
        {
            var b = Belegung.Anlegen(k, personId, warteliste, weg, wer, erm, zahlart, am ?? Buchungszeit(k), jetzt);
            db.Belegungen.Add(b);
            anzahl++;
            return b;
        }

        Buchender Tresen() => tresen[zufall.Next(tresen.Length)];
        Zahlart ZufallsZahlart() => zufall.Next(10) < 7 ? Domain.Anmeldung.Zahlart.Lastschrift : zufall.Next(2) == 0 ? Domain.Anmeldung.Zahlart.Ueberweisung : Domain.Anmeldung.Zahlart.Bar;

        foreach (var k in kurse.Where(k => k.Status != Kursstatus.Entwurf))
        {
            var max = k.MaxTeilnehmende;
            switch (k.Kursnummer)
            {
                case "26H-704":
                    PilatesAnlegen(k);
                    continue;
                case "26H-401" or "26H-402":
                    foreach (var _ in Enumerable.Range(0, max - 3 - zufall.Next(3)))
                    {
                        Buchen(k, Naechste(), false, ikTeam, Domain.Anmeldung.Zahlart.Kostentraeger, Buchungsweg.IntegrationskursTeam);
                    }

                    continue;
                case "26H-404":
                    foreach (var pid in mitarbeitende.Take(8))
                    {
                        Buchen(k, pid, false, new Buchender("Personalabteilung Stadtwerke (fiktiv)", null, null, stadtwerke!.Id, stadtwerke.Name),
                            Domain.Anmeldung.Zahlart.Kostentraeger, Buchungsweg.Auftraggeber);
                    }

                    continue;
                case "26H-502": // Spanisch A1: Mindestzahl (8) noch nicht erreicht
                    foreach (var _ in Enumerable.Range(0, 6))
                    {
                        Buchen(k, Naechste(), false, Tresen(), ZufallsZahlart());
                    }

                    continue;
                case "26H-801": // Yoga am Abend: voll, Warteliste mit Sabine Weber auf Platz 1
                    foreach (var _ in Enumerable.Range(0, max))
                    {
                        Buchen(k, Naechste(), false, Tresen(), ZufallsZahlart());
                    }

                    var wartezeit = jetzt.AddDays(-12);
                    foreach (var pid in new[] { sabine ?? Naechste(), Naechste(), Naechste() })
                    {
                        Buchen(k, pid, true, Tresen(), ZufallsZahlart(), am: wartezeit);
                        wartezeit = wartezeit.AddDays(2);
                    }

                    continue;
                case "26H-702": // PMR: eine Abmeldung, Nachrückangebot läuft
                    var belegt = Enumerable.Range(0, max).Select(_ => Buchen(k, Naechste(), false, Tresen(), ZufallsZahlart())).ToList();
                    var w1 = Buchen(k, Naechste(), true, Tresen(), ZufallsZahlart(), am: jetzt.AddDays(-20));
                    Buchen(k, Naechste(), true, Tresen(), ZufallsZahlart(), am: jetzt.AddDays(-15));
                    belegt[3].Stornieren(0, "Umzug nach Münster", Tresen().Name, jetzt.AddDays(-1));
                    w1.NachrueckenAnbieten(new DateTimeOffset(jetzt.Date.AddDays(3).AddSeconds(-1), jetzt.Offset));
                    continue;
                case "26H-701": // Rückenfit: ein Platz frei
                    foreach (var _ in Enumerable.Range(0, max - 1))
                    {
                        Buchen(k, Naechste(), false, Tresen(), ZufallsZahlart());
                    }

                    continue;
                case "26H-504": // abgesagt, Teilnehmende müssen umgebucht oder abgemeldet werden
                    foreach (var _ in Enumerable.Range(0, 5))
                    {
                        Buchen(k, Naechste(), false, Tresen(), ZufallsZahlart(), am: new DateTimeOffset(2026, 8, 20, 10, 0, 0, jetzt.Offset));
                    }

                    continue;
            }

            var ziel = k.Richtlinie.Belegung == Belegungsart.Kontingent ? 0 : Math.Max(1, zufall.Next(Math.Max(1, k.MinTeilnehmende - 1), Math.Max(k.MinTeilnehmende, max)));
            if (max > 40)
            {
                ziel = zufall.Next(15, 45); // Vorträge
            }

            if (k.Kursnummer == "26H-501" && mehmet is { } m)
            {
                Buchen(k, m, false, Tresen(), Domain.Anmeldung.Zahlart.Lastschrift);
                ziel--;
            }

            if (k.Kursnummer == "26H-403" && olena is { } o)
            {
                Buchen(k, o, false, Tresen(), Domain.Anmeldung.Zahlart.Ueberweisung, erm: olenaErm);
                ziel--;
            }

            foreach (var _ in Enumerable.Range(0, Math.Max(0, ziel)))
            {
                Buchen(k, Naechste(), false, Tresen(), ZufallsZahlart());
            }
        }

        void PilatesAnlegen(Kurs k)
        {
            var kontingente = new List<Belegung>();
            var personen = Enumerable.Range(0, 22).Select(_ => Naechste()).ToList();
            if (nadia is { } n)
            {
                personen.Add(n);
            }

            foreach (var pid in personen)
            {
                var am = new DateTimeOffset(2026, 9, 1, 10, 0, 0, jetzt.Offset).AddDays(zufall.Next(0, 60)).AddHours(zufall.Next(8));
                if (pid == nadia)
                {
                    am = jetzt.AddDays(-5);
                }

                kontingente.Add(Buchen(k, pid, false, Tresen(), ZufallsZahlart(), am: am));
            }

            var verzeichnisOrt = k.Veranstaltungen.First().Orte.First();
            const int Kapazitaet = 14;
            foreach (var v in k.Veranstaltungen.Where(v => !v.Ausgefallen).OrderBy(v => v.Beginn))
            {
                var vergangen = v.Ende < jetzt.LocalDateTime;
                var einbuchungszeit = new DateTimeOffset(v.Beginn.AddDays(-3), jetzt.Offset);
                if (!vergangen && einbuchungszeit > jetzt)
                {
                    einbuchungszeit = jetzt.AddMinutes(-30);
                }

                if (!vergangen && v.Beginn.AddHours(-k.Richtlinie.EinbuchungsfristStunden) < jetzt.LocalDateTime)
                {
                    continue;
                }

                var naechsteWoche = !vergangen && v.Beginn < jetzt.LocalDateTime.AddDays(8);
                var spaeter = !vergangen && !naechsteWoche;
                var quote = vergangen ? 0.55 : naechsteWoche ? 1.0 : 0.35;
                var belegt = 0;
                foreach (var b in kontingente.Where(b => b.GebuchtAm < einbuchungszeit && b.PersonId != nadia).OrderBy(_ => zufall.Next()))
                {
                    if (zufall.NextDouble() > quote || b.Kontingent.Verbleibend <= 0 || (b.Kontingent.GueltigBis is { } bis && v.Datum > bis))
                    {
                        continue;
                    }

                    var e = b.Einbuchen(v, verzeichnisOrt.OrtId, belegt, Kapazitaet, k.Richtlinie, einbuchungszeit, "Demodaten (Tresen)");
                    if (e.Code == Einbuchungscode.Eingebucht)
                    {
                        belegt++;
                        if (vergangen)
                        {
                            b.AnwesenheitFesthalten(v.Id, zufall.NextDouble() > 0.12, k.Richtlinie, new DateTimeOffset(v.Ende, jetzt.Offset), "Demodaten (Dozentin)");
                        }
                    }
                    else if (e.Code == Einbuchungscode.Warteliste && naechsteWoche)
                    {
                        // die nächste Veranstaltung ist voll; weitere stehen auf deren Warteliste
                    }
                }

                if (naechsteWoche && nadia is not null)
                {
                    var nb = kontingente.First(b => b.PersonId == nadia);
                    nb.Einbuchen(v, verzeichnisOrt.OrtId, belegt, Kapazitaet, k.Richtlinie, jetzt.AddMinutes(-20), "Demodaten (Tresen)");
                }

                _ = spaeter;
            }
        }

        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, "Demodaten.Anmeldung", null, null,
            $"Anmelde-Demodaten angelegt: {anzahl} Belegungen (Wartelisten, Nachrückangebot, abgesagter Kurs, Kontingente mit Einbuchungen)"));
        await db.SaveChangesAsync();
    }
}
