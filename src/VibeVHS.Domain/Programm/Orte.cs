using System.Globalization;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Programm;

public enum Ortsart
{
    Liegenschaft,
    Gebaeude,
    Raum,
    AllgemeinerOrt,
    VirtuellerRaum,
}

/// <summary>Wofür ein Raum genutzt wird; je Nutzungsart gilt eine eigene Kapazität.</summary>
public enum Nutzungsart
{
    Seminar,
    Vortrag,
    Pruefung,
    Integrationskurs,
    PcKurs,
    Bewegung,
    Kreativ,
    Kueche,
    Online,
    Exkursion,
}

public static class NutzungsartAnzeige
{
    public static string Text(Nutzungsart n) => n switch
    {
        Nutzungsart.Pruefung => "Prüfung",
        Nutzungsart.PcKurs => "PC-Kurs",
        Nutzungsart.Kueche => "Küche",
        _ => n.ToString(),
    };
}

/// <summary>Woran ein Merkmal hängt: an Orten (Ausstattung, Zulassung) oder an Personen (Qualifikationen von Dozierenden).</summary>
public enum MerkmalBezug
{
    Ort,
    Person,
}

/// <summary>Werttyp eines Merkmals: bestimmt Erfassungsfeld, Prüfung des Werts und mögliche Bedingungen der Kursarten.</summary>
public enum Merkmalstyp
{
    /// <summary>Vorhanden oder nicht (optional mit Bemerkung, z. B. „1. OG ohne Aufzug“).</summary>
    JaNein,

    /// <summary>Einer (oder mit <see cref="Merkmal.Mehrfach"/> mehrere) der festgelegten Werte, z. B. Tafel: Kreide/Whiteboard/Smartboard.</summary>
    Auswahl,

    /// <summary>Ganze Zahl mit optionaler Einheit und Grenzen, z. B. PC-Arbeitsplätze, Fläche in m².</summary>
    Ganzzahl,

    /// <summary>Freier Text, z. B. Hinweise zum Zugang; nicht als Bedingung verwendbar.</summary>
    Text,
}

public static class MerkmalstypAnzeige
{
    public static string Text(Merkmalstyp t) => t switch
    {
        Merkmalstyp.JaNein => "Ja/Nein",
        Merkmalstyp.Auswahl => "Auswahl",
        Merkmalstyp.Ganzzahl => "Ganzzahl",
        _ => "Text",
    };
}

/// <summary>Eintrag im gemeinsamen, pflegbaren Merkmalskatalog (z. B. "barrierefrei", "BAMF-Kursort", "BAMF-Lehrkraftzulassung").</summary>
public sealed class Merkmal
{
    public const int TextMaxLaenge = 500;

    private Merkmal()
    {
        Schluessel = string.Empty;
        Name = string.Empty;
        Gruppe = string.Empty;
    }

