using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using VibeVHS.Application.Dokumente;
using VibeVHS.Infrastructure.Dokumente;
using VibeVHS.Infrastructure.Durchfuehrung;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// PDFs nutzen nur die eingebetteten Liberation-Schriften – keine Systemschriften. Unter Linux ohne Windows-Schriften brach
/// MigraDoc sonst ab („No appropriate font found for family name 'Courier New'“, Fehlerschrift beim Rendern).
/// </summary>
public sealed class SchriftenTests
{
    [Theory]
    [InlineData("Arial", "LiberationSans")]
    [InlineData("Helvetica", "LiberationSans")]
    [InlineData("Segoe UI", "LiberationSans")]
    [InlineData("Courier New", "LiberationMono")]
    [InlineData("Consolas", "LiberationMono")]
    [InlineData("Times New Roman", "LiberationSerif")]
    [InlineData("Liberation Serif", "LiberationSerif")]
    [InlineData("Liberation Sans", "LiberationSans")]
    [InlineData("Unbekannt", "LiberationSans")]
    public void Bekannte_Namen_werden_abgebildet(string name, string familie)
        => Schriften.EingebetteteSchriften.Familie(name).ShouldBe(familie);

    [Fact]
    public void Alle_zwoelf_Schnitte_sind_eingebettet()
    {
        var aufloeser = new Schriften.EingebetteteSchriften();
        foreach (var name in new[] { "Arial", "Times New Roman", "Courier New" })
        foreach (var (fett, kursiv) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var info = aufloeser.ResolveTypeface(name, fett, kursiv);
            info.MustSimulateBold.ShouldBeFalse();
            info.MustSimulateItalic.ShouldBeFalse();
            var ttf = aufloeser.GetFont(info.FaceName);
            ttf.Length.ShouldBeGreaterThan(100_000);
            BitConverter.ToUInt32([ttf[3], ttf[2], ttf[1], ttf[0]]).ShouldBe(0x00010000u); // TrueType-Kennung
        }
    }

    [Fact]
    public void Brief_PDF_bettet_Liberation_ein_und_nutzt_keine_Systemschrift()
    {
        var pdf = new BriefPdf(new QrCodeErzeuger()).Erzeugen(new Brief(
            "Volkshochschule Test", "Bildungsweg 1 · 48282 Emsdetten", ["Jan Kowalski", "Hauptstraße 1", "48282 Emsdetten"], "Emsdetten, 16.11.2026",
            "Rechnung R-2026-00001", ["Bitte überweisen Sie den Betrag."], [("Betrag", "49,00 €")], null, null, "Ihre vhs", null,
            new Uri("https://vhs.example/brief/abc"), null, null));

        GlobalFontSettings.FontResolver.ShouldBeOfType<Schriften.EingebetteteSchriften>();
        using var dok = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var schriften = dok.Pages.Cast<PdfPage>()
            .SelectMany(s => s.Resources.Elements.GetDictionary("/Font")?.Elements.Values ?? [])
            .Select(v => (v as PdfReference)?.Value as PdfDictionary ?? v as PdfDictionary)
            .Select(d => d?.Elements.GetName("/BaseFont") ?? "")
            .ToList();
        schriften.ShouldNotBeEmpty();
        schriften.ShouldAllBe(n => n.Contains("Liberation"));
    }
}
