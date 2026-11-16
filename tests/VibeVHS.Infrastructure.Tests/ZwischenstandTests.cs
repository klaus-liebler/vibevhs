using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Täglicher Fortschreib-Job: Änderungen mit Datum in der Zukunft (Rolle endet, Umzug ab, Name ab) erreichen die Lese-Caches der
/// Personentabelle an ihrem Tag – auch nach einer Pause des Servers.
/// </summary>
public sealed class ZwischenstandTests
{
    private sealed class Automatik : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(Benutzer.Automatik);
    }

    private static Zwischenstaende Dienst(TestDatenbank t, VibeVhsDbContext db)
        => new(new ZwischenstandSpeicher(db, t.Uhr), new AuditProtokoll(db, new Automatik(), t.Uhr), t.Uhr);

    [Fact]
    public async Task Zukuenftige_Aenderungen_werden_an_ihrem_Tag_in_die_Caches_uebernommen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        var jetzt = t.Uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var p = Person.Anlegen("Rita", "Rolle", heute.AddYears(-2), jetzt.AddYears(-2));
        p.RolleHinzufuegen(Personenrolle.Teilnehmende | Personenrolle.Dozierende, heute.AddYears(-2), Erfassung.Erst(jetzt.AddYears(-2)));
        var adresse = p.AdresseHinzufuegen(null, "Altweg 1", "48282", "Emsdetten", heute.AddYears(-2), Erfassung.Erst(jetzt.AddYears(-2)));
        p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, adresse, heute.AddYears(-2), Erfassung.Erst(jetzt.AddYears(-2)));
        // Heute erfasst, wirksam in drei Tagen:
        p.RolleBeendenAb(Personenrolle.Dozierende, heute.AddDays(3), Erfassung.Aenderung(jetzt, "Vertrag endet"));
        p.UmzugAb(adresse, null, "Neuweg 2", "48268", "Greven", heute.AddDays(3), Erfassung.Aenderung(jetzt, "Umzug"));
        p.NameAendernAb("Rita", "Neumann", heute.AddDays(3), "Heirat", jetzt);
        await using (var db = t.NeuerKontext())
        {
            db.Personen.Add(p);
            await db.SaveChangesAsync(ct);
        }

        async Task<(Personenrolle Rollen, string? Wohnort, string Anzeigename)> CacheAsync()
        {
            await using var db = t.NeuerKontext();
            var x = await db.Personen.AsNoTracking().IgnoreAutoIncludes().SingleAsync(y => y.Id == p.Id, ct);
            return (x.Rollen, x.Wohnort, x.Anzeigename);
        }

        (await CacheAsync()).ShouldBe((Personenrolle.Teilnehmende | Personenrolle.Dozierende, "Emsdetten", "Rita Rolle"));

        await using (var db = t.NeuerKontext())
        {
            (await Dienst(t, db).FortschreibenAsync(ct)).Geaendert.ShouldBe(0); // heute noch nichts wirksam
        }

        // Server war zwei Tage aus: am vierten Tag holt der Lauf die verpassten Grenzen nach.
        t.Uhr.Advance(TimeSpan.FromDays(4));
        await using (var db = t.NeuerKontext())
        {
            var lauf = await Dienst(t, db).FortschreibenAsync(ct);
            (lauf.Geprueft, lauf.Geaendert).ShouldBe((1, 1));
        }

        (await CacheAsync()).ShouldBe((Personenrolle.Teilnehmende, "Greven", "Rita Neumann"));

        await using (var db = t.NeuerKontext())
        {
            (await Dienst(t, db).FortschreibenAsync(ct)).Geprueft.ShouldBe(0); // am selben Tag nur einmal
            (await db.Protokoll.CountAsync(e => e.Aktion == Zwischenstaende.Aktion, ct)).ShouldBe(2);
        }
    }
}
