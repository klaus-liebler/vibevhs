using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Vorlagen;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Rundschreiben mit Editor, Gestaltung, Anreden und Archiv (QA-66).</summary>
public sealed class RundschreibenTests
{
    private static readonly Benutzer Tresen = new("katja.friedsen", "Katja Friedsen", Rolle.Teilnehmerservice);

    private sealed class TestBenutzer(Benutzer b) : IAktuellerBenutzer
    {
        public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(b);
    }

    private static Rundschreiben Dienst(TestDatenbank t, VibeVhsDbContext db)
    {
        var b = new TestBenutzer(Tresen);
        var protokoll = new AuditProtokoll(db, b, t.Uhr);
        var post = new PostausgangDienst(new NachrichtenSpeicher(db), protokoll, b, new Uebersetzungslinks(new Uri("https://vhs.example/")), t.Uhr);
        var kopf = new Application.Stammdaten.Briefkopf();
        return new Rundschreiben(new PersonenAbfrage(db, new DublettenSuche(db), t.Uhr), post, new NachrichtenSpeicher(db), protokoll, t.Uhr, kopf, b);
    }

    [Fact]
    public async Task Formatierter_Text_wird_bereinigt_persoenlich_angesprochen_gestaltet_und_archiviert()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using (var db = t.NeuerKontext())
        {
            db.Personen.Add(Person.Anlegen("Erika", "Muster", new DateOnly(2026, 1, 1), t.Uhr.GetLocalNow(), null, "erika@example.org"));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = t.NeuerKontext())
        {
            var html = "<p>{Anrede},</p><p><b>neues Programm</b> <script>alert(1)</script><a href=\"javascript:alert(1)\">hier</a> und <a href=\"https://vhs.example\">Website</a></p>";
            var e = await Dienst(t, db).SendenGestaltetAsync(new PersonenFilter("Muster"), Kontaktzweck.Kursorganisation, "Hallo {Vorname}", html,
                Gestaltung.Winter, Anredestil.Informell, "Suche „Muster“", ct);
            e.Eingereiht.ShouldBe(1);
        }

        await using (var db2 = t.NeuerKontext())
        {
            var mail = await db2.Postausgang.SingleAsync(ct);
            mail.Betreff.ShouldBe("Hallo Erika");
            mail.InhaltHtml.ShouldNotBeNull();
            mail.InhaltHtml.ShouldContain("Guten Tag Erika Muster");          // ohne gepflegte Anrede: neutrale Ausweich-Anrede
            mail.InhaltHtml.ShouldContain("<b>neues Programm</b>");
            mail.InhaltHtml.ShouldNotContain("<script");
            mail.InhaltHtml.ShouldNotContain("javascript:");
            mail.InhaltHtml.ShouldContain("href=\"https://vhs.example\"");
            mail.InhaltHtml.ShouldContain("border-top: 6px solid #1f4e79");     // Gestaltung Winter
            mail.Inhalt.ShouldContain("neues Programm");                        // Textteil ohne HTML
            mail.Inhalt.ShouldNotContain("<b>");

            var archiv = await Dienst(t, db2).ArchivAsync(abbruch: ct);
            archiv.Single().Html.ShouldContain("{Anrede}");                    // Platzhalter bleiben fürs Wiederverwenden
            archiv.Single().Zielgruppe.ShouldBe("Suche „Muster“");
            archiv.Single().GesendetVon.ShouldContain("Katja Friedsen");
        }
    }

    [Fact]
    public void Bereinigung_laesst_nur_erlaubte_Elemente_Links_und_Stile_zu()
    {
        HtmlBereinigung.Bereinigen("<p onclick=\"x()\" style=\"color: red; position: fixed\">Hallo</p>").ShouldBe("<p style=\"color: red\">Hallo</p>");
        HtmlBereinigung.Bereinigen("<img src=x onerror=alert(1)><iframe src=\"https://x\"></iframe>ok").ShouldBe("ok");
        HtmlBereinigung.Bereinigen("<a href=\"mailto:info@vhs.example\">Mail</a>").ShouldContain("href=\"mailto:info@vhs.example\"");
        HtmlBereinigung.AlsText("<ul><li>eins</li><li>zwei</li></ul>").ShouldBe("• eins\n• zwei");
    }
}
