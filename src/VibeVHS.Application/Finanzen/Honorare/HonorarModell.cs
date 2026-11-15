using System.Globalization;

namespace VibeVHS.Application.Finanzen.Honorare;

public enum Parametertyp
{
    Geld,
    Zahl,
    Text,
    JaNein,
    Auswahl,
}

/// <summary>Beschreibung eines Modellparameters. Daraus erzeugt die Oberfläche das Formular und prüft die Eingaben.</summary>
public sealed record HonorarParameter(string Name, string Anzeige, Parametertyp Typ, bool Pflicht = false, string? Standard = null, string? Einheit = null,
    string? Hilfe = null, IReadOnlyList<string>? Optionen = null, decimal? Minimum = null, decimal? Maximum = null);

public enum Terminstatus
{
    Stattgefunden,

    /// <summary>Rechtzeitig ausgefallen.</summary>
    Ausgefallen,

    /// <summary>Weniger als 48 Stunden vor Beginn abgesagt (Zeitpunkt der Absage ist bekannt).</summary>
    KurzfristigAbgesagt,
}

/// <summary>Ein Termin der Kursleitung – nur anonyme Teilnahmedaten, keine Namen oder IDs von Teilnehmenden.</summary>
public sealed record HonorarTermin(Guid TerminId, Guid KursId, string Kursnummer, string Kursart, int Fachbereich, DateOnly Datum, DateTime Beginn, DateTime Ende,
    decimal Ue, Terminstatus Status, string? Ausfallgrund, string Ort, decimal? EntfernungKm, int Angemeldet, int? Anwesend, int Ermaessigt);

/// <summary>Bereits abgerechnete Position (aus früheren Honorarabrechnungen, nur lesend).</summary>
public sealed record FruehereHonorarposition(string Belegnummer, DateOnly Belegdatum, DateOnly? Von, DateOnly? Bis, Guid? TerminId, string Text, decimal Betrag,
    string Modell, string Version);

