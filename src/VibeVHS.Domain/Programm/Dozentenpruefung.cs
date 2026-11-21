using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Programm;

/// <summary>
/// Ausnahme der Fachbereichsleitung: Eine Person darf in einem Kurs eingesetzt werden, obwohl ein (nicht gesetzliches)
/// Merkmal fehlt. Begründung und Erteilende werden festgehalten und protokolliert.
/// </summary>
public sealed class Qualifikationsausnahme
{
    private Qualifikationsausnahme()
    {
        Schluessel = string.Empty;
        Begruendung = string.Empty;
        ErteiltVon = string.Empty;
    }

    public Qualifikationsausnahme(Guid kursId, Guid personId, Merkmal merkmal, string begruendung, string erteiltVon, DateTimeOffset erteiltAm)
    {
        ArgumentNullException.ThrowIfNull(merkmal);
        ArgumentException.ThrowIfNullOrWhiteSpace(begruendung);
        if (merkmal.Gesetzlich)
        {
            throw new InvalidOperationException($"„{merkmal.Name}“ ist gesetzlich vorgeschrieben; eine Ausnahme ist nicht zulässig.");
        }

        Id = Ids.Neu();
        KursId = kursId;
        PersonId = personId;
        Schluessel = merkmal.Schluessel;
        Begruendung = begruendung.Trim();
        ErteiltVon = erteiltVon;
        ErteiltAm = erteiltAm;
    }

    public Guid Id { get; private set; }

    public Guid KursId { get; private set; }

    public Guid PersonId { get; private set; }

    public string Schluessel { get; private set; }

    public string Begruendung { get; private set; }

    public string ErteiltVon { get; private set; }

    public DateTimeOffset ErteiltAm { get; private set; }
}

public enum Eignungscode
{
    Geeignet,

    /// <summary>Geeignet nur dank einer Ausnahme der Fachbereichsleitung.</summary>
    Ausnahme,

    MerkmalFehlt,

    /// <summary>Die Qualifikation war vorhanden, ist am Tag aber abgelaufen.</summary>
    Abgelaufen,

    /// <summary>Die Qualifikation gilt erst ab einem späteren Tag.</summary>
    NochNichtGueltig,
}

/// <summary>Eignung einer Person für einen Einsatz an einem Tag.</summary>
public sealed record Tageseignung(DateOnly Tag, Eignungscode Code, string? Begruendung, IReadOnlyList<string> Fehlend, bool AusnahmeMoeglich)
{
    public bool Geeignet => Code is Eignungscode.Geeignet or Eignungscode.Ausnahme;
}

/// <summary>Eignung über mehrere Veranstaltungstage, z. B. eine ganze Serie.</summary>
public sealed record Dozenteneignung(Guid PersonId, string Name, IReadOnlyList<Tageseignung> Tage)
{
    public bool Geeignet => Tage.All(t => t.Geeignet);

    public Tageseignung? ErsterFehltag => Tage.Where(t => !t.Geeignet).OrderBy(t => t.Tag).FirstOrDefault();

    public bool AusnahmeMoeglich => Tage.Where(t => !t.Geeignet).All(t => t.AusnahmeMoeglich);

    public bool MitAusnahme => Tage.Any(t => t.Code == Eignungscode.Ausnahme);

    /// <summary>Text für Tooltips: Grund am ersten Fehltag und wie viele Termine betroffen sind (z. B. Ablauf mitten in der Serie).</summary>
    public string? Begruendung
    {
        get
        {
            if (ErsterFehltag is not { } f)
            {
                return null;
            }

            var betroffen = Tage.Count(t => !t.Geeignet);
            var teil = betroffen < Tage.Count ? $" Betroffen sind {betroffen} von {Tage.Count} Terminen ab {f.Tag:dd.MM.yyyy}." : string.Empty;
            var ausnahme = AusnahmeMoeglich ? " Die Fachbereichsleitung kann mit Begründung eine Ausnahme erteilen." : " Eine Ausnahme ist nicht zulässig (gesetzlich vorgeschrieben).";
            return $"{Name}: {f.Begruendung}{teil}{ausnahme}";
        }
    }
}

