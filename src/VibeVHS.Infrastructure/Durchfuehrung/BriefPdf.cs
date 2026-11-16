using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using VibeVHS.Application.Dokumente;

namespace VibeVHS.Infrastructure.Durchfuehrung;

/// <summary>Briefe als PDF (MigraDoc) mit QR-Code „Translate this letter in your language“ in der Fußzeile jeder Seite.</summary>
internal sealed class BriefPdf(IQrCode qr, VibeVHS.Application.Stammdaten.Briefkopf? briefkopf = null) : IBriefPdf
{
    static BriefPdf()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }

    public byte[] Erzeugen(Brief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        var dok = new Document();
        dok.Info.Title = brief.Titel;
        dok.Info.Author = brief.Absender;
        var stil = dok.Styles[StyleNames.Normal]!;
        stil.Font.Name = "Arial";
        stil.Font.Size = 10.5;
        var s = dok.AddSection();
        s.PageSetup.PageFormat = PageFormat.A4;
        s.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        s.PageSetup.LeftMargin = Unit.FromCentimeter(2.5);
        s.PageSetup.RightMargin = Unit.FromCentimeter(2);
        s.PageSetup.BottomMargin = Unit.FromCentimeter(4);
        s.PageSetup.FooterDistance = Unit.FromCentimeter(0.8);

        // Fußzeile: QR-Code zur Übersetzungsseite
        var fuss = s.Footers.Primary.AddTable();
        fuss.AddColumn(Unit.FromCentimeter(2.6));
        fuss.AddColumn(Unit.FromCentimeter(14));
        var fz = fuss.AddRow();
        var bild = fz.Cells[0].AddImage("base64:" + Convert.ToBase64String(qr.Png(brief.Uebersetzungslink.ToString())));
        bild.Width = Unit.FromCentimeter(2.3);
        bild.LockAspectRatio = true;
        var ft = fz.Cells[1].AddParagraph();
        ft.AddFormattedText("Translate this letter in your language", TextFormat.Bold);
        ft.AddLineBreak();
        ft.AddText("Diesen Brief in Ihrer Sprache lesen · Bu mektubu kendi dilinizde okuyun · Прочитати лист своєю мовою · Przeczytaj ten list w swoim języku");
        ft.AddLineBreak();
        ft.AddFormattedText(brief.Uebersetzungslink.ToString(), new Font { Size = 7, Color = Colors.Gray });
        ft.Format.Font.Size = 8;
        fz.Cells[1].VerticalAlignment = VerticalAlignment.Center;

        // Kopf: Absender links, Logo der Einrichtung rechts (Einstellungen → Einrichtung)
        var kt = s.AddTable();
        kt.AddColumn(Unit.FromCentimeter(10));
        kt.AddColumn(Unit.FromCentimeter(6.5));
        var kz = kt.AddRow();
        kz.BottomPadding = Unit.FromCentimeter(1.0);
        var kopf = kz.Cells[0].AddParagraph();
        kopf.AddFormattedText(brief.Absender, new Font { Size = 13, Bold = true });
        kopf.AddLineBreak();
        kopf.AddFormattedText(brief.AbsenderZusatz, new Font { Size = 9, Color = Colors.Gray });
        if (briefkopf?.Logo is { } logo)
        {
            var la = kz.Cells[1].AddParagraph();
            la.Format.Alignment = ParagraphAlignment.Right;
            var lb = la.AddImage("base64:" + Convert.ToBase64String(logo));
            lb.Height = Unit.FromCentimeter(1.1);
            lb.LockAspectRatio = true;
        }

        // Anschrift und Datum
        var ad = s.AddTable();
        ad.AddColumn(Unit.FromCentimeter(10));
        ad.AddColumn(Unit.FromCentimeter(6.5));
        var az = ad.AddRow();
        var absenderzeile = az.Cells[0].AddParagraph($"{brief.Absender}");
        absenderzeile.Format.Font.Size = 7;
        absenderzeile.Format.Font.Color = Colors.Gray;
        foreach (var zeile in brief.Anschrift)
        {
            az.Cells[0].AddParagraph(zeile);
        }

        var datum = az.Cells[1].AddParagraph(brief.OrtDatum);
        datum.Format.Alignment = ParagraphAlignment.Right;
        s.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(1);

        var titel = s.AddParagraph(brief.Titel);
        titel.Format.Font.Size = 15;
        titel.Format.Font.Bold = true;
        titel.Format.SpaceAfter = Unit.FromPoint(10);

        foreach (var a in brief.Absaetze)
        {
            var p = s.AddParagraph(a);
            p.Format.SpaceAfter = Unit.FromPoint(8);
            p.Format.Alignment = ParagraphAlignment.Justify;
        }

        if (brief.Angaben.Count > 0)
        {
            var t = s.AddTable();
            t.AddColumn(Unit.FromCentimeter(4));
            t.AddColumn(Unit.FromCentimeter(12.5));
            foreach (var (feld, wert) in brief.Angaben)
            {
                var r = t.AddRow();
                r.Cells[0].AddParagraph(feld).Format.Font.Color = Colors.Gray;
                r.Cells[1].AddParagraph(wert);
                r.TopPadding = Unit.FromPoint(2);
            }

            s.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
        }

        if (brief.TabellenKopf is { Count: > 0 } kopfzeile && brief.Tabelle is { } zeilen)
        {
            var t = s.AddTable();
            t.Borders.Width = 0.25;
            t.Borders.Color = Colors.LightGray;
            var breite = 16.5 / kopfzeile.Count;
            foreach (var _ in kopfzeile)
            {
                t.AddColumn(Unit.FromCentimeter(breite));
            }

            var k = t.AddRow();
            k.HeadingFormat = true;
            k.Shading.Color = Colors.WhiteSmoke;
            for (var i = 0; i < kopfzeile.Count; i++)
            {
                k.Cells[i].AddParagraph(kopfzeile[i]).Format.Font.Bold = true;
            }

            foreach (var z in zeilen)
            {
                var r = t.AddRow();
                for (var i = 0; i < Math.Min(z.Count, kopfzeile.Count); i++)
                {
                    r.Cells[i].AddParagraph(z[i]);
                }
            }

            s.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
        }

        if (brief.PersoenlicherQrInhalt is { } inhalt)
        {
            var b = s.AddImage("base64:" + Convert.ToBase64String(qr.Png(inhalt)));
            b.Width = Unit.FromCentimeter(4.5);
            b.LockAspectRatio = true;
            var c = s.AddParagraph(brief.PersoenlicherQrText ?? string.Empty);
            c.Format.Font.Size = 14;
            c.Format.Font.Bold = true;
            c.Format.SpaceAfter = Unit.FromPoint(10);
        }

        if (brief.Unterschrift is { } u)
        {
            var linie = s.AddParagraph("______________________________");
            linie.Format.SpaceBefore = Unit.FromCentimeter(1.5);
            s.AddParagraph(u).Format.Font.Size = 9;
        }

        if (brief.Hinweis is { } h)
        {
            var p = s.AddParagraph(h);
            p.Format.Font.Size = 8;
            p.Format.Font.Color = Colors.Gray;
            p.Format.SpaceBefore = Unit.FromPoint(12);
        }

        var renderer = new PdfDocumentRenderer { Document = dok };
        renderer.RenderDocument();
        using var strom = new MemoryStream();
        renderer.PdfDocument.Save(strom, false);
        return strom.ToArray();
    }
}