/// <summary>Alles, was ein Honorarmodell zur Berechnung sieht. Kein Datenbank-, Netz- oder Uhrzugriff.</summary>
public sealed record HonorarKontext(DateOnly Von, DateOnly Bis, IReadOnlyDictionary<string, string> Parameter, DateOnly VertragAb, DateOnly? VertragBis,
    IReadOnlyList<HonorarTermin> Termine, IReadOnlyList<FruehereHonorarposition> Frueher)
{
    public decimal Dezimal(string name, decimal standard = 0)
        => Parameter.TryGetValue(name, out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : standard;

    public decimal? DezimalOderLeer(string name)
        => Parameter.TryGetValue(name, out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    public string Text(string name, string standard = "") => Parameter.TryGetValue(name, out var s) && !string.IsNullOrWhiteSpace(s) ? s : standard;

    public bool JaNein(string name) => Parameter.TryGetValue(name, out var s) && s is "true" or "ja";

    /// <summary>Wurde dieser Termin schon einmal abgerechnet?</summary>
    public bool Abgerechnet(Guid terminId) => Frueher.Any(f => f.TerminId == terminId);
}

/// <summary>Eine berechnete Position. Bezug: ein Termin oder der Zeitraum.</summary>
public sealed record HonorarPosition(string Text, decimal Betrag, Guid? TerminId = null, DateOnly? Von = null, DateOnly? Bis = null, decimal? Menge = null,
    decimal? Einzelpreis = null, string Art = "Honorar");

public sealed record HonorarErgebnis(IReadOnlyList<HonorarPosition> Positionen, IReadOnlyList<string> Hinweise)
{
    public decimal Summe => Positionen.Sum(p => p.Betrag);
}

/// <summary>
/// Honorarmodell als Plugin. Muss rein und deterministisch sein: gleiche Eingabe, gleiches Ergebnis; nur der Kontext zählt.
/// Kennung und Version werden an jeder festgeschriebenen Abrechnung gespeichert.
/// </summary>
public interface IHonorarModell
{
    string Kennung { get; }

    string Anzeigename { get; }

    string Version { get; }

    string Beschreibung { get; }

    IReadOnlyList<HonorarParameter> Parameter { get; }

    HonorarErgebnis Berechne(HonorarKontext kontext);
}

/// <summary>Prüft und normalisiert Parameterwerte anhand der Beschreibung.</summary>
public static class Parameterpruefung
{
    /// <summary>Liefert normalisierte Werte (invariante Zahlen, Standardwerte) oder die Fehler je Parameter.</summary>
    public static (Dictionary<string, string> Werte, Dictionary<string, string> Fehler) Pruefen(IHonorarModell modell, IReadOnlyDictionary<string, string?> eingabe)
    {
        ArgumentNullException.ThrowIfNull(modell);
        ArgumentNullException.ThrowIfNull(eingabe);
        var werte = new Dictionary<string, string>();
        var fehler = new Dictionary<string, string>();
        foreach (var p in modell.Parameter)
        {
            var roh = eingabe.TryGetValue(p.Name, out var e) && !string.IsNullOrWhiteSpace(e) ? e.Trim() : p.Standard;
            if (string.IsNullOrWhiteSpace(roh))
            {
                if (p.Pflicht)
                {
                    fehler[p.Name] = $"„{p.Anzeige}“ ist erforderlich.";
                }

                continue;
            }

            switch (p.Typ)
            {
                case Parametertyp.Geld or Parametertyp.Zahl:
                    var text = roh.Replace("€", "", StringComparison.Ordinal).Replace("%", "", StringComparison.Ordinal).Trim();
                    // Komma = deutsche Eingabe („32,50“), sonst invariante Schreibweise („32.50“, auch Standardwerte).
                    var kultur = text.Contains(',', StringComparison.Ordinal) ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.InvariantCulture;
                    if (!decimal.TryParse(text, NumberStyles.Number, kultur, out var d))
                    {
                        fehler[p.Name] = $"„{p.Anzeige}“: bitte eine Zahl eingeben, z. B. 32,50.";
                        continue;
                    }

                    if (d < (p.Minimum ?? 0) || (p.Maximum is { } max && d > max))
                    {
                        fehler[p.Name] = $"„{p.Anzeige}“ muss zwischen {p.Minimum ?? 0:0.##} und {(p.Maximum is { } m ? m.ToString("0.##", CultureInfo.GetCultureInfo("de-DE")) : "unbegrenzt")} liegen.";
                        continue;
                    }

                    werte[p.Name] = (p.Typ == Parametertyp.Geld ? Math.Round(d, 2) : d).ToString(CultureInfo.InvariantCulture);
                    break;
                case Parametertyp.JaNein:
                    werte[p.Name] = roh is "true" or "ja" or "Ja" ? "true" : "false";
                    break;
                case Parametertyp.Auswahl:
                    if (p.Optionen is { } optionen && !optionen.Contains(roh))
                    {
                        fehler[p.Name] = $"„{p.Anzeige}“: erlaubt sind {string.Join(", ", optionen)}.";
                        continue;
                    }

                    werte[p.Name] = roh;
                    break;
                default:
                    werte[p.Name] = roh;
                    break;
            }
        }

        return (werte, fehler);
    }

    public static string Anzeige(HonorarParameter p, string? wert)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (wert is null)
        {
            return "–";
        }

        var de = CultureInfo.GetCultureInfo("de-DE");
        return p.Typ switch
        {
            Parametertyp.Geld when decimal.TryParse(wert, NumberStyles.Number, CultureInfo.InvariantCulture, out var g) => g.ToString("N2", de) + " €",
            Parametertyp.Zahl when decimal.TryParse(wert, NumberStyles.Number, CultureInfo.InvariantCulture, out var z) => z.ToString("0.##", de) + (p.Einheit is null ? "" : " " + p.Einheit),
            Parametertyp.JaNein => wert == "true" ? "ja" : "nein",
            _ => wert,
        };
    }
}

/// <summary>Gemeinsame Bausteine der Standardmodelle (Fahrtkosten, Rundung).</summary>
public static class Honorarbausteine
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static decimal Runden(decimal betrag) => Math.Round(betrag, 2, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<HonorarParameter> Fahrtkostenparameter { get; } =
    [
        new("fahrtkosten", "Fahrtkosten", Parametertyp.Auswahl, Standard: "keine", Optionen: ["keine", "pauschal", "km"],
            Hilfe: "Erstattung je stattgefundenem Termin als eigene Position: Pauschale oder Kilometer × Satz."),
        new("fahrtPauschale", "Fahrtkostenpauschale je Termin", Parametertyp.Geld, Einheit: "€"),
        new("kmSatz", "Satz je km", Parametertyp.Geld, Standard: "0.30", Einheit: "€/km", Maximum: 5),
        new("kmJeTermin", "km je Termin (Hin- und Rückweg)", Parametertyp.Zahl, Einheit: "km",
            Hilfe: "Gilt, wenn für den Ort keine Entfernung hinterlegt ist."),
    ];

    public static HonorarPosition? Fahrtkosten(HonorarKontext k, HonorarTermin t, List<string> hinweise)
    {
        ArgumentNullException.ThrowIfNull(k);
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(hinweise);
        switch (k.Text("fahrtkosten", "keine"))
        {
            case "pauschal":
                var p = k.Dezimal("fahrtPauschale");
                return p > 0 ? new($"Fahrtkostenpauschale {t.Kursnummer} am {t.Datum:dd.MM.yyyy}", Runden(p), t.TerminId, Menge: 1, Einzelpreis: p, Art: "Fahrtkosten") : null;
            case "km":
                var km = t.EntfernungKm ?? k.DezimalOderLeer("kmJeTermin");
                if (km is null)
                {
                    hinweise.Add($"{t.Datum:dd.MM.yyyy}: keine Entfernung bekannt – Fahrtkosten nicht berechnet.");
                    return null;
                }

                var satz = k.Dezimal("kmSatz", 0.30m);
                return new($"Fahrtkosten {t.Kursnummer} am {t.Datum:dd.MM.yyyy}: {km.Value.ToString("0.#", De)} km × {satz.ToString("N2", De)} €", Runden(km.Value * satz), t.TerminId,
                    Menge: km, Einzelpreis: satz, Art: "Fahrtkosten");
            default:
                return null;
        }
    }
}

/// <summary>Modell 1: Satz je Unterrichtseinheit (45 Min.); kurzfristig abgesagte Termine optional anteilig.</summary>
public sealed class HonorarProUe : IHonorarModell
{
    public string Kennung => "pro-ue";

    public string Anzeigename => "Honorar pro UE";

    public string Version => "1.0";

    public string Beschreibung => "Satz je Unterrichtseinheit (45 Minuten) für jeden stattgefundenen Termin. Kurzfristig abgesagte Termine werden optional anteilig vergütet.";

    public IReadOnlyList<HonorarParameter> Parameter { get; } =
    [
        new("satzJeUe", "Satz je UE", Parametertyp.Geld, Pflicht: true, Einheit: "€", Hilfe: "Honorar für 45 Minuten Unterricht.", Minimum: 0.01m),
        new("ausfallProzent", "Ausfallvergütung bei kurzfristiger Absage", Parametertyp.Zahl, Standard: "0", Einheit: "%", Maximum: 100,
            Hilfe: "Anteil des Honorars für Termine, die weniger als 48 Stunden vorher abgesagt wurden."),
        .. Honorarbausteine.Fahrtkostenparameter,
    ];

    public HonorarErgebnis Berechne(HonorarKontext kontext)
    {
        ArgumentNullException.ThrowIfNull(kontext);
        var satz = kontext.Dezimal("satzJeUe");
        var ausfall = kontext.Dezimal("ausfallProzent") / 100m;
        var positionen = new List<HonorarPosition>();
        var hinweise = new List<string>();
        foreach (var t in kontext.Termine.OrderBy(t => t.Beginn))
        {
            if (kontext.Abgerechnet(t.TerminId))
            {
                hinweise.Add($"{t.Datum:dd.MM.yyyy} {t.Kursnummer}: bereits abgerechnet, übersprungen.");
                continue;
            }

            switch (t.Status)
            {
                case Terminstatus.Stattgefunden:
                    positionen.Add(new($"{t.Kursnummer} am {t.Datum:dd.MM.yyyy}: {t.Ue.ToString("0.##", Honorarbausteine.De)} UE × {satz.ToString("N2", Honorarbausteine.De)} €",
                        Honorarbausteine.Runden(t.Ue * satz), t.TerminId, Menge: t.Ue, Einzelpreis: satz));
                    if (Honorarbausteine.Fahrtkosten(kontext, t, hinweise) is { } f)
                    {
                        positionen.Add(f);
                    }

                    break;
                case Terminstatus.KurzfristigAbgesagt when ausfall > 0:
                    positionen.Add(new($"{t.Kursnummer} am {t.Datum:dd.MM.yyyy}: kurzfristig abgesagt ({t.Ausfallgrund ?? "ohne Grund"}), {(ausfall * 100).ToString("0.##", Honorarbausteine.De)} % von {t.Ue.ToString("0.##", Honorarbausteine.De)} UE",
                        Honorarbausteine.Runden(t.Ue * satz * ausfall), t.TerminId, Menge: t.Ue * ausfall, Einzelpreis: satz, Art: "Ausfall"));
                    break;
                default:
                    hinweise.Add($"{t.Datum:dd.MM.yyyy} {t.Kursnummer}: ausgefallen, keine Vergütung.");
                    break;
            }
        }

        return new(positionen, hinweise);
    }
}

/// <summary>Modell 2: Basishonorar je Termin + Satz je UE + Satz je anwesender Person; optional Mindest- und Höchstbetrag je Termin.</summary>
public sealed class HonorarBasisUeTeilnehmende : IHonorarModell
{
    public string Kennung => "basis-ue-tn";

    public string Anzeigename => "Basis pro Termin + pro UE + pro Teilnehmer";

    public string Version => "1.0";

    public string Beschreibung => "Je stattgefundenem Termin: Basisbetrag + UE × Satz + anwesende Teilnehmende × Satz, begrenzt durch Mindest- und Höchstbetrag.";

    public IReadOnlyList<HonorarParameter> Parameter { get; } =
    [
        new("basisJeTermin", "Basis je Termin", Parametertyp.Geld, Standard: "0", Einheit: "€"),
        new("satzJeUe", "Satz je UE", Parametertyp.Geld, Standard: "0", Einheit: "€"),
        new("satzJeTeilnehmer", "Satz je anwesender Person", Parametertyp.Geld, Standard: "0", Einheit: "€",
            Hilfe: "Gezählt wird die Anwesenheitsliste des Termins (nur Anzahl, keine Namen)."),
        new("mindestJeTermin", "Mindestbetrag je Termin", Parametertyp.Geld, Einheit: "€"),
        new("hoechstJeTermin", "Höchstbetrag je Termin", Parametertyp.Geld, Einheit: "€"),
        .. Honorarbausteine.Fahrtkostenparameter,
    ];

    public HonorarErgebnis Berechne(HonorarKontext kontext)
    {
        ArgumentNullException.ThrowIfNull(kontext);
        var basis = kontext.Dezimal("basisJeTermin");
        var satzUe = kontext.Dezimal("satzJeUe");
        var satzTn = kontext.Dezimal("satzJeTeilnehmer");
        var min = kontext.DezimalOderLeer("mindestJeTermin");
        var max = kontext.DezimalOderLeer("hoechstJeTermin");
        var positionen = new List<HonorarPosition>();
        var hinweise = new List<string>();
        if (min is { } mi && max is { } ma && mi > ma)
        {
            hinweise.Add("Mindestbetrag liegt über dem Höchstbetrag – es gilt der Höchstbetrag.");
        }

        foreach (var t in kontext.Termine.OrderBy(t => t.Beginn))
        {
            if (kontext.Abgerechnet(t.TerminId))
            {
                hinweise.Add($"{t.Datum:dd.MM.yyyy} {t.Kursnummer}: bereits abgerechnet, übersprungen.");
                continue;
            }

            if (t.Status != Terminstatus.Stattgefunden)
            {
                hinweise.Add($"{t.Datum:dd.MM.yyyy} {t.Kursnummer}: {(t.Status == Terminstatus.KurzfristigAbgesagt ? "kurzfristig abgesagt" : "ausgefallen")}, keine Vergütung.");
                continue;
            }

            var anwesend = t.Anwesend ?? 0;
            if (t.Anwesend is null && satzTn > 0)
            {
                hinweise.Add($"{t.Datum:dd.MM.yyyy} {t.Kursnummer}: Anwesenheit nicht erfasst – 0 Teilnehmende angesetzt.");
            }

            var roh = basis + (t.Ue * satzUe) + (anwesend * satzTn);
            var betrag = roh;
            var grenze = "";
            if (min is { } untergrenze && betrag < untergrenze)
            {
                betrag = untergrenze;
                grenze = " (Mindestbetrag)";
            }

            if (max is { } obergrenze && betrag > obergrenze)
            {
                betrag = obergrenze;
                grenze = " (Höchstbetrag)";
            }

            var de = Honorarbausteine.De;
            positionen.Add(new($"{t.Kursnummer} am {t.Datum:dd.MM.yyyy}: Basis {basis.ToString("N2", de)} € + {t.Ue.ToString("0.##", de)} UE × {satzUe.ToString("N2", de)} € + {anwesend} TN × {satzTn.ToString("N2", de)} €{grenze}",
                Honorarbausteine.Runden(betrag), t.TerminId));
            if (Honorarbausteine.Fahrtkosten(kontext, t, hinweise) is { } f)
            {
                positionen.Add(f);
            }
        }

        return new(positionen, hinweise);
    }
}
