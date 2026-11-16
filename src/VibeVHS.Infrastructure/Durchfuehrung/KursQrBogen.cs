using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Shop;
using VibeVHS.Application.Stammdaten;

namespace VibeVHS.Infrastructure.Durchfuehrung;

/// <summary>
/// Sammel-PDF der Kurs-QR-Codes für Programmheft und Aushang: Kopf mit Logo und Titel, Scan-Anleitung, Kurse je Fachbereich
/// als Karten im Raster (3 je Zeile, 4 Zeilen je A4-Seite), QR-Code zentriert, Kursnummer groß, Titel, Termin/Ort, Entgelt;
/// Fußzeile mit Einrichtung, Website und „Seite x von y“. Mit Schneidemarken: gestrichelte Kartenränder zum Ausschneiden.
/// </summary>
internal sealed class KursQrBogen(IQrCode qr, Briefkopf? briefkopf = null) : IKursQrBogen
{
    // Farben aus dem App-Theme (app.css: --vhs-accent, --vhs-accent-weak, --vhs-line, --vhs-fg, --vhs-muted)
    private static readonly Color Akzent = new(13, 104, 112);
    private static readonly Color AkzentHell = new(214, 236, 238);
    private static readonly Color Linie = new(201, 211, 213);
    private static readonly Color Text = new(23, 38, 41);
    private static readonly Color Gedaempft = new(87, 105, 109);

    private const double KarteBreite = 5.6;
    private const double Abstand = 0.6;
    private const double KarteHoehe = 5.3;

    static KursQrBogen() => VibeVHS.Infrastructure.Dokumente.Schriften.Einrichten();

    public byte[] Erzeugen(string ueberschrift, IReadOnlyList<KursQrEintrag> kurse, KursQrBogenOptionen? optionen = null)
    {
        ArgumentNullException.ThrowIfNull(kurse);
        optionen ??= new KursQrBogenOptionen();
        var dok = new Document();
        dok.Info.Title = ueberschrift;
        dok.Info.Author = briefkopf?.Name ?? "Volkshochschule";
        var stil = dok.Styles[StyleNames.Normal]!;
        stil.Font.Name = "Arial";
        stil.Font.Size = 9;
        stil.Font.Color = Text;

        var s = dok.AddSection();
        s.PageSetup.PageFormat = PageFormat.A4;
        s.PageSetup.TopMargin = Unit.FromCentimeter(3.3);
        s.PageSetup.HeaderDistance = Unit.FromCentimeter(1.0);
        s.PageSetup.BottomMargin = Unit.FromCentimeter(2.0);
        s.PageSetup.FooterDistance = Unit.FromCentimeter(0.9);
        s.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        s.PageSetup.RightMargin = Unit.FromCentimeter(1.5);

        Kopf(s, ueberschrift, optionen.Untertitel);
        Fuss(s);
        Anleitung(s);

        if (kurse.Count == 0)
        {
            s.AddParagraph("Keine öffentlichen Kurse für diese Auswahl.").Format.Font.Color = Gedaempft;
        }

        // Je Fachbereich eine Zwischenüberschrift und ein Kartenraster.
        foreach (var gruppe in kurse.GroupBy(k => k.Fachbereich).OrderBy(g => g.Key ?? int.MaxValue))
        {
            if (gruppe.Key is { } fb)
            {
                var name = optionen.Fachbereiche?.GetValueOrDefault(fb);
                var z = s.AddParagraph(name is null ? $"Fachbereich {fb}" : $"Fachbereich {fb} · {name}");
                z.Format.Font.Bold = true;
                z.Format.Font.Size = 10.5;
                z.Format.Font.Color = Akzent;
                z.Format.Shading.Color = AkzentHell;
                z.Format.Borders.Left.Width = Unit.FromPoint(3);
                z.Format.Borders.Left.Color = Akzent;
                z.Format.Borders.DistanceFromLeft = Unit.FromPoint(6);
                z.Format.Borders.DistanceFromTop = Unit.FromPoint(3);
                z.Format.Borders.DistanceFromBottom = Unit.FromPoint(3);
                z.Format.SpaceBefore = Unit.FromPoint(8);
                z.Format.SpaceAfter = Unit.FromPoint(6);
                z.Format.KeepWithNext = true;
            }

            Raster(s, gruppe.ToList(), optionen.Schneidemarken);
        }

        var renderer = new PdfDocumentRenderer { Document = dok };
        renderer.RenderDocument();
        using var strom = new MemoryStream();
        renderer.PdfDocument.Save(strom, false);
        return strom.ToArray();
    }

    private void Kopf(Section s, string ueberschrift, string? untertitel)
    {
        var t = s.Headers.Primary.AddTable();
        t.AddColumn(Unit.FromCentimeter(7));
        t.AddColumn(Unit.FromCentimeter(11));
        var r = t.AddRow();
        r.VerticalAlignment = VerticalAlignment.Bottom;
        r.Borders.Bottom.Width = Unit.FromPoint(1.5);
        r.Borders.Bottom.Color = Akzent;
        r.BottomPadding = Unit.FromPoint(6);
        if (briefkopf?.Logo is { } logo)
        {
            var bild = r.Cells[0].AddParagraph().AddImage("base64:" + Convert.ToBase64String(logo));
            bild.Height = Unit.FromCentimeter(1.1);
            bild.LockAspectRatio = true;
        }
        else
        {
            r.Cells[0].AddParagraph(briefkopf?.Name ?? "Volkshochschule").Format.Font.Bold = true;
        }

        var titel = r.Cells[1].AddParagraph(ueberschrift);
        titel.Format.Alignment = ParagraphAlignment.Right;
        titel.Format.Font.Size = 14;
        titel.Format.Font.Bold = true;
        var unter = r.Cells[1].AddParagraph(untertitel ?? "Direkt anmelden per QR-Code");
        unter.Format.Alignment = ParagraphAlignment.Right;
        unter.Format.Font.Size = 9.5;
        unter.Format.Font.Color = Akzent;
    }