    public Merkmal(string schluessel, string name, string gruppe, bool vererbbar = true, bool nachweisPflicht = false, string? ausschlussgruppe = null,
        MerkmalBezug bezug = MerkmalBezug.Ort, bool gesetzlich = false, IEnumerable<string>? werte = null, int vorlaufTage = 60, bool mehrfach = false,
        Merkmalstyp? typ = null, string? einheit = null, int? min = null, int? max = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schluessel);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Schluessel = schluessel.Trim().ToLowerInvariant();
        Name = name.Trim();
        Gruppe = gruppe;
        Vererbbar = bezug == MerkmalBezug.Ort && vererbbar;
        NachweisPflicht = nachweisPflicht;
        Ausschlussgruppe = ausschlussgruppe;
        Bezug = bezug;
        Gesetzlich = gesetzlich;
        Werte = werte?.ToList() ?? [];
        VorlaufTage = vorlaufTage;
        Mehrfach = mehrfach && Werte.Count > 0;
        // Ohne Angabe: mit Werten eine Auswahl, sonst Ja/Nein (bisheriges Verhalten).
        TypSetzen(typ ?? (Werte.Count > 0 ? Merkmalstyp.Auswahl : Merkmalstyp.JaNein), Werte, Mehrfach, einheit, min, max);
    }

    /// <summary>Werttyp; nach der ersten Verwendung nicht mehr änderbar (<see cref="TypFestlegen"/>).</summary>
    public Merkmalstyp Typ { get; private set; }

    /// <summary>Einheit bei Ganzzahlen, z. B. „Plätze“ oder „m²“.</summary>
    public string? Einheit { get; private set; }

    public int? Min { get; private set; }

    public int? Max { get; private set; }

    /// <summary>Auswahlwerte, die nicht mehr neu vergeben werden (bestehende Einträge bleiben gültig).</summary>
    /// <remarks>Gespeichert als eigene Tabelle (je Eintrag eine Zeile, siehe <see cref="Gemeinsam.Listenwert{T}"/>).</remarks>
    public List<string> InaktiveWerte
    {
        get => Gemeinsam.Listenwerte.Werte(_inaktiveWerte);
        private set => _inaktiveWerte = Gemeinsam.Listenwerte.Von(value);
    }

    private List<Gemeinsam.Listenwert<string>> _inaktiveWerte = [];

    public IReadOnlyList<string> AktiveWerte => Werte.Where(w => !InaktiveWerte.Contains(w, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>Kann eine Kursart darauf eine Bedingung stellen? (Text nicht.)</summary>
    public bool AlsBedingung => Typ != Merkmalstyp.Text;

    /// <summary>Typ und Typangaben festlegen. Nach der ersten Verwendung (Eintrag an Ort/Person oder Bedingung einer Kursart) ist ein anderer Typ nicht mehr möglich.</summary>
    public void TypFestlegen(Merkmalstyp typ, bool verwendet, IEnumerable<string>? werte = null, bool mehrfach = false, string? einheit = null, int? min = null, int? max = null)
    {
        if (verwendet && typ != Typ)
        {
            throw new InvalidOperationException($"Der Typ von „{Name}“ ist nicht mehr änderbar, weil das Merkmal schon verwendet wird. Möglich: Auswahlwerte ergänzen oder deaktivieren.");
        }

        if (verwendet && Typ == Merkmalstyp.Auswahl && werte is not null && Werte.Any(w => !werte.Contains(w, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Verwendete Auswahlwerte können nicht gelöscht, nur deaktiviert werden.");
        }

        TypSetzen(typ, werte?.ToList() ?? Werte, mehrfach, einheit, min, max);
    }

    private void TypSetzen(Merkmalstyp typ, List<string> werte, bool mehrfach, string? einheit, int? min, int? max)
    {
        var bereinigt = werte.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (typ == Merkmalstyp.Auswahl && bereinigt.Count == 0)
        {
            throw new ArgumentException($"Für die Auswahl bei „{Name}“ bitte mindestens einen Wert angeben.");
        }

        if (typ == Merkmalstyp.Ganzzahl && min is { } a && max is { } b && a > b)
        {
            throw new ArgumentException("Das Minimum liegt über dem Maximum.");
        }

        Typ = typ;
        Werte = typ == Merkmalstyp.Auswahl ? bereinigt : [];
        Mehrfach = typ == Merkmalstyp.Auswahl && mehrfach;
        InaktiveWerte = typ == Merkmalstyp.Auswahl ? InaktiveWerte.Where(w => Werte.Contains(w, StringComparer.OrdinalIgnoreCase)).ToList() : [];
        Einheit = typ == Merkmalstyp.Ganzzahl && !string.IsNullOrWhiteSpace(einheit) ? einheit.Trim() : null;
        Min = typ == Merkmalstyp.Ganzzahl ? min : null;
        Max = typ == Merkmalstyp.Ganzzahl ? max : null;
    }

    /// <summary>Auswahlwert ergänzen (oder einen deaktivierten wieder aktivieren).</summary>
    public void WertErgaenzen(string wert)
    {
        if (Typ != Merkmalstyp.Auswahl)
        {
            throw new InvalidOperationException($"„{Name}“ ist keine Auswahl.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(wert);
        var w = wert.Trim();
        InaktiveWerte = InaktiveWerte.Where(x => !x.Equals(w, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!Werte.Contains(w, StringComparer.OrdinalIgnoreCase))
        {
            Werte = [.. Werte, w];
        }
    }

    /// <summary>Auswahlwert deaktivieren: nicht mehr neu wählbar; bestehende Einträge bleiben gültig.</summary>
    public void WertDeaktivieren(string wert)
    {
        var w = Werte.FirstOrDefault(x => x.Equals(wert?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"„{wert}“ ist kein Wert von „{Name}“.");
        if (AktiveWerte.Count <= 1 && !InaktiveWerte.Contains(w))
        {
            throw new InvalidOperationException("Mindestens ein Auswahlwert muss aktiv bleiben.");
        }

        if (!InaktiveWerte.Contains(w))
        {
            InaktiveWerte = [.. InaktiveWerte, w];
        }
    }

    /// <summary>
    /// Prüft und vereinheitlicht einen Wert zum Eintragen: Ja/Nein mit optionaler Bemerkung, Auswahl nur aus den aktiven Werten
    /// (bei Mehrfachauswahl kommagetrennt), Ganzzahl innerhalb der Grenzen, Text nicht leer.
    /// </summary>
    public string? WertPruefen(string? wert)
    {
        var w = string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
        switch (Typ)
        {
            case Merkmalstyp.JaNein:
                return w;
            case Merkmalstyp.Auswahl:
                var gewaehlt = Mehrfach ? Anforderung.Werte(w) : w is null ? [] : [w];
                if (gewaehlt.Count == 0)
                {
                    throw new ArgumentException($"Bitte für „{Name}“ {(Mehrfach ? "mindestens einen" : "einen")} der Werte wählen: {string.Join(", ", AktiveWerte)}.");
                }

                var kanonisch = new List<string>();
                foreach (var g in gewaehlt)
                {
                    kanonisch.Add(AktiveWerte.FirstOrDefault(a => a.Equals(g, StringComparison.OrdinalIgnoreCase))
                                  ?? throw new ArgumentException($"„{g}“ ist für „{Name}“ nicht wählbar. Möglich: {string.Join(", ", AktiveWerte)}."));
                }

                return string.Join(", ", kanonisch.Distinct());
            case Merkmalstyp.Ganzzahl:
                if (!int.TryParse(w, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var zahl))
                {
                    throw new ArgumentException($"Für „{Name}“ bitte eine ganze Zahl eingeben{(Einheit is null ? "" : $" ({Einheit})")}.");
                }

                if ((Min is { } min && zahl < min) || (Max is { } max && zahl > max))
                {
                    throw new ArgumentException($"„{Name}“: erlaubt sind {Min?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "beliebig"} bis {Max?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "beliebig"}{(Einheit is null ? "" : $" {Einheit}")}.");
                }

                return zahl.ToString(System.Globalization.CultureInfo.InvariantCulture);
            default:
                if (w is null)
                {
                    throw new ArgumentException($"Bitte für „{Name}“ einen Text eingeben.");
                }

                return w.Length <= TextMaxLaenge ? w : throw new ArgumentException($"Der Text für „{Name}“ ist zu lang (höchstens {TextMaxLaenge} Zeichen).");
        }
    }

    /// <summary>Wert zur Anzeige, z. B. „12 Plätze“, „ja“ oder die Bemerkung.</summary>
    public string WertAnzeige(string? wert) => Typ switch
    {
        Merkmalstyp.JaNein => wert ?? "ja",
        Merkmalstyp.Ganzzahl => wert is null ? "–" : $"{wert}{(Einheit is null ? "" : $" {Einheit}")}",
        _ => wert ?? "–",
    };

    public MerkmalBezug Bezug { get; private set; }

    /// <summary>Mehrere Werte zugleich möglich (z. B. Lehrfreigabe für Englisch und Spanisch); gespeichert kommagetrennt.</summary>
    public bool Mehrfach { get; private set; }

    /// <summary>Gesetzlich gefordert (z. B. BAMF-Lehrkraftzulassung): keine Ausnahme durch die Fachbereichsleitung möglich.</summary>
    public bool Gesetzlich { get; private set; }

    /// <summary>Erlaubte Werte (z. B. Zulassungsart: direkt / mit Auflagen / nach Zusatzqualifizierung); leer = freier oder kein Wert.</summary>
    /// <remarks>Gespeichert als eigene Tabelle (je Eintrag eine Zeile, siehe <see cref="Gemeinsam.Listenwert{T}"/>).</remarks>
    public List<string> Werte
    {
        get => Gemeinsam.Listenwerte.Werte(_werte);
        private set => _werte = Gemeinsam.Listenwerte.Von(value);
    }

    private List<Gemeinsam.Listenwert<string>> _werte = [];

    /// <summary>So viele Tage vor Ablauf entsteht eine Aufgabe.</summary>
    public int VorlaufTage { get; private set; } = 60;

    public string Schluessel { get; private set; }

    public string Name { get; private set; }

    /// <summary>Zugänglichkeit, Zulassung, Ausstattung, Organisation.</summary>
    public string Gruppe { get; private set; }

    /// <summary>Gilt ein Merkmal des Gebäudes auch für seine Räume?</summary>
    public bool Vererbbar { get; private set; }

    /// <summary>Muss beim Eintragen ein Nachweis angegeben werden (z. B. Zulassung)?</summary>
    public bool NachweisPflicht { get; private set; }

    /// <summary>Merkmale derselben Gruppe schließen sich aus; ein eigenes überschreibt ein geerbtes (z. B. barrierefrei / nicht barrierefrei).</summary>
    public string? Ausschlussgruppe { get; private set; }
}

/// <summary>Merkmal eines Orts mit Wert, Nachweis und Gültigkeit (bitemporal).</summary>
public sealed class OrtMerkmal : BitemporaleVersion<OrtMerkmal>
{
    private OrtMerkmal()
    {
        Schluessel = string.Empty;
    }

    public OrtMerkmal(string schluessel, string? wert, string? nachweis, DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        Schluessel = schluessel;
        WertText = string.IsNullOrWhiteSpace(wert) ? null : wert.Trim();
        Nachweis = string.IsNullOrWhiteSpace(nachweis) ? null : nachweis.Trim();
    }

    public string Schluessel { get; private set; }

    /// <summary>z. B. "12" bei PC-Arbeitsplätzen, "eine Stufe (4 cm)".</summary>
    public string? WertText { get; private set; }

    public string? Nachweis { get; private set; }

    public override string Datenart => "Merkmale";

    public override string Feld => Schluessel;

    public override string Gegenstand => Schluessel;

    public override string Wert => IstEndemarke ? "(entfernt)" : string.Join(" · ", new[] { WertText, Nachweis }.Where(t => t is not null));

    protected internal override OrtMerkmal KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Schluessel, WertText, Nachweis, gueltigVon, gueltigBis, erfassung);
}

public sealed record OrtKapazitaet(Nutzungsart Nutzungsart, int Plaetze);

/// <summary>Ort: Liegenschaft → Gebäude → Raum, außerdem allgemeine Orte (Treffpunkt) und virtuelle Räume.</summary>
public sealed class Ort
{
    private readonly List<OrtMerkmal> _merkmale = [];

    private Ort()
    {
        Name = string.Empty;
    }

    public Ort(Ortsart art, string name, Guid? elternId = null, string? kurzname = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = Ids.Neu();
        Art = art;
        Name = name.Trim();
        Kurzname = string.IsNullOrWhiteSpace(kurzname) ? null : kurzname.Trim();
        ElternId = elternId;
    }

    public Guid Id { get; private set; }

    public Ortsart Art { get; private set; }

    public string Name { get; private set; }

    /// <summary>Kurze Bezeichnung für Pläne, z. B. "R 2.14".</summary>
    public string? Kurzname { get; private set; }

    public Guid? ElternId { get; private set; }

    public string? Strasse { get; private set; }

    public string? Postleitzahl { get; private set; }

    public string? Stadt { get; private set; }

    public double? Breitengrad { get; private set; }

    public double? Laengengrad { get; private set; }

    /// <summary>Lagebeschreibung dieser Ebene, z. B. "2. OG, rechts, dritte Tür links".</summary>
    public string? Lage { get; private set; }

    /// <summary>Zugangslink bei virtuellen Räumen.</summary>
    public string? Zugang { get; private set; }

    public bool Aktiv { get; private set; } = true;

    public List<OrtKapazitaet> Kapazitaeten { get; private set; } = [];

    public IReadOnlyCollection<OrtMerkmal> Merkmale => _merkmale;

    public string Anzeige => Kurzname is null || Name.StartsWith(Kurzname, StringComparison.Ordinal) ? Name : $"{Kurzname} {Name}";

    /// <summary>Räume und virtuelle Räume sind exklusiv (Konflikt bei Überschneidung); Treffpunkte nicht.</summary>
    public bool Exklusiv => Art is Ortsart.Raum or Ortsart.VirtuellerRaum;

    public bool Buchbar => Art is Ortsart.Raum or Ortsart.VirtuellerRaum or Ortsart.AllgemeinerOrt;

    public Ort MitAdresse(string strasse, string plz, string stadt, double? breite = null, double? laenge = null)
    {
        Strasse = strasse;
        Postleitzahl = plz;
        Stadt = stadt;
        Breitengrad = breite;
        Laengengrad = laenge;
        return this;
    }

    public Ort MitLage(string? lage)
    {
        Lage = string.IsNullOrWhiteSpace(lage) ? null : lage.Trim();
        return this;
    }

    public Ort MitZugang(string zugang)
    {
        Zugang = zugang;
        return this;
    }

    public Ort MitKapazitaet(Nutzungsart nutzungsart, int plaetze)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(plaetze, 1);
        Kapazitaeten = Kapazitaeten.Where(k => k.Nutzungsart != nutzungsart).Append(new OrtKapazitaet(nutzungsart, plaetze)).ToList();
        return this;
    }

    public int? KapazitaetFuer(Nutzungsart nutzungsart) => Kapazitaeten.FirstOrDefault(k => k.Nutzungsart == nutzungsart)?.Plaetze;

    /// <summary>Trägt ein Merkmal ab einem Datum ein (optional befristet, z. B. Zulassung bis 31.12.2027).</summary>
    public void MerkmalSetzenAb(Merkmal merkmal, string? wert, string? nachweis, DateOnly ab, DateOnly? bisEinschliesslich, Erfassung erfassung)
    {
        ArgumentNullException.ThrowIfNull(merkmal);
        if (merkmal.NachweisPflicht && string.IsNullOrWhiteSpace(nachweis))
        {
            throw new ArgumentException($"Für „{merkmal.Name}“ ist ein Nachweis Pflicht.");
        }

        if (merkmal.Bezug != MerkmalBezug.Ort)
        {
            throw new ArgumentException($"„{merkmal.Name}“ ist ein Merkmal für Personen, nicht für Orte.");
        }

        ZeitlicheHistorie.AendernAb(_merkmale, new OrtMerkmal(merkmal.Schluessel, merkmal.WertPruefen(wert), nachweis, ab, bisEinschliesslich?.AddDays(1), erfassung));
    }

    public void MerkmalEntfernenAb(string schluessel, DateOnly ab, Erfassung erfassung)
    {
        var aktuell = _merkmale.Where(m => m.Schluessel == schluessel).AlsStichtag(ab, erfassung.Am)
            ?? throw new InvalidOperationException("Dieses Merkmal ist am angegebenen Tag nicht eingetragen.");
        ZeitlicheHistorie.BeendenAb(_merkmale, aktuell.KopieMit(ab, null, erfassung with { Art = Erfassungsart.Beendet }));
    }
}

/// <summary>Ein am Stichtag wirksames Merkmal mit Herkunft (eigenes oder geerbtes).</summary>
public sealed record WirksamesMerkmal(Merkmal Merkmal, OrtMerkmal Eintrag, Ort Herkunft, bool Geerbt)
{
    /// <summary>Letzter Gültigkeitstag oder <c>null</c> bei unbefristeten Merkmalen.</summary>
    public DateOnly? GueltigBis => Eintrag.GueltigBis?.AddDays(-1);
}

/// <summary>Alle Orte mit ihrer Hierarchie: wirksame Merkmale (mit Vererbung), Pfad, Wegbeschreibung.</summary>
public sealed class Ortsverzeichnis(IEnumerable<Ort> orte, IEnumerable<Merkmal> katalog, IEnumerable<Kursart>? kursarten = null)
{
    private readonly Dictionary<Guid, Ort> _orte = orte.ToDictionary(o => o.Id);
    private readonly Dictionary<Guid, Nutzungsart> _nutzung = (kursarten ?? []).ToDictionary(k => k.Id, k => k.Nutzungsart);

    /// <summary>Nutzungsart eines Kurses = die seiner Kursart (keine Kopie am Kurs); unbekannt → Seminar.</summary>
    public Nutzungsart NutzungsartVon(Kurs kurs) => _nutzung.GetValueOrDefault(kurs.KursartId, Nutzungsart.Seminar);
    private readonly Dictionary<string, Merkmal> _katalog = katalog.ToDictionary(m => m.Schluessel);

    public IReadOnlyCollection<Ort> Alle => _orte.Values;

    public IReadOnlyDictionary<string, Merkmal> Katalog => _katalog;

    public Ort? Finden(Guid id) => _orte.GetValueOrDefault(id);

    /// <summary>Vom obersten Elternteil bis zum Ort selbst, z. B. Liegenschaft → Gebäude → Raum.</summary>
    public IReadOnlyList<Ort> Pfad(Ort ort)
    {
        var pfad = new List<Ort> { ort };
        var aktuell = ort;
        while (aktuell.ElternId is { } e && _orte.TryGetValue(e, out var eltern) && pfad.Count < 10)
        {
            pfad.Insert(0, eltern);
            aktuell = eltern;
        }

        return pfad;
    }

    /// <summary>Koordinaten des Orts oder seines nächsten übergeordneten Orts mit Koordinaten (Raum → Gebäude → Liegenschaft).</summary>
    public (double Breite, double Laenge)? Koordinaten(Guid id)
        => Finden(id) is { } ort && Pfad(ort).LastOrDefault(o => o.Breitengrad is not null && o.Laengengrad is not null) is { } mit
            ? (mit.Breitengrad!.Value, mit.Laengengrad!.Value)
            : null;

    public IEnumerable<Ort> Kinder(Guid id) => _orte.Values.Where(o => o.ElternId == id).OrderBy(o => o.Name, StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true));

    /// <summary>
    /// Merkmale, die am <paramref name="tag"/> wirken: eigene plus vererbbare der Eltern. Ein eigenes Merkmal
    /// überschreibt geerbte derselben Ausschlussgruppe (z. B. "nicht barrierefrei" im Altbau-Obergeschoss).
    /// </summary>
    public IReadOnlyList<WirksamesMerkmal> WirksameMerkmale(Ort ort, DateOnly tag, DateTimeOffset bekanntAm)
    {
        var ergebnis = new List<WirksamesMerkmal>();
        foreach (var ebene in Pfad(ort).Reverse())
        {
            var geerbt = ebene.Id != ort.Id;
            foreach (var eintrag in ebene.Merkmale.AlleAmStichtag(tag, bekanntAm))
            {
                if (!_katalog.TryGetValue(eintrag.Schluessel, out var m) || (geerbt && !m.Vererbbar))
                {
                    continue;
                }

                var schonDa = ergebnis.Any(x => x.Merkmal.Schluessel == m.Schluessel
                                                || (m.Ausschlussgruppe is not null && x.Merkmal.Ausschlussgruppe == m.Ausschlussgruppe));
                if (!schonDa)
                {
                    ergebnis.Add(new WirksamesMerkmal(m, eintrag, ebene, geerbt));
                }
            }
        }

        return ergebnis;
    }

    /// <summary>Wegbeschreibung aus Adresse und Lagebeschreibungen aller Ebenen, mit Kartenlink.</summary>
    public string Wegbeschreibung(Ort ort)
    {
        var pfad = Pfad(ort);
        var teile = new List<string>();
        var mitAdresse = pfad.LastOrDefault(o => o.Strasse is not null);
        if (mitAdresse is not null)
        {
            teile.Add($"{mitAdresse.Name}, {mitAdresse.Strasse}, {mitAdresse.Postleitzahl} {mitAdresse.Stadt}.");
        }

        teile.AddRange(pfad.Where(o => o.Lage is not null)
            .Select(o => o.Art switch
            {
                Ortsart.Gebaeude => $"{o.Name}: {o.Lage}",
                Ortsart.Raum => $"Zum Raum {o.Kurzname ?? o.Name}: {o.Lage}",
                _ => o.Lage!,
            }));

        if (ort.Art == Ortsart.VirtuellerRaum)
        {
            teile.Add(ort.Zugang is null ? "Online-Raum; den Zugangslink erhalten Sie mit der Anmeldebestätigung." : $"Online: {ort.Zugang}");
        }

        var geo = pfad.LastOrDefault(o => o.Breitengrad is not null);
        if (geo is not null)
        {
            var b = geo.Breitengrad!.Value.ToString("0.#####", CultureInfo.InvariantCulture);
            var l = geo.Laengengrad!.Value.ToString("0.#####", CultureInfo.InvariantCulture);
            teile.Add($"Karte: https://www.openstreetmap.org/?mlat={b}&mlon={l}#map=18/{b}/{l}");
        }

        return string.Join(Environment.NewLine, teile);
    }

    public string PfadAnzeige(Ort ort) => string.Join(" › ", Pfad(ort).Select(o => o.Kurzname ?? o.Name));
}
