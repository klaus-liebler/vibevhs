using System.Globalization;
using System.Text;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Application.Betrieb;

/// <summary>Spaltentyp für Anzeige und Export (XLSX setzt Zahlen und Datum als echte Werte).</summary>
public enum Spaltentyp
{
    Text,
    Zahl,
    Geld,
    Prozent,
    Datum,
}

public sealed record Spalte(string Titel, Spaltentyp Typ = Spaltentyp.Text);

/// <summary>Eine Auswertung als Tabelle: Grundlage für Anzeige, CSV und XLSX.</summary>
public sealed record Tabelle(string Titel, string Beschreibung, IReadOnlyList<Spalte> Spalten, IReadOnlyList<IReadOnlyList<object?>> Zeilen)
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Text(object? wert, Spaltentyp typ) => wert switch
    {
        null => "",
        decimal d when typ == Spaltentyp.Geld => d.ToString("N2", De),
        decimal d when typ == Spaltentyp.Prozent => d.ToString("0.#", De) + " %",
        decimal d => d.ToString("0.##", De),
        int i => i.ToString(De),
        long l => l.ToString(De),
        DateOnly d => d.ToString("dd.MM.yyyy", De),
        DateTime d => d.ToString("dd.MM.yyyy", De),
        _ => wert.ToString() ?? "",
    };

    /// <summary>CSV mit Semikolon und deutschem Dezimalkomma (für Excel und die Kämmerei üblich), UTF-8 mit BOM.</summary>
    public byte[] Csv()
    {
        static string Feld(string s) => s.Contains(';') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : s;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', Spalten.Select(s => Feld(s.Titel))));
        foreach (var z in Zeilen)
        {
            sb.AppendLine(string.Join(';', z.Select((w, i) => Feld(Text(w, Spalten[i].Typ)))));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }
}

/// <summary>Kennzahlen für das Dashboard der Leitung (FA-50).</summary>
public sealed record Dashboarddaten(
    string Zeitraum,
    int Kurse,
    int KurseAbgesagt,
    int Termine,
    int TermineAusgefallen,
    int Belegungen,
    int Plaetze,
    long UmsatzCent,
    long OffeneForderungenCent,
    int OffeneAufgaben,
    int UeberfaelligeAufgaben,
    IReadOnlyList<(string Fachbereich, int Belegt, int Plaetze, long UmsatzCent)> JeFachbereich,
    IReadOnlyList<(string Monat, long UmsatzCent)> UmsatzJeMonat,
    IReadOnlyList<(string Kurs, int Belegt, int Min)> Unterbelegt)
{
    public decimal Auslastung => Plaetze == 0 ? 0 : Math.Round(100m * Belegungen / Plaetze, 1);

    public decimal Ausfallquote => Termine == 0 ? 0 : Math.Round(100m * TermineAusgefallen / Termine, 1);
}

/// <summary>Grunddaten für Auswertungen; die Infrastruktur liefert sie in einem Zug (Last: 3.000 Personen, 500 Kurse).</summary>
public interface IAuswertungsquelle
{
    Task<IReadOnlyList<Kurskennzahl>> KurseAsync(Guid? semesterId, int? fachbereich, CancellationToken abbruch = default);

    Task<IReadOnlyList<(Guid Id, string Name)>> SemesterAsync(CancellationToken abbruch = default);

    Task<IReadOnlyDictionary<int, string>> FachbereicheAsync(CancellationToken abbruch = default);

    /// <summary>Umsatzwirksame Belegpositionen eines Jahres (Rechnungen positiv, Stornos/Gutschriften negativ).</summary>
    Task<IReadOnlyList<(DateOnly Datum, int? Fachbereich, long Cent)>> UmsatzAsync(int jahr, CancellationToken abbruch = default);

    /// <summary>Belegungen eines Jahres mit groben Statistikmerkmalen (ohne Namen).</summary>
    Task<IReadOnlyList<Statistikfall>> StatistikfaelleAsync(int jahr, CancellationToken abbruch = default);

    Task<(int Offen, int Ueberfaellig)> AufgabenAsync(CancellationToken abbruch = default);
}

public sealed record Kurskennzahl(Guid Id, string Kursnummer, string Titel, int Fachbereich, string Semester, string Status, int Min, int Max, int Angemeldet,
    int Warteliste, int Termine, int Ausgefallen, decimal UeGeplant, decimal UeGehalten, decimal? Anwesenheitsquote, long UmsatzCent, DateTime? Beginn);

public sealed record Statistikfall(int Fachbereich, string Kursart, decimal UeKurs, string Geschlecht, string Altersgruppe, bool Integrationskurs);

/// <summary>Leserechte für Auswertungen und Statistik.</summary>
public static class Auswertungsrollen
{
    public static readonly Rolle[] Lesen = [Rolle.Leitung, Rolle.Verwaltungsleitung, Rolle.Fachbereichsleitung, Rolle.Buchhaltung, Rolle.Systemverwaltung, Rolle.Pruefung];
}

/// <summary>Excel-Export einer Tabelle (ClosedXML in der Infrastruktur).</summary>
public interface ITabellenExport
{
    byte[] Xlsx(Tabelle tabelle);
}