/// <summary>
/// Ein Prüfdienst für Dozierende: Hat die Person am Veranstaltungstag alle verlangten Qualifikationen
/// (oder eine Ausnahme der Fachbereichsleitung)? Liefert je Tag einen Begründungscode.
/// </summary>
public static class Dozentenpruefung
{
    public static Tageseignung AmTag(Person person, IEnumerable<string> benoetigt, IReadOnlyDictionary<string, Merkmal> katalog, DateOnly tag,
        DateTimeOffset bekanntAm, IEnumerable<Qualifikationsausnahme>? ausnahmen = null)
    {
        ArgumentNullException.ThrowIfNull(person);
        var fehlend = new List<string>();
        var gruende = new List<string>();
        var code = Eignungscode.Geeignet;
        var mitAusnahme = false;
        var ausnahmeListe = ausnahmen?.Where(a => a.PersonId == person.Id).ToList() ?? [];
        foreach (var anforderung in benoetigt.Distinct())
        {
            var (schluessel, wert) = Anforderung.Zerlegen(anforderung);
            var vorhanden = person.QualifikationAm(schluessel, tag, bekanntAm);
            var abweichung = vorhanden is null ? null : Anforderung.Abweichung(anforderung, katalog.GetValueOrDefault(schluessel), vorhanden.WertText);
            if (vorhanden is not null && abweichung is null)
            {
                continue;
            }

            var merkmal = katalog.GetValueOrDefault(schluessel);
            var name = Anforderung.Anzeige(anforderung, katalog);
            if (merkmal is { Gesetzlich: false } && ausnahmeListe.Any(a => a.Schluessel == schluessel || a.Schluessel == anforderung))
            {
                mitAusnahme = true;
                continue;
            }

            fehlend.Add(anforderung);
            if (vorhanden is not null)
            {
                // Freigabe vorhanden, aber für einen anderen Wert (z. B. Spanisch statt Englisch)
                code = Schlechter(code, Eignungscode.MerkmalFehlt);
                gruende.Add(abweichung!);
                continue;
            }

            var verlauf = person.Qualifikationen.Where(q => q.Schluessel == schluessel && q.IstAktuellerWissensstand && !q.IstEndemarke).ToList();
            var frueher = verlauf.Where(q => q.GueltigBis is { } b && b <= tag).OrderByDescending(q => q.GueltigBis).FirstOrDefault();
            var spaeter = verlauf.Where(q => q.GueltigVon > tag).OrderBy(q => q.GueltigVon).FirstOrDefault();
            if (frueher is not null && spaeter is null)
            {
                code = Schlechter(code, Eignungscode.Abgelaufen);
                gruende.Add($"„{name}“ abgelaufen am {frueher.GueltigBisEinschliesslich:dd.MM.yyyy}.");
            }
            else if (spaeter is not null)
            {
                code = Schlechter(code, Eignungscode.NochNichtGueltig);
                gruende.Add($"„{name}“ gilt erst ab {spaeter.GueltigVon:dd.MM.yyyy}.");
            }
            else
            {
                code = Schlechter(code, Eignungscode.MerkmalFehlt);
                gruende.Add($"„{name}“ fehlt.");
            }
        }

        if (fehlend.Count == 0)
        {
            return new Tageseignung(tag, mitAusnahme ? Eignungscode.Ausnahme : Eignungscode.Geeignet,
                mitAusnahme ? "Eingesetzt mit Ausnahme der Fachbereichsleitung." : null, [], true);
        }

        var moeglich = fehlend.All(s => katalog.GetValueOrDefault(Anforderung.Zerlegen(s).Schluessel) is { Gesetzlich: false });
        return new Tageseignung(tag, code, string.Join(" ", gruende), fehlend, moeglich);
    }

    public static Dozenteneignung Pruefen(Person person, IEnumerable<string> benoetigt, IReadOnlyDictionary<string, Merkmal> katalog,
        IEnumerable<DateOnly> tage, DateTimeOffset bekanntAm, IEnumerable<Qualifikationsausnahme>? ausnahmen = null)
    {
        ArgumentNullException.ThrowIfNull(person);
        var liste = benoetigt.ToList();
        var ausnahmeListe = ausnahmen?.ToList();
        var ergebnis = tage.Distinct().Order().Select(t => AmTag(person, liste, katalog, t, bekanntAm, ausnahmeListe)).ToList();
        return new Dozenteneignung(person.Id, person.Anzeigename, ergebnis);
    }

    private static Eignungscode Schlechter(Eignungscode bisher, Eignungscode neu) => bisher == Eignungscode.Geeignet ? neu : bisher;
}

/// <summary>
/// Anforderung an Dozierende: ein Merkmalsschlüssel, optional mit verlangtem Wert („sprachfreigabe=Englisch“).
/// Ohne Wert genügt die Qualifikation mit beliebigem Wert; mit Wert muss er unter den (ggf. mehreren, kommagetrennten) Werten sein.
/// </summary>
public static class Anforderung
{
    private static readonly char[] Trenner = [',', ';', '/'];

