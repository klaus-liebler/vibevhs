using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Application.Betrieb;

public sealed record Auswertungsfilter(Guid? SemesterId = null, int? Fachbereich = null, int? Jahr = null);

public sealed record Bericht(string Schluessel, string Name, string Beschreibung, bool Statistik = false);

/// <summary>
/// Dashboard der Leitung (FA-50), Auswertungen mit Export (FA-51) und Statistik-Mock (FA-52: VHS-Statistik des DIE,
/// Weiterbildungsgesetz NRW) – nur aggregierte Zahlen, keine Personendaten. Fachbereichsleitungen sehen ihre Fachbereiche.
/// </summary>
public sealed class AuswertungsDienst(IAuswertungsquelle quelle, PostausgangDienst post, INachrichtenSpeicher nachrichten, IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    public static readonly IReadOnlyList<Bericht> Berichte =
    [
        new("kurse", "Kursliste mit Belegung", "Je Kurs: Mindest-/Höchstzahl, Angemeldete, Warteliste, Termine, Ausfälle, UE, Anwesenheitsquote, Umsatz."),
        new("auslastung", "Auslastung je Fachbereich", "Belegte Plätze, Kapazität und Auslastung je Fachbereich."),
        new("umsatz", "Umsatz je Monat und Fachbereich", "Festgeschriebene Rechnungen abzüglich Stornos und Gutschriften (Belegdatum)."),
        new("unterbelegt", "Unterbelegte Kurse", "Kurse unter der Mindestteilnehmerzahl."),
        new("die", "VHS-Statistik (DIE)", "Kurse, Unterrichtsstunden und Belegungen je Programmbereich, Geschlecht und Alter – aggregiert.", Statistik: true),
        new("wbg", "Weiterbildungsgesetz NRW", "Unterrichtsstunden und Teilnahmefälle je Sachbereich (Mock-Meldung) – aggregiert.", Statistik: true),
    ];

    /// <summary>Zuordnung Fachbereich → Programmbereich der VHS-Statistik (mit der Statistikstelle abzustimmen).</summary>
    public static string Programmbereich(int fachbereich, bool integrationskurs) => integrationskurs ? "4 Sprachen (Deutsch als Zweitsprache)" : fachbereich switch
    {
        1 or 3 => "1 Politik – Gesellschaft – Umwelt",
        2 => "2 Kultur – Gestalten",
        7 or 8 => "3 Gesundheit",
        5 => "4 Sprachen",
        6 => "5 Arbeit – Beruf",
        4 => "6 Grundbildung – Schulabschlüsse",
        _ => "1 Politik – Gesellschaft – Umwelt",
    };

    /// <summary>Sachbereiche nach WbG NRW (vereinfacht; mit der Bezirksregierung abzustimmen).</summary>
    public static string Sachbereich(int fachbereich, bool integrationskurs) => integrationskurs || fachbereich == 4 ? "Grundbildung, Schulabschlüsse, Deutsch als Zweitsprache"
        : fachbereich switch
        {
            1 or 3 => "Politische Bildung",
            6 => "Berufliche Bildung",
            7 or 8 => "Gesundheitsbildung",
            5 => "Sprachen",
            _ => "Kulturelle Bildung",
        };

    private async Task<Benutzer> RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !Auswertungsrollen.Lesen.Contains(r)))
        {
            throw new InvalidOperationException(Freigabe.NurFuer(wer.Rolle, Auswertungsrollen.Lesen).Begruendung);
        }

        return wer;
    }

    private static bool Sichtbar(Benutzer wer, int fb) => wer.Rolle != Rolle.Fachbereichsleitung || wer.ZustaendigeFachbereiche.Contains(fb);

    // ---------- Dashboard ----------

    public async Task<Dashboarddaten> DashboardAsync(Guid? semesterId, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var kurse = (await quelle.KurseAsync(semesterId, null, abbruch)).Where(k => Sichtbar(wer, k.Fachbereich)).ToList();
        var fb = await quelle.FachbereicheAsync(abbruch);
        var jahr = uhr.GetLocalNow().Year;
        var umsatz = (await quelle.UmsatzAsync(jahr, abbruch)).Where(u => u.Fachbereich is null ? wer.Rolle != Rolle.Fachbereichsleitung : Sichtbar(wer, u.Fachbereich.Value)).ToList();
        var (offen, ueberfaellig) = await quelle.AufgabenAsync(abbruch);
        var aktiv = kurse.Where(k => k.Status != "Abgesagt").ToList();
        var semester = semesterId is { } s ? (await quelle.SemesterAsync(abbruch)).FirstOrDefault(x => x.Id == s).Name ?? "Semester" : "alle Semester";
        return new Dashboarddaten(semester, kurse.Count, kurse.Count(k => k.Status == "Abgesagt"), kurse.Sum(k => k.Termine), kurse.Sum(k => k.Ausgefallen),
            aktiv.Sum(k => k.Angemeldet), aktiv.Sum(k => k.Max), kurse.Sum(k => k.UmsatzCent), 0, offen, ueberfaellig,
            aktiv.GroupBy(k => k.Fachbereich).OrderBy(g => g.Key)
                .Select(g => ($"{g.Key} {fb.GetValueOrDefault(g.Key) ?? ""}".Trim(), g.Sum(k => k.Angemeldet), g.Sum(k => k.Max), g.Sum(k => k.UmsatzCent))).ToList(),
            Enumerable.Range(1, 12).Select(m => (new DateOnly(jahr, m, 1).ToString("MMM", Tabelle.De), umsatz.Where(u => u.Datum.Month == m).Sum(u => u.Cent))).ToList(),
            aktiv.Where(k => k.Angemeldet < k.Min && k.Beginn is { } b && b > uhr.GetLocalNow().DateTime).OrderBy(k => k.Beginn).Take(8)
                .Select(k => ($"{k.Kursnummer} {k.Titel}", k.Angemeldet, k.Min)).ToList());
    }

    // ---------- Berichte ----------

    public async Task<Tabelle> TabelleAsync(string bericht, Auswertungsfilter filter, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var fbNamen = await quelle.FachbereicheAsync(abbruch);
        string Fb(int nr) => $"{nr} {fbNamen.GetValueOrDefault(nr)}".Trim();
        var jahr = filter.Jahr ?? uhr.GetLocalNow().Year;
        switch (bericht)
        {
            case "kurse":
            {
                var kurse = (await quelle.KurseAsync(filter.SemesterId, filter.Fachbereich, abbruch)).Where(k => Sichtbar(wer, k.Fachbereich)).OrderBy(k => k.Kursnummer).ToList();
                return new Tabelle("Kursliste mit Belegung", Berichte[0].Beschreibung,
                [
                    new("Kursnummer"), new("Titel"), new("Fachbereich"), new("Semester"), new("Status"), new("Beginn", Spaltentyp.Datum), new("Min", Spaltentyp.Zahl),
                    new("Max", Spaltentyp.Zahl), new("Angemeldet", Spaltentyp.Zahl), new("Warteliste", Spaltentyp.Zahl), new("Termine", Spaltentyp.Zahl),
                    new("Ausgefallen", Spaltentyp.Zahl), new("UE geplant", Spaltentyp.Zahl), new("UE gehalten", Spaltentyp.Zahl), new("Anwesenheit", Spaltentyp.Prozent),
                    new("Umsatz €", Spaltentyp.Geld),
                ], kurse.Select(k => (IReadOnlyList<object?>)[k.Kursnummer, k.Titel, Fb(k.Fachbereich), k.Semester, k.Status, k.Beginn, k.Min, k.Max, k.Angemeldet, k.Warteliste,
                    k.Termine, k.Ausgefallen, k.UeGeplant, k.UeGehalten, k.Anwesenheitsquote, k.UmsatzCent / 100m]).ToList());
            }

            case "auslastung":
            {
                var kurse = (await quelle.KurseAsync(filter.SemesterId, null, abbruch)).Where(k => Sichtbar(wer, k.Fachbereich) && k.Status != "Abgesagt").ToList();
                return new Tabelle("Auslastung je Fachbereich", Berichte[1].Beschreibung,
                    [new("Fachbereich"), new("Kurse", Spaltentyp.Zahl), new("Angemeldet", Spaltentyp.Zahl), new("Plätze", Spaltentyp.Zahl), new("Auslastung", Spaltentyp.Prozent),
                        new("Termine ausgefallen", Spaltentyp.Zahl)],
                    kurse.GroupBy(k => k.Fachbereich).OrderBy(g => g.Key).Select(g => (IReadOnlyList<object?>)[Fb(g.Key), g.Count(), g.Sum(k => k.Angemeldet), g.Sum(k => k.Max),
                        g.Sum(k => k.Max) == 0 ? 0m : Math.Round(100m * g.Sum(k => k.Angemeldet) / g.Sum(k => k.Max), 1), g.Sum(k => k.Ausgefallen)]).ToList());
            }

            case "umsatz":
            {
                var umsatz = (await quelle.UmsatzAsync(jahr, abbruch)).Where(u => u.Fachbereich is null ? wer.Rolle != Rolle.Fachbereichsleitung : Sichtbar(wer, u.Fachbereich.Value)).ToList();
                var bereiche = umsatz.Select(u => u.Fachbereich).Distinct().OrderBy(f => f ?? 99).ToList();
                return new Tabelle($"Umsatz {jahr} je Monat und Fachbereich", Berichte[2].Beschreibung,
                    [new("Monat"), .. bereiche.Select(f => new Spalte(f is { } n ? Fb(n) : "ohne Kursbezug", Spaltentyp.Geld)), new("Summe", Spaltentyp.Geld)],
                    Enumerable.Range(1, 12).Select(m => (IReadOnlyList<object?>)[new DateOnly(jahr, m, 1).ToString("MMMM", Tabelle.De),
                        .. bereiche.Select(f => (object?)(umsatz.Where(u => u.Datum.Month == m && u.Fachbereich == f).Sum(u => u.Cent) / 100m)),
                        umsatz.Where(u => u.Datum.Month == m).Sum(u => u.Cent) / 100m]).ToList());
            }

            case "unterbelegt":
            {
                var kurse = (await quelle.KurseAsync(filter.SemesterId, filter.Fachbereich, abbruch))
                    .Where(k => Sichtbar(wer, k.Fachbereich) && k.Status != "Abgesagt" && k.Angemeldet < k.Min).OrderBy(k => k.Beginn).ToList();
                return new Tabelle("Unterbelegte Kurse", Berichte[3].Beschreibung,
                    [new("Kursnummer"), new("Titel"), new("Fachbereich"), new("Beginn", Spaltentyp.Datum), new("Angemeldet", Spaltentyp.Zahl), new("Min", Spaltentyp.Zahl),
                        new("fehlen", Spaltentyp.Zahl)],
                    kurse.Select(k => (IReadOnlyList<object?>)[k.Kursnummer, k.Titel, Fb(k.Fachbereich), k.Beginn, k.Angemeldet, k.Min, k.Min - k.Angemeldet]).ToList());
            }

            case "die" or "wbg":
                return await StatistikAsync(bericht, jahr, abbruch);

            default:
                throw new InvalidOperationException($"Unbekannte Auswertung „{bericht}“.");
        }
    }

    private async Task<Tabelle> StatistikAsync(string art, int jahr, CancellationToken abbruch)
    {
        var faelle = await quelle.StatistikfaelleAsync(jahr, abbruch);
        if (art == "die")
        {
            string[] alter = ["unter 18", "18–24", "25–34", "35–49", "50–64", "65 und älter", "unbekannt"];
            return new Tabelle($"VHS-Statistik {jahr} (DIE) – Programmbereiche", Berichte[4].Beschreibung,
            [
                new("Programmbereich"), new("Belegungen", Spaltentyp.Zahl), new("Unterrichtsstunden (UE)", Spaltentyp.Zahl), new("weiblich", Spaltentyp.Zahl), new("männlich", Spaltentyp.Zahl),
                new("divers/keine Angabe", Spaltentyp.Zahl), .. alter.Select(a => new Spalte(a, Spaltentyp.Zahl)),
            ], faelle.GroupBy(f => Programmbereich(f.Fachbereich, f.Integrationskurs)).OrderBy(g => g.Key)
                .Select(g => (IReadOnlyList<object?>)[g.Key, g.Count(), g.Sum(f => f.UeKurs), g.Count(f => f.Geschlecht == "weiblich"), g.Count(f => f.Geschlecht == "männlich"),
                    g.Count(f => f.Geschlecht is not ("weiblich" or "männlich")), .. alter.Select(a => (object?)g.Count(f => Altersstufe(f.Altersgruppe) == a))]).ToList());
        }

        return new Tabelle($"Weiterbildungsgesetz NRW {jahr} – Sachbereiche", Berichte[5].Beschreibung,
            [new("Sachbereich"), new("Teilnahmefälle", Spaltentyp.Zahl), new("Unterrichtsstunden (UE)", Spaltentyp.Zahl)],
            faelle.GroupBy(f => Sachbereich(f.Fachbereich, f.Integrationskurs)).OrderBy(g => g.Key)
                .Select(g => (IReadOnlyList<object?>)[g.Key, g.Count(), g.Sum(f => f.UeKurs)]).ToList());
    }

    private static string Altersstufe(string? gruppe) => gruppe switch
    {
        null or "" or "unbekannt" => "unbekannt",
        _ when gruppe.StartsWith("unter", StringComparison.Ordinal) || gruppe.StartsWith("0", StringComparison.Ordinal) => "unter 18",
        _ when gruppe.StartsWith("18", StringComparison.Ordinal) => "18–24",
        _ when gruppe.StartsWith("25", StringComparison.Ordinal) => "25–34",
        _ when gruppe.StartsWith("35", StringComparison.Ordinal) => "35–49",
        _ when gruppe.StartsWith("50", StringComparison.Ordinal) => "50–64",
        _ => "65 und älter",
    };

    /// <summary>Statistik an die Statistikstelle übermitteln (Mock: Postausgang) – nur aggregierte Zahlen.</summary>
    public async Task UebermittelnAsync(string art, int jahr, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        if (wer.Rolle is not (Rolle.Leitung or Rolle.Verwaltungsleitung) && !wer.IstAutomatik)
        {
            throw new InvalidOperationException(Freigabe.NurFuer(wer.Rolle, Rolle.Leitung, Rolle.Verwaltungsleitung).Begruendung);
        }

        var t = await StatistikAsync(art, jahr, abbruch);
        await post.SchnittstelleEinreihenAsync(art == "die" ? "VHS-Statistik DIE (Mock)" : "WbG NRW (Mock)", t.Titel, System.Text.Encoding.UTF8.GetString(t.Csv()).TrimStart('﻿'), abbruch);
        await protokoll.ProtokollierenAsync("Statistik.Uebermittelt", $"{t.Titel} übermittelt ({t.Zeilen.Count} Zeilen, nur aggregierte Zahlen)", abbruch: abbruch);
        await nachrichten.SpeichernAsync(abbruch);
    }

    /// <summary>Export protokollieren (wer hat welche Auswertung wann exportiert).</summary>
    public async Task ExportProtokollierenAsync(string bericht, string format, CancellationToken abbruch = default)
    {
        await protokoll.ProtokollierenAsync("Auswertung.Export", $"Auswertung „{bericht}“ als {format.ToUpperInvariant()} exportiert", abbruch: abbruch);
        await nachrichten.SpeichernAsync(abbruch);
    }
}
