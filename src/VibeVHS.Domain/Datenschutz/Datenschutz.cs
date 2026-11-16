using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Datenschutz;

/// <summary>Datenschutz-Lebenszyklus einer Person: aktiv → archiviert → gesperrt (Art. 18 DSGVO) → anonymisiert.</summary>
public enum Datenschutzstatus
{
    Aktiv,
    Archiviert,
    Gesperrt,
    Anonymisiert,
}

public static class DatenschutzAnzeige
{
    public static string Text(Datenschutzstatus s) => s switch
    {
        Datenschutzstatus.Archiviert => "archiviert",
        Datenschutzstatus.Gesperrt => "gesperrt",
        Datenschutzstatus.Anonymisiert => "anonymisiert",
        _ => "aktiv",
    };
}

/// <summary>Pseudonyme Bezeichnungen für Protokolle: Das Protokoll enthält nie Klarnamen von Personen.</summary>
public static class Pseudonym
{
    public static string Kurz(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    public static string Person(Guid id) => $"Person #{Kurz(id)}";
}

/// <summary>Nach der Anonymisierung bleiben nur grobe Statistikmerkmale.</summary>
public static class Statistik
{
    public static string Altersgruppe(DateOnly? geburtsdatum, DateOnly heute)
    {
        if (geburtsdatum is not { } g)
        {
            return "unbekannt";
        }

        var alter = heute.Year - g.Year - (heute < g.AddYears(heute.Year - g.Year) ? 1 : 0);
        return alter switch
        {
            < 18 => "unter 18",
            < 25 => "18–24",
            < 35 => "25–34",
            < 50 => "35–49",
            < 65 => "50–64",
            _ => "65 und älter",
        };
    }

    /// <summary>Geburtsjahr-Kohorte in 5-Jahres-Schritten („1985–1989“) – bleibt nach der Anonymisierung statt des Geburtsdatums.</summary>
    public static string Geburtskohorte(DateOnly geburtsdatum)
    {
        var von = geburtsdatum.Year - (geburtsdatum.Year % 5);
        return $"{von}–{von + 4}";
    }

    /// <summary>
    /// Altersgruppe zum Stichtag aus einer Geburtsjahr-Kohorte (Mitte der Kohorte, 1. Juli). An Gruppengrenzen ist das ungenau
    /// (± 2 Jahre) – für anonymisierte Datensätze ist das die bewusst gewählte Unschärfe.
    /// </summary>
    public static string AltersgruppeAusKohorte(string? kohorte, DateOnly stichtag)
        => kohorte is { Length: >= 4 } k && int.TryParse(k[..4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var von)
            ? Altersgruppe(new DateOnly(von + 2, 7, 1), stichtag)
            : "unbekannt";

    private static readonly Dictionary<string, string> Kreise = new(StringComparer.Ordinal)
    {
        ["48282"] = "Kreis Steinfurt",
        ["48268"] = "Kreis Steinfurt",
        ["48369"] = "Kreis Steinfurt",
        ["48356"] = "Kreis Steinfurt",
        ["48291"] = "Kreis Steinfurt",
        ["48431"] = "Kreis Steinfurt",
        ["48565"] = "Kreis Steinfurt",
        ["48143"] = "Stadt Münster",
        ["48145"] = "Stadt Münster",
        ["48147"] = "Stadt Münster",
        ["48149"] = "Stadt Münster",
        ["48151"] = "Stadt Münster",
        ["48153"] = "Stadt Münster",
        ["48155"] = "Stadt Münster",
        ["48157"] = "Stadt Münster",
        ["48159"] = "Stadt Münster",
        ["48161"] = "Stadt Münster",
        ["48163"] = "Stadt Münster",
        ["48165"] = "Stadt Münster",
        ["48167"] = "Stadt Münster",
    };

    /// <summary>Kreis bzw. grobes Gebiet aus der Postleitzahl (keine genaue Adresse).</summary>
    public static string Gebiet(string? postleitzahl)
        => string.IsNullOrWhiteSpace(postleitzahl) ? "unbekannt"
            : Kreise.TryGetValue(postleitzahl.Trim(), out var k) ? k
            : postleitzahl.Trim().Length >= 2 ? $"PLZ-Gebiet {postleitzahl.Trim()[..2]}" : "unbekannt";
}

/// <summary>Aufbewahrungsfrist je Datenart (einstellbar; Vorschläge, mit dem Datenschutzbeauftragten abzustimmen).</summary>
public sealed class Aufbewahrungsfrist
{
    private Aufbewahrungsfrist()
    {
        Schluessel = string.Empty;
        Bezeichnung = string.Empty;
        Rechtsgrundlage = string.Empty;
    }