    /// <summary>
    /// Bedingung lesen: „schluessel“ (vorhanden), „schluessel=Wert“ (Auswahl enthält Wert) oder „schluessel&gt;=12“ (Ganzzahl mindestens).
    /// </summary>
    public static Bedingung Lesen(string anforderung)
    {
        ArgumentNullException.ThrowIfNull(anforderung);
        var m = anforderung.IndexOf(">=", StringComparison.Ordinal);
        if (m > 0)
        {
            return new Bedingung(anforderung[..m].Trim(), Vergleich.Mindestens, anforderung[(m + 2)..].Trim());
        }

        var i = anforderung.IndexOf('=', StringComparison.Ordinal);
        return i < 0 ? new Bedingung(anforderung.Trim(), Vergleich.Vorhanden, null) : new Bedingung(anforderung[..i].Trim(), Vergleich.Gleich, anforderung[(i + 1)..].Trim());
    }

    /// <summary>Schlüssel und (bei „=“) verlangter Wert; „&gt;=“-Bedingungen liefern keinen Wert.</summary>
    public static (string Schluessel, string? Wert) Zerlegen(string anforderung)
    {
        var b = Lesen(anforderung);
        return (b.Schluessel, b.Vergleich == Vergleich.Gleich ? b.Wert : null);
    }

    public static string Basis(string anforderung) => Lesen(anforderung).Schluessel;

