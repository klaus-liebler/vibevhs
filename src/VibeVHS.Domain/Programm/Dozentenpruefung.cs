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
            if (vorhanden is not null && Anforderung.WertPasst(vorhanden.WertText, wert))
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
                gruende.Add($"„{merkmal?.Name ?? schluessel}“ liegt nur für {vorhanden.WertText ?? "(ohne Angabe)"} vor, nicht für {wert}.");
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

    public static (string Schluessel, string? Wert) Zerlegen(string anforderung)
    {
        ArgumentNullException.ThrowIfNull(anforderung);
        var i = anforderung.IndexOf('=', StringComparison.Ordinal);
        return i < 0 ? (anforderung, null) : (anforderung[..i], anforderung[(i + 1)..].Trim());
    }

    public static string Basis(string anforderung) => Zerlegen(anforderung).Schluessel;

    public static string Bilden(string schluessel, string? wert) => string.IsNullOrWhiteSpace(wert) ? schluessel : $"{schluessel}={wert.Trim()}";

    public static IReadOnlyList<string> Werte(string? wertText)
        => (wertText ?? "").Split(Trenner, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool WertPasst(string? vorhanden, string? verlangt)
        => verlangt is null || Werte(vorhanden).Contains(verlangt, StringComparer.OrdinalIgnoreCase);

    /// <summary>„Lehrfreigabe Sprachkurs: Englisch“.</summary>
    public static string Anzeige(string anforderung, IReadOnlyDictionary<string, Merkmal> katalog)
    {
        var (schluessel, wert) = Zerlegen(anforderung);
        var name = katalog.GetValueOrDefault(schluessel)?.Name ?? schluessel;
        return wert is null ? name : $"{name}: {wert}";
    }
}

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
