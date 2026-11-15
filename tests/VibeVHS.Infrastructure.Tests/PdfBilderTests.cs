using System.Text;
using System.Text.RegularExpressions;
using VibeVHS.Application.Dokumente;
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

    [Fact]
    public void Qr_PNG_ist_RGB_und_damit_fuer_PDFsharp_lesbar()
    {
        var png = new QrCodeErzeuger().Png("https://vhs.example/k/26H-101");
        png[..4].ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        png[25].ShouldBeOneOf((byte)2, (byte)3); // Farbtyp RGB oder Palette, nicht Graustufen (0)
    }
}