    public Aufbewahrungsfrist(string schluessel, string bezeichnung, int jahre, bool abJahresende, string rechtsgrundlage, string? hinweis = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schluessel);
        Schluessel = schluessel;
        Bezeichnung = bezeichnung;
        Aendern(jahre, abJahresende, rechtsgrundlage, hinweis);
        Rechtsgrundlage = rechtsgrundlage;
    }

    public string Schluessel { get; private set; }

    public string Bezeichnung { get; private set; }

    public int Jahre { get; private set; }

    /// <summary>Frist beginnt mit dem Schluss des Kalenderjahres (wie § 147 AO).</summary>
    public bool AbJahresende { get; private set; }

    public string Rechtsgrundlage { get; private set; }

    public string? Hinweis { get; private set; }

    public void Aendern(int jahre, bool abJahresende, string rechtsgrundlage, string? hinweis)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(jahre);
        Jahre = jahre;
        AbJahresende = abJahresende;
        Rechtsgrundlage = rechtsgrundlage;
        Hinweis = hinweis;
    }

    /// <summary>Letzter Tag der Aufbewahrung für einen Vorgang mit Bezugsdatum.</summary>
    public DateOnly Fristende(DateOnly bezug) => AbJahresende ? new DateOnly(bezug.Year + Jahre, 12, 31) : bezug.AddYears(Jahre);

    public static IReadOnlyList<Aufbewahrungsfrist> Vorschlaege() =>
    [
        new("stammdaten", "Stammdaten ohne weitere Pflichten (letzte Aktivität)", 3, false, "Art. 5 Abs. 1 e, Art. 17 DSGVO", "Vorschlag – mit dem Datenschutzbeauftragten abstimmen"),
        new("buchhaltung", "Buchungsbelege, Rechnungen, Honorarabrechnungen", 8, true, "§ 147 Abs. 1 Nr. 4, Abs. 3 AO, § 257 HGB", "gesetzlich (8 Jahre seit 2025); Beginn mit Schluss des Kalenderjahres"),
        new("handelsbriefe", "Geschäftsbriefe (z. B. Anmeldebestätigungen)", 6, true, "§ 147 Abs. 1 Nr. 2, 3 AO", "Vorschlag"),
        new("integrationskurs", "Integrationskurs: Teilnahme- und Abrechnungsunterlagen", 10, true, "IntV, Zuwendungs-/Abrechnungsrecht BAMF", "Vorschlag – mit den Vorgaben des BAMF abgleichen"),
        new("bescheinigungen", "Teilnahme- und Prüfungsdaten für Zweitschriften", 10, true, "berechtigtes Interesse (Art. 6 Abs. 1 f DSGVO)", "Vorschlag"),
        new("einwilligung", "Nachweis erteilter oder widerrufener Einwilligungen", 3, true, "Art. 7 Abs. 1 DSGVO (Nachweispflicht)", "Vorschlag"),
        new("postausgang", "Inhalte versandter Nachrichten", 2, false, "Art. 5 Abs. 1 e DSGVO", "Vorschlag"),
    ];
}

/// <summary>Ein Vorgang, der die Aufbewahrung von Daten einer Person begründet (z. B. Honorarabrechnung als Dozentin).</summary>
public sealed record Aufbewahrungsgrund(string FristSchluessel, string Beschreibung, DateOnly Bezugsdatum);

public enum Loeschaktion
{
    Anonymisieren,
    Sperren,
}

public sealed record Loeschentscheidung(Loeschaktion Aktion, DateOnly? GesperrtBis, IReadOnlyList<string> Gruende);

public static class Loeschregel
{
    /// <summary>Läuft noch eine Frist, wird gesperrt (bis zum spätesten Fristende), sonst anonymisiert.</summary>
    public static Loeschentscheidung Entscheiden(IEnumerable<Aufbewahrungsgrund> gruende, IReadOnlyDictionary<string, Aufbewahrungsfrist> fristen, DateOnly heute)
    {
        var laufend = gruende
            .Select(g => (g, frist: fristen.GetValueOrDefault(g.FristSchluessel)))
            .Where(x => x.frist is not null)
            .Select(x => (x.g, x.frist, ende: x.frist!.Fristende(x.g.Bezugsdatum)))
            .Where(x => x.ende >= heute)
            .ToList();
        return laufend.Count == 0
            ? new Loeschentscheidung(Loeschaktion.Anonymisieren, null, [])
            : new Loeschentscheidung(Loeschaktion.Sperren, laufend.Max(x => x.ende),
                laufend.Select(x => $"{x.frist!.Bezeichnung}: {x.g.Beschreibung}, Frist bis {x.ende:dd.MM.yyyy} ({x.frist.Rechtsgrundlage})").ToList());
    }
}

