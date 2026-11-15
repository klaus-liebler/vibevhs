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
        foreach (var schluessel in benoetigt.Distinct())
        {
            if (person.QualifikationAm(schluessel, tag, bekanntAm) is not null)
            {
                continue;
            }

            var merkmal = katalog.GetValueOrDefault(schluessel);
            var name = merkmal?.Name ?? schluessel;
            if (merkmal is { Gesetzlich: false } && ausnahmeListe.Any(a => a.Schluessel == schluessel))
            {
                mitAusnahme = true;
                continue;
            }

            fehlend.Add(schluessel);
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

        var moeglich = fehlend.All(s => katalog.GetValueOrDefault(s) is { Gesetzlich: false });
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
