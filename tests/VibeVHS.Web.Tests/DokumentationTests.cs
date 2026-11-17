using VibeVHS.Application.Sicherheit;
using VibeVHS.Web.Dokumentation;

namespace VibeVHS.Web.Tests;

/// <summary>Markdown-Darstellung (Hilfe, Spezifikation, Impressum) und Dokumentkatalog.</summary>
public class DokumentationTests
{
    [Fact]
    public void Html_im_Markdown_wird_nicht_uebernommen()
    {
        var html = MarkdownDarstellung.AlsHtml("Hallo <script>alert(1)</script> <img src=x onerror=alert(1)>");

        html.ShouldNotContain("<script");
        html.ShouldNotContain("<img");
        html.ShouldContain("&lt;script&gt;");
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JavaScript:alert(1))")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[x](vbscript:msgbox)")]
    public void Gefaehrliche_Links_werden_entfernt(string markdown)
    {
        var html = MarkdownDarstellung.AlsHtml(markdown);

        html.ShouldNotContain("script:", Case.Insensitive);
        html.ShouldNotContain("data:", Case.Insensitive);
        html.ShouldContain(">x</a>");
    }

    [Theory]
    [InlineData("[x](https://www.vhs-egs.de)", "href=\"https://www.vhs-egs.de\"")]
    [InlineData("[x](mailto:info@vhs-egs.de)", "href=\"mailto:info@vhs-egs.de\"")]
    [InlineData("[x](#bedienung)", "href=\"#bedienung\"")]
    public void Erlaubte_Links_bleiben(string markdown, string erwartet)
        => MarkdownDarstellung.AlsHtml(markdown).ShouldContain(erwartet);

    [Fact]
    public void Abschnitte_mit_eindeutigen_Ankern_und_reinem_Titel()
    {
        var md = "Vorspann\n\n# Titel\n\nText\n\n## Bedienung `QA`\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n### Unterpunkt\n\n#### tiefer bleibt im Abschnitt\n\n## Bedienung `QA`\n";

        var a = MarkdownDarstellung.InAbschnitte(md);

        a.Select(x => (x.Ebene, x.Id)).ShouldBe([(0, ""), (1, "titel"), (2, "bedienung-qa"), (3, "unterpunkt"), (2, "bedienung-qa-1")]);
        a[2].Titel.ShouldBe("Bedienung QA");
        a[2].TitelHtml.ShouldContain("<code>QA</code>");
        a[2].InhaltHtml.ShouldContain("<table>");
        a[3].InhaltHtml.ShouldContain("<h4");
    }

    [Fact]
    public void Relative_Links_zeigen_auf_Seiten_der_Anwendung_oder_entfallen()
    {
        var anforderungen = Dokumentkatalog.Finden("spezifikation/anforderungen")!;

        Dokumentkatalog.LinkZiel(anforderungen, "architektur.md#datenhaltung").ShouldBe("dokumentation/spezifikation/architektur#datenhaltung");
        Dokumentkatalog.LinkZiel(anforderungen, "../betrieb.md").ShouldBe("dokumentation/doku/betrieb");
        Dokumentkatalog.LinkZiel(anforderungen, "../hilfe.md#aufbau").ShouldBe("hilfe#aufbau");
        Dokumentkatalog.LinkZiel(anforderungen, "#bedienung").ShouldBe("dokumentation/spezifikation/anforderungen#bedienung");
        Dokumentkatalog.LinkZiel(anforderungen, "../../src/geheim.cs").ShouldBeNull();
        Dokumentkatalog.LinkZiel(anforderungen, "../../../etc/passwd").ShouldBeNull();
    }

    [Fact]
    public void Jedes_Dokument_des_Katalogs_wird_mit_ausgeliefert()
    {
        var katalog = new Dokumentkatalog();
        foreach (var d in Dokumentkatalog.Alle)
        {
            var abschnitte = katalog.Laden(d, Rolle.Leitung);
            abschnitte.ShouldNotBeNull($"{d.Datei} fehlt unter Dokumentation/ (csproj: docs/**/*.md)");
            abschnitte.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void Hilfe_fuer_alle_Spezifikation_nur_fuer_Leitung_und_Systemverwaltung()
    {
        var katalog = new Dokumentkatalog();

        katalog.Laden(Dokumentkatalog.Finden(Dokumentkatalog.Hilfe)!, Rolle.Teilnehmerservice).ShouldNotBeNull();
        Should.Throw<InvalidOperationException>(() => katalog.Laden(Dokumentkatalog.Finden("spezifikation/anforderungen")!, Rolle.Teilnehmerservice));
        Should.Throw<InvalidOperationException>(() => katalog.Laden(Dokumentkatalog.Finden("doku/sicherheit")!, Rolle.Buchhaltung));
        katalog.Laden(Dokumentkatalog.Finden("doku/sicherheit")!, Rolle.Systemverwaltung).ShouldNotBeNull();
    }
}