public enum Loeschlaufstatus
{
    Vorschau,
    TeilweiseFreigegeben,
    Ausgefuehrt,
    Verworfen,
}

/// <summary>
/// Regelbasierter Löschlauf: Vorschau mit Treffern → zwei Freigaben durch verschiedene Personen → Ausführung.
/// Nach der Ausführung bleiben nur Zahlen, keine Personendaten.
/// </summary>
public sealed class Loeschlauf
{
    private Loeschlauf()
    {
        Regel = string.Empty;
        ErstelltVon = string.Empty;
    }

    public Loeschlauf(string regel, IEnumerable<Guid> personIds, int zuAnonymisieren, int zuSperren, DateTimeOffset erstelltAm, string erstelltVon)
    {
        Id = Ids.Neu();
        Regel = regel;
        PersonIds = personIds.ToList();
        ZuAnonymisieren = zuAnonymisieren;
        ZuSperren = zuSperren;
        ErstelltAm = erstelltAm;
        ErstelltVon = erstelltVon;
        Status = Loeschlaufstatus.Vorschau;
    }

    public Guid Id { get; private set; }

    public string Regel { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }

    public string ErstelltVon { get; private set; }

    /// <summary>Treffer bis zur Ausführung; danach geleert (Löschprotokoll ohne Personendaten).</summary>
    /// <remarks>Gespeichert als eigene Tabelle (je Eintrag eine Zeile, siehe <see cref="Gemeinsam.Listenwert{T}"/>).</remarks>
    public List<Guid> PersonIds
    {
        get => Gemeinsam.Listenwerte.Werte(_personIds);
        private set => _personIds = Gemeinsam.Listenwerte.Von(value);
    }

    private List<Gemeinsam.Listenwert<Guid>> _personIds = [];

    public int ZuAnonymisieren { get; private set; }

    public int ZuSperren { get; private set; }

    public Loeschlaufstatus Status { get; private set; }

    public string? Freigabe1Kennung { get; private set; }

    public string? Freigabe1Name { get; private set; }

    public DateTimeOffset? Freigabe1Am { get; private set; }

    public string? Freigabe2Kennung { get; private set; }

    public string? Freigabe2Name { get; private set; }

    public DateTimeOffset? Freigabe2Am { get; private set; }

    public DateTimeOffset? AusgefuehrtAm { get; private set; }

    public int Anonymisiert { get; private set; }

    public int Gesperrt { get; private set; }

    public int Uebersprungen { get; private set; }

    public int Treffer => ZuAnonymisieren + ZuSperren;

    public bool VollstaendigFreigegeben => Freigabe1Kennung is not null && Freigabe2Kennung is not null;

    /// <summary>Freigabe; die zweite muss von einer anderen Person kommen. Liefert true, wenn damit beide Freigaben vorliegen.</summary>
    public bool Freigeben(string kennung, string name, bool rolleDarfFreigeben, DateTimeOffset jetzt)
    {
        if (Status is Loeschlaufstatus.Ausgefuehrt or Loeschlaufstatus.Verworfen)
        {
            throw new InvalidOperationException("Dieser Löschlauf ist abgeschlossen.");
        }

        if (!rolleDarfFreigeben)
        {
            throw new InvalidOperationException("Löschläufe dürfen nur Verwaltungsleitung oder Leitung freigeben.");
        }

        if (Freigabe1Kennung is null)
        {
            (Freigabe1Kennung, Freigabe1Name, Freigabe1Am) = (kennung, name, jetzt);
            Status = Loeschlaufstatus.TeilweiseFreigegeben;
            return false;
        }

        if (string.Equals(Freigabe1Kennung, kennung, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Vier-Augen-Prinzip: Die zweite Freigabe muss von einer anderen Person kommen.");
        }

        (Freigabe2Kennung, Freigabe2Name, Freigabe2Am) = (kennung, name, jetzt);
        return true;
    }

    public void AlsAusgefuehrt(int anonymisiert, int gesperrt, int uebersprungen, DateTimeOffset jetzt)
    {
        if (!VollstaendigFreigegeben)
        {
            throw new InvalidOperationException("Ohne zwei Freigaben wird nichts gelöscht.");
        }

        (Anonymisiert, Gesperrt, Uebersprungen, AusgefuehrtAm) = (anonymisiert, gesperrt, uebersprungen, jetzt);
        Status = Loeschlaufstatus.Ausgefuehrt;
        PersonIds = [];
    }

    public void Verwerfen()
    {
        if (Status == Loeschlaufstatus.Ausgefuehrt)
        {
            throw new InvalidOperationException("Ein ausgeführter Löschlauf kann nicht verworfen werden.");
        }

        Status = Loeschlaufstatus.Verworfen;
        PersonIds = [];
    }
}
