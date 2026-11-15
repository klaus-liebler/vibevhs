using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using VibeVHS.Application.Datenschutz;

namespace VibeVHS.Infrastructure.Datenschutz;

/// <summary>Auskunft nach Art. 15 DSGVO als PDF (MigraDoc).</summary>
internal sealed class AuskunftPdf : IAuskunftPdf
{
    static AuskunftPdf()
    {
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
    }

    public byte[] Erzeugen(Auskunft a)
    {
        var dok = new Document();
        dok.Info.Title = "Auskunft nach Art. 15 DSGVO";
        var stil = dok.Styles[StyleNames.Normal]!;
        stil.Font.Name = "Arial";
        stil.Font.Size = 10;
        var s = dok.AddSection();
        s.PageSetup.TopMargin = Unit.FromCentimeter(2);
        s.PageSetup.LeftMargin = Unit.FromCentimeter(2.2);
        s.PageSetup.RightMargin = Unit.FromCentimeter(2);

        var kopf = s.AddParagraph(a.Einrichtung);
        kopf.Format.Font.Size = 9;
        kopf.Format.Font.Color = Colors.Gray;
        var titel = s.AddParagraph("Auskunft über gespeicherte personenbezogene Daten (Art. 15 DSGVO)");
        titel.Format.Font.Size = 15;
        titel.Format.Font.Bold = true;
        titel.Format.SpaceAfter = Unit.FromPoint(6);
        s.AddParagraph($"Betroffene Person: {a.Name} · Status: {a.Datenschutzstatus}");
        s.AddParagraph($"Erstellt am {a.ErstelltAm:dd.MM.yyyy HH:mm} von {a.ErstelltVon}").Format.Font.Color = Colors.Gray;

        Abschnitt(s, "Stammdaten (aktueller Stand)");
        var st = a.Stammdaten;
        Zeile(s, "Titel", st.Titel);
        Zeile(s, "Geburtsdatum", st.Geburtsdatum);
        Zeile(s, "Rollen", st.Rollen);
        Zeile(s, "Sprache", st.Sprache);
        Liste(s, "Postadressen", st.Adressen);
        Liste(s, "Telefon / Messaging", st.Rufnummern);
        Liste(s, "E-Mail", st.EMails);
        Liste(s, "Organisationen", st.Organisationen);
        Liste(s, "Buchungsvollmachten", st.Vollmachten);
        Liste(s, "Einwilligungen", st.Einwilligungen);

        Abschnitt(s, $"Datenhistorie ({a.Historie.Count} Einträge)");
        var t = s.AddTable();
        t.Borders.Width = 0.25;
        t.Borders.Color = Colors.LightGray;
        foreach (var breite in new[] { 2.4, 3.4, 6.2, 4.6 })
        {
            t.AddColumn(Unit.FromCentimeter(breite));
        }

        var k = t.AddRow();
        k.HeadingFormat = true;
        k.Format.Font.Bold = true;
        k.Cells[0].AddParagraph("Erfasst");
        k.Cells[1].AddParagraph("Feld");
        k.Cells[2].AddParagraph("Wert (alt → neu)");
        k.Cells[3].AddParagraph("Gültig / Art");
        foreach (var h in a.Historie)
        {
            var r = t.AddRow();
            r.Format.Font.Size = 8;
            r.Cells[0].AddParagraph(h.ErfasstAm.ToLocalTime().ToString("dd.MM.yyyy"));
            r.Cells[1].AddParagraph(h.Feld);
            r.Cells[2].AddParagraph((h.AlterWert is null ? "" : h.AlterWert + " → ") + h.NeuerWert);
            r.Cells[3].AddParagraph($"ab {h.GueltigVon:dd.MM.yyyy}{(h.GueltigBis is { } b ? $" bis {b.AddDays(-1):dd.MM.yyyy}" : "")} · {h.ArtAnzeige}{(h.Ersetzt ? " (ersetzt)" : "")}");
        }

        Abschnitt(s, "Kurse als Dozentin / Dozent");
        Liste(s, null, a.KurseAlsDozent.Select(x => $"{x.Kursnummer} {x.Titel}: {x.Termine} Termine {x.Zeitraum}").ToList());
        Abschnitt(s, "Kursbelegungen");
        Liste(s, null, a.Belegungen.Select(b => $"{b.Kurs}: {b.Status}, gebucht am {b.GebuchtAm.ToLocalTime():dd.MM.yyyy} von {b.GebuchtVon} ({b.Weg}), Entgelt {b.Entgelt}"
                                                 + (b.Kontingent is null ? "" : $"; {b.Kontingent}")).ToList());
        Abschnitt(s, "Ermäßigungen");
        Liste(s, null, a.Ermaessigungen);
        Abschnitt(s, "Nachrichten an Sie");
        Liste(s, null, a.Nachrichten.Select(n => $"{n.Datum.ToLocalTime():dd.MM.yyyy} {n.Kanal}: {n.Betreff} ({n.Status})").ToList());
        Abschnitt(s, "Empfänger");
        Liste(s, null, a.Empfaenger);
        Abschnitt(s, "Aufbewahrung");
        Liste(s, null, a.Aufbewahrung.Count == 0
            ? ["Keine gesetzliche Aufbewahrungspflicht; Löschung nach drei Jahren ohne Aktivität."]
            : a.Aufbewahrung.Select(f => $"{f.Datenart}: {f.Grund} – bis {f.Fristende} ({f.Rechtsgrundlage})").ToList());
        Abschnitt(s, "Verarbeitungen (Protokoll)");
        Liste(s, null, a.Verarbeitungen.Select(v => $"{v.Zeitpunkt.ToLocalTime():dd.MM.yyyy HH:mm} {v.Aktion} – {v.Bearbeiter}").ToList());
        Abschnitt(s, "Dokumente");
        Liste(s, null, a.Dokumente);
        Abschnitt(s, "Hinweise");
        Liste(s, null, a.Hinweise);

        var renderer = new PdfDocumentRenderer { Document = dok };
        renderer.RenderDocument();
        using var strom = new MemoryStream();
        renderer.PdfDocument.Save(strom, false);
        return strom.ToArray();
    }

    private static void Abschnitt(Section s, string text)
    {
        var p = s.AddParagraph(text);
        p.Format.Font.Size = 12;
        p.Format.Font.Bold = true;
        p.Format.SpaceBefore = Unit.FromPoint(12);
        p.Format.SpaceAfter = Unit.FromPoint(4);
    }

    private static void Zeile(Section s, string feld, string? wert)
    {
        var p = s.AddParagraph();
        p.AddFormattedText(feld + ": ", TextFormat.Bold);
        p.AddText(string.IsNullOrWhiteSpace(wert) ? "–" : wert);
    }

    private static void Liste(Section s, string? feld, IReadOnlyList<string> werte)
    {
        if (feld is not null)
        {
            s.AddParagraph().AddFormattedText(feld + ":", TextFormat.Bold);
        }

        if (werte.Count == 0)
        {
            s.AddParagraph("–");
        }

        foreach (var w in werte)
        {
            var p = s.AddParagraph("• " + w);
            p.Format.LeftIndent = Unit.FromCentimeter(0.4);
        }
    }
}