    public static string Mindestens(string schluessel, int wert) => $"{schluessel}>={wert.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Ist die Bedingung mit dem vorhandenen Wert erfüllt? Liefert null oder die konkrete Abweichung,
    /// z. B. „nur 8 statt mind. 12 PC-Arbeitsplätze“ oder „„Tafel“ ist Kreide, nicht Smartboard“.
    /// </summary>
    public static string? Abweichung(string anforderung, Merkmal? merkmal, string? vorhandenerWert)
    {
        var b = Lesen(anforderung);
        var name = merkmal?.Name ?? b.Schluessel;
        switch (b.Vergleich)
        {
            case Vergleich.Gleich when !WertPasst(vorhandenerWert, b.Wert):
                return $"„{name}“ liegt nur für {vorhandenerWert ?? "(ohne Angabe)"} vor, nicht für {b.Wert}.";
            case Vergleich.Mindestens:
                var noetig = int.TryParse(b.Wert, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
                var ist = int.TryParse(vorhandenerWert, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
                if (ist is { } x && x >= noetig)
                {
                    return null;
                }

                return merkmal?.Einheit is { } e
                    ? $"„{name}“: nur {ist?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "ohne Angabe"} statt mind. {noetig} {e}."
                    : $"nur {ist?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "ohne Angabe"} statt mind. {noetig} {name}.";
            default:
                return null;
        }
    }

    /// <summary>Bedingung prüfen, bevor eine Kursart sie stellt: passt sie zum Typ des Merkmals?</summary>
    public static string Pruefen(string anforderung, IReadOnlyDictionary<string, Merkmal> katalog)
    {
        var b = Lesen(anforderung);
        var m = katalog.GetValueOrDefault(b.Schluessel) ?? throw new ArgumentException($"Merkmal „{b.Schluessel}“ steht nicht im Katalog.");
        if (!m.AlsBedingung)
        {
            throw new ArgumentException($"„{m.Name}“ ist ein Textmerkmal und kann keine Bedingung sein.");
        }

        switch (b.Vergleich)
        {
            case Vergleich.Mindestens when m.Typ != Merkmalstyp.Ganzzahl:
                throw new ArgumentException($"„mindestens“ geht nur bei Zahlenmerkmalen, „{m.Name}“ ist {MerkmalstypAnzeige.Text(m.Typ)}.");
            case Vergleich.Mindestens when !int.TryParse(b.Wert, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _):
                throw new ArgumentException($"Für „{m.Name}“ bitte eine ganze Zahl als Mindestwert angeben.");
            case Vergleich.Gleich when m.Typ != Merkmalstyp.Auswahl:
                throw new ArgumentException($"Ein bestimmter Wert geht nur bei Auswahlmerkmalen, „{m.Name}“ ist {MerkmalstypAnzeige.Text(m.Typ)}.");
            case Vergleich.Gleich when !m.Werte.Contains(b.Wert, StringComparer.OrdinalIgnoreCase):
                throw new ArgumentException($"„{b.Wert}“ ist kein Wert von „{m.Name}“.");
        }

        return anforderung.Trim();
    }

    public static string Bilden(string schluessel, string? wert) => string.IsNullOrWhiteSpace(wert) ? schluessel : $"{schluessel}={wert.Trim()}";

    /// <summary>Werte trennen an „,“, „;“ und „/“ – nicht innerhalb von Klammern (z. B. „Deutsch (DaF/DaZ)“).</summary>
    public static IReadOnlyList<string> Werte(string? wertText)
    {
        var werte = new List<string>();
        var tiefe = 0;
        var start = 0;
        var text = wertText ?? "";
        for (var i = 0; i <= text.Length; i++)
        {
            var c = i < text.Length ? text[i] : ',';
            tiefe += c switch { '(' => 1, ')' when tiefe > 0 => -1, _ => 0 };
            if (tiefe == 0 && Array.IndexOf(Trenner, c) >= 0)
            {
                if (text[start..Math.Min(i, text.Length)].Trim() is { Length: > 0 } wert)
                {
                    werte.Add(wert);
                }

                start = i + 1;
            }
        }

        return werte;
    }

    public static bool WertPasst(string? vorhanden, string? verlangt)
        => verlangt is null || Werte(vorhanden).Contains(verlangt, StringComparer.OrdinalIgnoreCase);

    /// <summary>„Lehrfreigabe Sprachkurs: Englisch“, „PC-Arbeitsplätze: mind. 12“.</summary>
    public static string Anzeige(string anforderung, IReadOnlyDictionary<string, Merkmal> katalog)
    {
        var b = Lesen(anforderung);
        var m = katalog.GetValueOrDefault(b.Schluessel);
        var name = m?.Name ?? b.Schluessel;
        return b.Vergleich switch
        {
            Vergleich.Gleich => $"{name}: {b.Wert}",
            Vergleich.Mindestens => $"{name}: mind. {b.Wert}{(m?.Einheit is { } e ? $" {e}" : "")}",
            _ => name,
        };
    }
}

public enum Vergleich
{
    Vorhanden,
    Gleich,
    Mindestens,
}

/// <summary>Bedingung einer Kursart an ein Merkmal (Ort oder Person).</summary>
public sealed record Bedingung(string Schluessel, Vergleich Vergleich, string? Wert);

/// <summary>
/// Lehrfreigabe für Sprachkurse (Eignungsprüfung bzw. Freigabe der vhs je Sprache). Verlangt die Kursart die Freigabe ohne Sprache,
/// bestimmt der Kurs die Sprache aus seinem Titel („Englisch A2 – Konversation“ → Englisch).
/// </summary>
public static class Sprachfreigabe
{
    public const string Schluessel = "sprachfreigabe";

    public const string DeutschWert = "Deutsch (DaF/DaZ)";

    public static IReadOnlyList<string> Sprachen { get; } =
    [
        "Arabisch", "Chinesisch", DeutschWert, "Englisch", "Französisch", "Griechisch", "Italienisch", "Japanisch", "Niederländisch", "Norwegisch",
        "Plattdeutsch", "Polnisch", "Portugiesisch", "Russisch", "Schwedisch", "Spanisch", "Türkisch",
    ];

    /// <summary>Sprache aus dem Kurstitel (erstes passendes Wort), sonst null.</summary>
    public static string? AusTitel(string titel)
    {
        foreach (var wort in (titel ?? "").Split([' ', '-', '–', ':', ',', '(', ')', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (wort.Equals("Deutsch", StringComparison.OrdinalIgnoreCase) || wort.Equals("DaF", StringComparison.OrdinalIgnoreCase)
                || wort.Equals("DaZ", StringComparison.OrdinalIgnoreCase))
            {
                return DeutschWert;
            }

            var treffer = Sprachen.FirstOrDefault(s => s.Equals(wort, StringComparison.OrdinalIgnoreCase));
            if (treffer is not null)
            {
                return treffer;
            }
        }

        return null;
    }

    /// <summary>Ersetzt eine allgemeine Sprachfreigabe durch die Freigabe für die Sprache des Kurses (falls erkennbar und noch nicht gesetzt).</summary>
    public static IReadOnlyList<string> Konkretisieren(IEnumerable<string> anforderungen, string titel)
    {
        var liste = anforderungen.Distinct().ToList();
        if (!liste.Contains(Schluessel))
        {
            return liste;
        }

        var konkret = liste.Any(a => a.StartsWith(Schluessel + "=", StringComparison.Ordinal));
        var sprache = AusTitel(titel);
        if (konkret || sprache is not null)
        {
            liste.Remove(Schluessel);
        }

        if (!konkret && sprache is not null)
        {
            liste.Add(Anforderung.Bilden(Schluessel, sprache));
        }

        return liste;
    }
}
