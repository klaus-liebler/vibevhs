using System.Text;
using VibeVHS.Domain.Dokumente;

namespace VibeVHS.Application.Dokumente;

/// <summary>Ein Brief der VHS (Bescheinigung, Ausweis, Auskunft …). Jeder Brief trägt den QR-Code „Translate this letter in your language“.</summary>
public sealed record Brief(
    string Absender,
    string AbsenderZusatz,
    IReadOnlyList<string> Anschrift,
    string OrtDatum,
    string Titel,
    IReadOnlyList<string> Absaetze,
    IReadOnlyList<(string Feld, string Wert)> Angaben,
    IReadOnlyList<string>? TabellenKopf,
    IReadOnlyList<IReadOnlyList<string>>? Tabelle,
    string? Unterschrift,
    string? Hinweis,
    Uri Uebersetzungslink,
    string? PersoenlicherQrInhalt = null,
    string? PersoenlicherQrText = null)
{
    /// <summary>Text des Briefs für die Übersetzungsseite.</summary>
    public string Klartext()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Titel).AppendLine();
        foreach (var a in Absaetze)
        {
            sb.AppendLine(a).AppendLine();
        }

        foreach (var (f, w) in Angaben)
        {
            sb.AppendLine($"{f}: {w}");
        }

        if (Tabelle is not null && TabellenKopf is not null)
        {
            sb.AppendLine().AppendLine(string.Join(" | ", TabellenKopf));
            foreach (var z in Tabelle)
            {
                sb.AppendLine(string.Join(" | ", z));
            }
        }

        if (Hinweis is not null)
        {
            sb.AppendLine().AppendLine(Hinweis);
        }

        return sb.ToString().TrimEnd();
    }
}

public interface IBriefPdf
{
    byte[] Erzeugen(Brief brief);
}

public interface IQrCode
{
    /// <summary>QR-Code als SVG (für die Anzeige im Browser).</summary>
    string Svg(string inhalt);

    /// <summary>QR-Code als PNG (für PDFs).</summary>
    byte[] Png(string inhalt);
}

/// <summary>Übersetzung von Brief- und Mailtexten. In Stufe 1 ein Mock-Adapter, der den Text mit Platzhalter-Hinweis zurückgibt.</summary>
public interface IUebersetzer
{
    string Uebersetzen(string text, string sprache);
}

public interface IDokumentSpeicher
{
    Task<Dokument?> LadenAsync(Guid id, CancellationToken abbruch = default);

    Task<Dokument?> ZumUebersetzungsTokenAsync(string token, CancellationToken abbruch = default);

    Task<IReadOnlyList<Dokument>> DerPersonAsync(Guid personId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Dokument>> DesKursesAsync(Guid kursId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Dokument>> ZurBelegungAsync(Guid belegungId, CancellationToken abbruch = default);

    void Hinzufuegen(Dokument dokument);

    Task SpeichernAsync(CancellationToken abbruch = default);
}
