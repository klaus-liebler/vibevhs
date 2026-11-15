using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Shop;

namespace VibeVHS.Infrastructure.Durchfuehrung;

/// <summary>Sammel-PDF der Kurs-QR-Codes für das Programmheft: drei Spalten je Zeile, Kursnummer, Titel, Beginn unter jedem Code.</summary>
internal sealed class KursQrBogen(IQrCode qr) : IKursQrBogen
{
    static KursQrBogen()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }

    public byte[] Erzeugen(string ueberschrift, IReadOnlyList<KursQrEintrag> kurse)
    {
        ArgumentNullException.ThrowIfNull(kurse);
        var dok = new Document();
        dok.Info.Title = ueberschrift;
        var stil = dok.Styles[StyleNames.Normal]!;
        stil.Font.Name = "Arial";
        stil.Font.Size = 9;
        var s = dok.AddSection();
        s.PageSetup.PageFormat = PageFormat.A4;
        s.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        s.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);
        s.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        s.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        var kopf = s.AddParagraph(ueberschrift);
        kopf.Format.Font.Size = 14;
        kopf.Format.Font.Bold = true;
        kopf.Format.SpaceAfter = Unit.FromPoint(4);
        var hinweis = s.AddParagraph("QR-Code scannen: Kursbeschreibung mit Online-Anmeldung. Mit gemerktem Gerät oder persönlichem Link: Anmeldung mit einem Klick.");
        hinweis.Format.Font.Color = Colors.Gray;
        hinweis.Format.SpaceAfter = Unit.FromPoint(10);
        if (kurse.Count == 0)
        {
            s.AddParagraph("Keine öffentlichen Kurse für diese Auswahl.");
        }

        var t = s.AddTable();
        for (var i = 0; i < 3; i++)
        {
            t.AddColumn(Unit.FromCentimeter(6));
        }

        for (var z = 0; z < kurse.Count; z += 3)
        {
            var r = t.AddRow();
            r.KeepWith = 0;
            r.TopPadding = Unit.FromPoint(6);
            r.BottomPadding = Unit.FromPoint(10);
            for (var i = 0; i < 3 && z + i < kurse.Count; i++)
            {
                var k = kurse[z + i];
                var c = r.Cells[i];
                c.Format.Alignment = ParagraphAlignment.Center;
                var bild = c.AddImage("base64:" + Convert.ToBase64String(qr.Png(k.Inhalt)));
                bild.Width = Unit.FromCentimeter(3.4);
                bild.LockAspectRatio = true;
                var n = c.AddParagraph(k.Nummer);
                n.Format.Font.Bold = true;
                n.Format.Font.Size = 11;
                c.AddParagraph(k.Titel);
                if (k.Zusatz is { } zusatz)
                {
                    c.AddParagraph(zusatz).Format.Font.Color = Colors.DimGray;
                }
            }
        }

        var renderer = new PdfDocumentRenderer { Document = dok };
        renderer.RenderDocument();
        using var strom = new MemoryStream();
        renderer.PdfDocument.Save(strom, false);
        return strom.ToArray();
    }
}
