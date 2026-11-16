using System.Text;
using System.Text.RegularExpressions;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Shop;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Infrastructure.Durchfuehrung;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// QR-Codes in erzeugten PDFs sind echte, eingebettete Bilder. Ein PNG, das PDFsharp/MigraDoc nicht lesen kann, ergäbe
/// stattdessen einen grauen Kasten mit „Image has no valid type.“ – ohne Ausnahme beim Erzeugen.
/// </summary>
public sealed class PdfBilderTests
{
    private static int Bilder(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Subtype\s*/Image").Count;

    private static Brief Brief(string? persoenlicherQr) => new(
        "Volkshochschule Test", "Bildungsweg 1 · 48282 Emsdetten", ["Jan Kowalski", "Hauptstraße 1", "48282 Emsdetten"], "Emsdetten, 15.11.2026",
        "Teilnahmebescheinigung", ["Sie haben am Kurs teilgenommen."], [("Kurs", "26H-501 Englisch A2")], null, null, "Ihre vhs", null,
        new Uri("https://vhs.example/brief/abc"), persoenlicherQr, persoenlicherQr is null ? null : "Ihr Ausweis");

    [Fact]
    public void Brief_bettet_den_Uebersetzungs_QR_als_Bild_ein()
        => Bilder(new BriefPdf(new QrCodeErzeuger()).Erzeugen(Brief(null))).ShouldBe(1);

    [Fact]
    public void Brief_mit_Ausweis_QR_bettet_beide_Bilder_ein()
        => Bilder(new BriefPdf(new QrCodeErzeuger()).Erzeugen(Brief("VHSA1.test-token"))).ShouldBe(2);

    [Fact]
    public void Kurs_QR_Bogen_bettet_je_Kurs_ein_Bild_ein()
        => Bilder(new KursQrBogen(new QrCodeErzeuger()).Erzeugen("Kurs-QR-Codes",
            [new("26H-101", "Garten", null, "https://vhs.example/k/26H-101"), new("26H-102", "Vögel", "ab 21.11.2026", "https://vhs.example/k/26H-102"),
             new("26H-103", "Repair-Café", null, "https://vhs.example/k/26H-103"), new("26H-104", "Kochen", null, "https://vhs.example/k/26H-104")])).ShouldBe(4);

    private static Briefkopf MitLogo()
    {
        var e = new VibeVHS.Domain.Stammdaten.Einrichtung("Volkshochschule Test", "vhs Test", ["Emsdetten"], "NRW");
        e.LogoSetzen(new QrCodeErzeuger().Png("Logo")); // irgendein gültiges PNG
        e.WebsiteSetzen("www.vhs.example");
        var k = new Briefkopf();
        k.Uebernehmen(e);
        return k;
    }

    [Fact]
    public void Kurs_QR_Bogen_mit_Logo_Fachbereichen_und_Schneidemarken()
    {
        var kurse = Enumerable.Range(1, 8).Select(i => new KursQrEintrag($"26H-{(i < 5 ? 1 : 5)}0{i}", $"Kurs {i}", "ab 01.12.2026, 18:00 Uhr · A 0.01",
            $"https://vhs.example/k/{i}", i < 5 ? 1 : 5, i % 2 == 0 ? "84,00 € · ermäßigt 63,00 €" : null)).ToList();
        var optionen = new KursQrBogenOptionen("Direkt anmelden per QR-Code", new Dictionary<int, string> { [1] = "Umwelt", [5] = "Fremdsprachen" });
        var pdf = new KursQrBogen(new QrCodeErzeuger(), MitLogo()).Erzeugen("Kursprogramm Herbst 2026", kurse, optionen);
        Bilder(pdf).ShouldBeGreaterThanOrEqualTo(9); // 8 QR-Codes + Logo im Kopf
        new KursQrBogen(new QrCodeErzeuger(), MitLogo()).Erzeugen("Aushang", kurse, optionen with { Schneidemarken = true }).Length.ShouldBeGreaterThan(2000);
    }

    [Fact]
    public void Brief_zeigt_das_Logo_der_Einrichtung()
        => Bilder(new BriefPdf(new QrCodeErzeuger(), MitLogo()).Erzeugen(Brief(null))).ShouldBe(2);

    [Fact]
    public void Logo_nur_als_PNG_oder_JPEG_und_nicht_zu_gross()
    {
        var e = new VibeVHS.Domain.Stammdaten.Einrichtung("Volkshochschule Test", "vhs Test", [], "NRW");
        Should.Throw<ArgumentException>(() => e.LogoSetzen(Encoding.UTF8.GetBytes("<svg></svg>"))).Message.ShouldContain("PNG");
        var gross = new byte[VibeVHS.Domain.Stammdaten.Einrichtung.LogoMaxBytes + 1];
        gross[0] = 0x89; gross[1] = 0x50; gross[2] = 0x4E; gross[3] = 0x47;
        Should.Throw<ArgumentException>(() => e.LogoSetzen(gross)).Message.ShouldContain("zu groß");
        e.LogoSetzen(null);
        e.Logo.ShouldBeNull();
    }

    [Fact]
    public void Qr_PNG_ist_RGB_und_damit_fuer_PDFsharp_lesbar()
    {
        var png = new QrCodeErzeuger().Png("https://vhs.example/k/26H-101");
        png[..4].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        png[25].ShouldBeOneOf((byte)2, (byte)3); // Farbtyp RGB oder Palette, nicht Graustufen (0)
    }
}
