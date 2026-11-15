using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Sicherheit;
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>QA-44: Abwesenheit mit Vertretung in den Aufgaben.</summary>
public sealed class VertretungTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Benutzer Anna = new("anna", "Anna Buch", Rolle.Buchhaltung, [Rolle.Buchhaltung]);
    private static readonly Benutzer Bert = new("bert", "Bert Buch", Rolle.Buchhaltung, [Rolle.Buchhaltung]);
    private static readonly Benutzer Chef = new("chef", "Chris Leitung", Rolle.Leitung, [Rolle.Leitung]);

    private static async Task<T> AlsAsync<T>(ShopTestHost h, Benutzer wer, Func<AufgabenDienst, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider.GetRequiredService<AufgabenDienst>());
    }

    [Fact]
    public async Task Vertretung_sieht_Aufgaben_und_bekommt_neue_Zuweisungen()
    {
        await using var h = await StartenAsync();
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            db.Benutzerkonten.AddRange(new Benutzerkonto("anna", "Anna Buch", null, [Rolle.Buchhaltung], [], null),
                new Benutzerkonto("bert", "Bert Buch", null, [Rolle.Buchhaltung], [], null),
                new Benutzerkonto("tina", "Tina Tresen", null, [Rolle.Teilnehmerservice], [], null),
                new Benutzerkonto("chef", "Chris Leitung", null, [Rolle.Leitung], [], null));
            await db.SaveChangesAsync(Ct);
        }

        var heute = DateOnly.FromDateTime(h.Uhr.GetLocalNow().Date);
        AufgabenAnlass Anlass(string s) => new(s, $"Anliegen {s}", null, heute.AddDays(3), null);
        await AlsAsync(h, Benutzer.Automatik, async d => await d.AbgleichenAsync(Aufgabentypen.PortalBuchhaltung, [Anlass("a1"), Anlass("a2")], abbruch: Ct));
        var ids = await AlsAsync(h, Anna, async d => (await d.UebersichtAsync(Ct)).Gruppenkorb.Select(a => a.Id).ToList());
        await AlsAsync(h, Anna, async d => { await d.UebernehmenAsync(ids[0], Ct); return 0; });

        // Vertretung braucht eine gemeinsame Rolle
        (await Should.ThrowAsync<InvalidOperationException>(() => AlsAsync(h, Anna, d => d.AbwesenheitEintragenAsync(null, heute, heute.AddDays(5), "tina", "Urlaub", Ct))))
            .Message.ShouldContain("keine der Rollen");
        await AlsAsync(h, Anna, d => d.AbwesenheitEintragenAsync(null, heute, heute.AddDays(5), "bert", "Urlaub", Ct));
        (await Should.ThrowAsync<InvalidOperationException>(() => AlsAsync(h, Anna, d => d.AbwesenheitEintragenAsync(null, heute.AddDays(2), heute.AddDays(3), null, null, Ct))))
            .Message.ShouldContain("bereits");

        // Bert sieht Annas Aufgabe „In Vertretung“
        var bertSicht = await AlsAsync(h, Bert, d => d.UebersichtAsync(Ct));
        bertSicht.InVertretung!.Select(a => a.Id).ShouldBe([ids[0]]);
        (await AlsAsync(h, Anna, d => d.UebersichtAsync(Ct))).EigeneAbwesenheit!.VertretungName.ShouldBe("Bert Buch");

        // Zuweisung an Anna geht an Bert; die Auswahl zeigt die Abwesenheit
        (await AlsAsync(h, Chef, d => d.KandidatenAsync(ids[1], Ct))).Single(k => k.Login == "anna").Funktion!.ShouldContain("geht an Bert Buch");
        await AlsAsync(h, Chef, async d => { await d.ZuweisenAsync(ids[1], "anna", Ct); return 0; });
        (await AlsAsync(h, Chef, d => d.LadenAsync(ids[1], Ct))).BearbeiterKennung.ShouldBe("bert");

        // Feste Person Anna: neue Aufgaben gehen an die Vertretung
        await AlsAsync(h, Chef, async d => { await d.TypAendernAsync(Aufgabentypen.PortalBuchhaltung, Rolle.Buchhaltung, false, "anna", 7, null, null, Ct); return 0; });
        await AlsAsync(h, Benutzer.Automatik, async d => await d.AbgleichenAsync(Aufgabentypen.PortalBuchhaltung, [Anlass("a1"), Anlass("a2"), Anlass("a3")], abbruch: Ct));
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            (await bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>().Aufgaben.SingleAsync(a => a.Schluessel == "a3", Ct)).BearbeiterKennung.ShouldBe("bert");
        }

        // Wieder da: Vertretung endet
        var weg = (await AlsAsync(h, Anna, d => d.AbwesenheitenAsync(Ct))).Single();
        await AlsAsync(h, Anna, async d => { await d.AbwesenheitBeendenAsync(weg.Id, Ct); return 0; });
        (await AlsAsync(h, Bert, d => d.UebersichtAsync(Ct))).InVertretung.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ohne_Vertretung_keine_Zuweisung_an_Abwesende()
    {
        await using var h = await StartenAsync();
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            db.Benutzerkonten.AddRange(new Benutzerkonto("anna", "Anna Buch", null, [Rolle.Buchhaltung], [], null),
                new Benutzerkonto("chef", "Chris Leitung", null, [Rolle.Leitung], [], null));
            await db.SaveChangesAsync(Ct);
        }

        var heute = DateOnly.FromDateTime(h.Uhr.GetLocalNow().Date);
        await AlsAsync(h, Benutzer.Automatik, async d => await d.AbgleichenAsync(Aufgabentypen.PortalBuchhaltung, [new AufgabenAnlass("x", "X", null, heute, null)], abbruch: Ct));
        var id = (await AlsAsync(h, Anna, d => d.UebersichtAsync(Ct))).Gruppenkorb.Single().Id;
        await AlsAsync(h, Anna, d => d.AbwesenheitEintragenAsync(null, heute, heute.AddDays(2), null, null, Ct));
        (await Should.ThrowAsync<InvalidOperationException>(() => AlsAsync(h, Chef, async d => { await d.ZuweisenAsync(id, "anna", Ct); return 0; })))
            .Message.ShouldContain("keine passende Vertretung");
        (await Should.ThrowAsync<InvalidOperationException>(() => AlsAsync(h, Anna, d => d.AbwesenheitEintragenAsync("chef", heute, heute, null, null, Ct))))
            .Message.ShouldContain("Leitung");
    }
}