    private void Fuss(Section s)
    {
        var t = s.Footers.Primary.AddTable();
        t.AddColumn(Unit.FromCentimeter(13));
        t.AddColumn(Unit.FromCentimeter(5));
        var r = t.AddRow();
        r.Borders.Top.Width = Unit.FromPoint(0.5);
        r.Borders.Top.Color = Linie;
        r.TopPadding = Unit.FromPoint(4);
        r.Format.Font.Size = 7.5;
        r.Format.Font.Color = Gedaempft;
        var links = r.Cells[0].AddParagraph(string.Join(" · ", new[] { briefkopf?.Name, briefkopf?.Website }.Where(x => !string.IsNullOrWhiteSpace(x))));
        links.Format.Font.Size = 7.5;
        var rechts = r.Cells[1].AddParagraph("Seite ");
        rechts.AddPageField();
        rechts.AddText(" von ");
        rechts.AddNumPagesField();
        rechts.Format.Alignment = ParagraphAlignment.Right;
    }

    private static void Anleitung(Section s)
    {
        var p = s.AddParagraph();
        p.AddFormattedText("So geht’s: ", TextFormat.Bold);
        p.AddText("Handykamera öffnen → QR-Code scannen → die Kursseite öffnet sich → online anmelden. ");
        p.AddText("Wer sein Gerät bei der vhs gemerkt hat, meldet sich mit einem Klick an.");
        p.Format.Font.Size = 8.5;
        p.Format.Font.Color = Gedaempft;
        p.Format.SpaceAfter = Unit.FromPoint(4);
    }

    private void Raster(Section s, IReadOnlyList<KursQrEintrag> kurse, bool schneidemarken)
    {
        // Jede Kartenzeile ist eine eigene Tabelle: keine geteilten Ränder zwischen Zeilen, und eine Zeile bricht nie über
        // eine Seite. Der Abstand ist ein leerer Absatz – am Seitenende unsichtbar.
        for (var z = 0; z < kurse.Count; z += 3)
        {
            if (z > 0)
            {
                var luecke = s.AddParagraph();
                luecke.Format.Font.Size = 1;
                luecke.Format.SpaceAfter = Unit.FromCentimeter(schneidemarken ? 0.2 : 0.4);
            }

            var t = s.AddTable();
            t.LeftPadding = Unit.FromPoint(6);
            t.RightPadding = Unit.FromPoint(6);
            for (var i = 0; i < 5; i++)
            {
                t.AddColumn(Unit.FromCentimeter(i % 2 == 0 ? KarteBreite : Abstand));
            }

            var r = t.AddRow();
            r.Height = Unit.FromCentimeter(KarteHoehe);
            r.HeightRule = RowHeightRule.AtLeast;
            r.TopPadding = Unit.FromPoint(6);
            r.BottomPadding = Unit.FromPoint(6);
            for (var i = 0; i < 3 && z + i < kurse.Count; i++)
            {
                Karte(r.Cells[i * 2], kurse[z + i], schneidemarken);
            }
        }
    }

    private void Karte(Cell c, KursQrEintrag k, bool schneidemarken)
    {
        foreach (var rand in new[] { c.Borders.Top, c.Borders.Bottom, c.Borders.Left, c.Borders.Right })
        {
            rand.Width = Unit.FromPoint(schneidemarken ? 0.75 : 0.6);
            rand.Color = schneidemarken ? Gedaempft : Linie;
            rand.Style = schneidemarken ? BorderStyle.DashSmallGap : BorderStyle.Single;
        }

        c.Format.Alignment = ParagraphAlignment.Center;
        // Bild in einem zentrierten Absatz: direkt in die Zelle gesetzte Bilder ignorieren die Ausrichtung der Zelle.
        var qrAbsatz = c.AddParagraph();
        qrAbsatz.Format.Alignment = ParagraphAlignment.Center;
        var bild = qrAbsatz.AddImage("base64:" + Convert.ToBase64String(qr.Png(k.Inhalt)));
        bild.Width = Unit.FromCentimeter(3.0);
        bild.LockAspectRatio = true;

        var n = c.AddParagraph(k.Nummer);
        n.Format.Font.Bold = true;
        n.Format.Font.Size = 13;
        n.Format.Font.Color = Akzent;
        n.Format.SpaceBefore = Unit.FromPoint(2);
        var titel = c.AddParagraph(Kuerzen(k.Titel, 70));
        titel.Format.Font.Bold = true;
        titel.Format.Font.Size = 9;
        if (k.Zusatz is { } zusatz)
        {
            var zp = c.AddParagraph(Kuerzen(zusatz, 80));
            zp.Format.Font.Size = 7.5;
            zp.Format.Font.Color = Gedaempft;
            zp.Format.SpaceBefore = Unit.FromPoint(1);
        }

        if (k.Preis is { } preis)
        {
            var pp = c.AddParagraph(preis);
            pp.Format.Font.Size = 8;
            pp.Format.Font.Bold = true;
            pp.Format.SpaceBefore = Unit.FromPoint(1);
        }
    }

    private static string Kuerzen(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
}
