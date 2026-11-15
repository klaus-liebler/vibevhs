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

/// <summary>Eintrag im pflegbaren Merkmalskatalog (z. B. "barrierefrei", "BAMF-Kursort").</summary>
public sealed class Merkmal
{
    private Merkmal()
    {
        Schluessel = string.Empty;
        Name = string.Empty;
        Gruppe = string.Empty;
    }

    public Merkmal(string schluessel, string name, string gruppe, bool vererbbar = true, bool nachweisPflicht = false, string? ausschlussgruppe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schluessel);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Schluessel = schluessel.Trim().ToLowerInvariant();
        Name = name.Trim();
        Gruppe = gruppe;
        Vererbbar = vererbbar;
        NachweisPflicht = nachweisPflicht;
        Ausschlussgruppe = ausschlussgruppe;
    }

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
        Suchtext = string.Empty;
    }

    public Ort(Ortsart art, string name, Guid? elternId = null, string? kurzname = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = Ids.Neu();
        Art = art;
        Name = name.Trim();
        Kurzname = string.IsNullOrWhiteSpace(kurzname) ? null : kurzname.Trim();
        ElternId = elternId;
        Suchtext = Gemeinsam.Suchtext.Normalisieren($"{Name} {Kurzname}");
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

    /// <summary>Normalisierter Text für die globale Suche.</summary>
    public string Suchtext { get; private set; }

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
        Suchtext = Gemeinsam.Suchtext.Normalisieren($"{Name} {Kurzname} {Strasse} {Stadt}");
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

        ZeitlicheHistorie.AendernAb(_merkmale, new OrtMerkmal(merkmal.Schluessel, wert, nachweis, ab, bisEinschliesslich?.AddDays(1), erfassung));
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
public sealed class Ortsverzeichnis(IEnumerable<Ort> orte, IEnumerable<Merkmal> katalog)
{
    private readonly Dictionary<Guid, Ort> _orte = orte.ToDictionary(o => o.Id);
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
