using System.Globalization;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Programm;

public sealed class Semester
{
    private Semester()
    {
        Name = string.Empty;
        Kuerzel = string.Empty;
    }

    public Semester(string name, string kuerzel, DateOnly beginn, DateOnly ende)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (ende < beginn)
        {
            throw new ArgumentException("Das Semesterende liegt vor dem Beginn.");
        }

        Id = Ids.Neu();
        Name = name.Trim();
        Kuerzel = kuerzel.Trim();
        Beginn = beginn;
        Ende = ende;
    }

    public Guid Id { get; private set; }

    /// <summary>z. B. "Frühjahr 2027".</summary>
    public string Name { get; private set; }

    /// <summary>Präfix der Kursnummern, z. B. "27F".</summary>
    public string Kuerzel { get; private set; }

    public DateOnly Beginn { get; private set; }

    public DateOnly Ende { get; private set; }

    /// <summary>Archiviert: schreibgeschützt und aus Standardlisten ausgeblendet; rückgängig machbar.</summary>
    public DateTimeOffset? ArchiviertAm { get; private set; }

    public bool Archiviert => ArchiviertAm is not null;

    public void Archivieren(DateTimeOffset jetzt) => ArchiviertAm ??= jetzt;

    public void Wiederherstellen() => ArchiviertAm = null;
}

public enum Sichtbarkeit
{
    Oeffentlich,
    Intern,
    Geschlossen,
}

public enum Kursumfang
{
    Begrenzt,
    Unbefristet,
}

public enum Kursstatus
{
    Entwurf,
    Veroeffentlicht,
    Abgesagt,
}

/// <summary>Wie Teilnehmende belegen: alle Veranstaltungen oder ein Kontingent (z. B. 10er-Karte). Umsetzung der Buchung in I3.</summary>
public enum Belegungsart
{
    AlleVeranstaltungen,
    Kontingent,
}

/// <summary>Wer bucht: Teilnehmerservice am Tresen, Integrationskurs-Team, Teilnehmende selbst (online), Auftraggeber.</summary>
public enum Buchungsweg
{
    Tresen,
    IntegrationskursTeam,
    Online,
    Auftraggeber,
}

public enum SelbstCheckin
{
    Erlaubt,

    /// <summary>Selbst-Check-in wird vorgemerkt und von der Kursleitung bestätigt (z. B. Integrationskurse).</summary>
    MitBestaetigung,

    /// <summary>Nur die Kursleitung erfasst die Anwesenheit (Ausweis-Scan oder Liste).</summary>
    Aus,
}

public static class SelbstCheckinAnzeige
{
    public static string Text(SelbstCheckin s) => s switch
    {
        SelbstCheckin.Erlaubt => "erlaubt",
        SelbstCheckin.MitBestaetigung => "mit Bestätigung durch die Kursleitung",
        _ => "aus (nur Kursleitung)",
    };
}

/// <summary>Regel je Buchungsweg (Rolle): darf buchen, darf über die Höchstzahl hinaus buchen.</summary>
public sealed record Buchungsrecht(Buchungsweg Weg, bool DarfBuchen, bool DarfUeberbuchen = false);

/// <summary>Buchungsrichtlinie eines Kurses (Vorlage aus der Kursart, je Kurs anpassbar).</summary>
public sealed record Buchungsrichtlinie
{
    public List<Buchungsrecht> Rechte { get; init; } = [];

    public Belegungsart Belegung { get; init; } = Belegungsart.AlleVeranstaltungen;

    /// <summary>Anzahl Veranstaltungen je Kontingent (nur bei <see cref="Belegungsart.Kontingent"/>).</summary>
    public int? KontingentAnzahl { get; init; }

    public int? KontingentGueltigMonate { get; init; }

    /// <summary>Vorab-Einbuchung in einzelne Veranstaltungen mit Kapazitätsprüfung (I3).</summary>
    public bool VorabEinbuchung { get; init; }

    public bool Warteliste { get; init; } = true;

    public int StornofristTage { get; init; } = 7;

    /// <summary>Bearbeitungsgebühr bei fristgerechter Abmeldung (AGB), in Cent.</summary>
    public long BearbeitungsgebuehrCent { get; init; }

    /// <summary>Gebühr für eine Umbuchung auf einen anderen Kurs, in Cent.</summary>
    public long UmbuchungsgebuehrCent { get; init; }

    /// <summary>So lange gilt ein Nachrückangebot von der Warteliste.</summary>
    public int NachrueckfristTage { get; init; } = 3;

    /// <summary>Vorab-Einbuchung in eine Veranstaltung bis so viele Stunden vor Beginn.</summary>
    public int EinbuchungsfristStunden { get; init; } = 24;

    /// <summary>Austragen aus einer Veranstaltung bis so viele Stunden vor Beginn.</summary>
    public int AustragefristStunden { get; init; } = 24;

    /// <summary>Wer eingebucht ist und nicht erscheint, verbraucht die Einheit des Kontingents.</summary>
    public bool NichterscheinenZaehlt { get; init; } = true;

    /// <summary>Selbst-Check-in der Teilnehmenden per Termin-QR (bei abrechnungsrelevanter Anwesenheit abschaltbar oder mit Bestätigung).</summary>
    public SelbstCheckin SelbstCheckin { get; init; } = SelbstCheckin.Erlaubt;

    public bool Darf(Buchungsweg weg) => Rechte.Any(r => r.Weg == weg && r.DarfBuchen);

    public bool DarfUeberbuchen(Buchungsweg weg) => Rechte.Any(r => r.Weg == weg && r.DarfBuchen && r.DarfUeberbuchen);

    public static string WegAnzeige(Buchungsweg weg) => weg switch
    {
        Buchungsweg.Tresen => "den Teilnehmerservice (Tresen)",
        Buchungsweg.IntegrationskursTeam => "das Integrationskurs-Team",
        Buchungsweg.Online => "die Online-Anmeldung",
        _ => "die beauftragende Organisation",
    };

    public static Buchungsrichtlinie Oeffentlich { get; } = new()
    {
        Rechte = [new(Buchungsweg.Tresen, true, true), new(Buchungsweg.Online, true)],
    };

    public static Buchungsrichtlinie Integrationskurs { get; } = new()
    {
        Rechte = [new(Buchungsweg.IntegrationskursTeam, true, true)],
        SelbstCheckin = SelbstCheckin.MitBestaetigung,
        Warteliste = false,
        StornofristTage = 0,
    };

    public static Buchungsrichtlinie Firmenkurs { get; } = new()
    {
        Rechte = [new(Buchungsweg.Tresen, true), new(Buchungsweg.Auftraggeber, true)],
        Warteliste = false,
    };

    public static Buchungsrichtlinie Kontingentkurs(int anzahl, int monate) => new()
    {
        Rechte = [new(Buchungsweg.Tresen, true), new(Buchungsweg.Online, true)],
        Belegung = Belegungsart.Kontingent,
        KontingentAnzahl = anzahl,
        KontingentGueltigMonate = monate,
        VorabEinbuchung = true,
    };
}

/// <summary>Kursart mit Nutzungsart, benötigten Merkmalen und Vorlage für die Buchungsrichtlinie.</summary>
public sealed class Kursart
{
    private Kursart()
    {
        Name = string.Empty;
        Richtlinie = Buchungsrichtlinie.Oeffentlich;
    }

    public Kursart(string name, Nutzungsart nutzungsart, IEnumerable<string> merkmale, Buchungsrichtlinie richtlinie,
        Sichtbarkeit sichtbarkeit = Sichtbarkeit.Oeffentlich, bool ferienAuslassen = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = Ids.Neu();
        Name = name.Trim();
        Nutzungsart = nutzungsart;
        BenoetigteMerkmale = merkmale.ToList();
        Richtlinie = richtlinie;
        Sichtbarkeit = sichtbarkeit;
        FerienAuslassen = ferienAuslassen;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public Nutzungsart Nutzungsart { get; private set; }

    /// <summary>Merkmalsschlüssel, die jeder Ort haben muss (z. B. "bamf" für Integrationskurse).</summary>
    public List<string> BenoetigteMerkmale { get; private set; } = [];

    public Buchungsrichtlinie Richtlinie { get; private set; }

    public Sichtbarkeit Sichtbarkeit { get; private set; }

    /// <summary>Vorgabe für neue Serien: in den Schulferien pausieren (z. B. Sprachkurse) oder weiterlaufen (z. B. Bewegung).</summary>
    public bool FerienAuslassen { get; private set; }

    /// <summary>Qualifikationen, die Dozierende am Veranstaltungstag haben müssen (z. B. "bamf-lk" für Integrationskurse).</summary>
    public List<string> BenoetigteDozierendenmerkmale { get; private set; } = [];

    public Kursart MitDozierendenmerkmalen(IEnumerable<string> schluessel)
    {
        BenoetigteDozierendenmerkmale = schluessel.Distinct().ToList();
        return this;
    }
}

public sealed record KursDozent(Guid PersonId, string Name, bool Leitung);

/// <summary>Entgelt eines Kurses in Cent (zeitlich gültig, z. B. Erhöhung ab Frühjahr).</summary>
public sealed class KursEntgelt : BitemporaleVersion<KursEntgelt>
{
    private KursEntgelt()
    {
    }

    public KursEntgelt(long cent, long? ermaessigtCent, DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cent);
        Cent = cent;
        ErmaessigtCent = ermaessigtCent;
    }

    public long Cent { get; private set; }

    public long? ErmaessigtCent { get; private set; }

    public Geld Betrag => new(Cent);

    public override string Datenart => "Entgelt";

    public override string Feld => "Entgelt";

    public override string Gegenstand => "entgelt";

    public override string Wert => IstEndemarke ? "(keines)" : ErmaessigtCent is { } e ? $"{Betrag} (ermäßigt {new Geld(e)})" : Betrag.ToString();

    protected internal override KursEntgelt KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Cent, ErmaessigtCent, gueltigVon, gueltigBis, erfassung);
}

/// <summary>Ein Ort einer Veranstaltung; mehrere = hybrid. Eigene Höchstzahl überschreibt die Kapazität des Orts.</summary>
public sealed record VeranstaltungsOrt(Guid OrtId, string OrtName, int? Hoechstzahl = null);

/// <summary>Ein einzelner Termin eines Kurses.</summary>
public sealed class Veranstaltung
{
    private Veranstaltung()
    {
        DozentName = string.Empty;
    }

    internal Veranstaltung(Guid kursId, int nummer, DateTime beginn, DateTime ende, IEnumerable<VeranstaltungsOrt> orte,
        KursDozent? dozent, bool ausSerie)
    {
        if (ende <= beginn)
        {
            throw new ArgumentException("Das Ende einer Veranstaltung muss nach dem Beginn liegen.");
        }

        Id = Ids.Neu();
        KursId = kursId;
        Nummer = nummer;
        Beginn = beginn;
        Ende = ende;
        Orte = orte.ToList();
        DozentId = dozent?.PersonId;
        DozentName = dozent?.Name ?? string.Empty;
        AusSerie = ausSerie;
    }

    public Guid Id { get; private set; }

    public Guid KursId { get; private set; }

    public int Nummer { get; private set; }

    /// <summary>Ortszeit (Wanduhr).</summary>
    public DateTime Beginn { get; private set; }

    public DateTime Ende { get; private set; }

    public List<VeranstaltungsOrt> Orte { get; private set; } = [];

    public Guid? DozentId { get; private set; }

    public string DozentName { get; private set; }

    /// <summary>Von der Serienregel erzeugt (sonst Einzelveranstaltung).</summary>
    public bool AusSerie { get; private set; }

    /// <summary>Einzeln geändert ("nur diese"): bleibt bei Serienänderungen unverändert.</summary>
    public bool Ausnahme { get; private set; }

    public bool Ausgefallen { get; private set; }

    public string? Bemerkung { get; private set; }

    public DateOnly Datum => DateOnly.FromDateTime(Beginn);

    public void NurDieseAendern(DateTime beginn, DateTime ende, IEnumerable<VeranstaltungsOrt> orte, KursDozent? dozent, string? bemerkung)
    {
        if (ende <= beginn)
        {
            throw new ArgumentException("Das Ende einer Veranstaltung muss nach dem Beginn liegen.");
        }

        Beginn = beginn;
        Ende = ende;
        Orte = orte.ToList();
        DozentId = dozent?.PersonId;
        DozentName = dozent?.Name ?? string.Empty;
        Bemerkung = bemerkung;
        Ausnahme = true;
    }

    public void Ausfallen(string grund)
    {
        Ausgefallen = true;
        Bemerkung = grund;
        Ausnahme = true;
    }

    internal void Nummerieren(int nummer) => Nummer = nummer;

    internal void DozentUmbenennen(Guid personId, string name)
    {
        if (DozentId == personId)
        {
            DozentName = name;
        }
    }
}

/// <summary>Kurs mit Team, Sichtbarkeit, Richtlinie, Entgelt (versioniert) und Veranstaltungen.</summary>
public sealed class Kurs
{
    private readonly List<KursEntgelt> _entgelte = [];
    private readonly List<Veranstaltung> _veranstaltungen = [];

    private Kurs()
    {
        Kursnummer = string.Empty;
        Titel = string.Empty;
        Suchtext = string.Empty;
        Richtlinie = Buchungsrichtlinie.Oeffentlich;
    }

    public Kurs(string kursnummer, string titel, Semester semester, int fachbereich, Kursart kursart, int minTeilnehmende, int maxTeilnehmende)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kursnummer);
        ArgumentException.ThrowIfNullOrWhiteSpace(titel);
        ArgumentNullException.ThrowIfNull(semester);
        ArgumentNullException.ThrowIfNull(kursart);
        if (maxTeilnehmende < 1 || minTeilnehmende < 0 || minTeilnehmende > maxTeilnehmende)
        {
            throw new ArgumentException("Mindest- und Höchstzahl passen nicht zusammen.");
        }

        Id = Ids.Neu();
        Kursnummer = kursnummer.Trim();
        Titel = titel.Trim();
        SemesterId = semester.Id;
        Fachbereich = fachbereich;
        KursartId = kursart.Id;
        Nutzungsart = kursart.Nutzungsart;
        BenoetigteMerkmale = [.. kursart.BenoetigteMerkmale];
        BenoetigteDozierendenmerkmale = [.. kursart.BenoetigteDozierendenmerkmale];
        Richtlinie = kursart.Richtlinie;
        Sichtbarkeit = kursart.Sichtbarkeit;
        MinTeilnehmende = minTeilnehmende;
        MaxTeilnehmende = maxTeilnehmende;
        Status = Kursstatus.Entwurf;
        Suchtext = Gemeinsam.Suchtext.Normalisieren($"{Kursnummer} {Titel}");
    }

    public Guid Id { get; private set; }

    public string Kursnummer { get; private set; }

    public string Titel { get; private set; }

    public string? Beschreibung { get; private set; }

    public Guid SemesterId { get; private set; }

    public int Fachbereich { get; private set; }

    public Guid KursartId { get; private set; }

    public Nutzungsart Nutzungsart { get; private set; }

    public List<string> BenoetigteMerkmale { get; private set; } = [];

    /// <summary>Qualifikationen der Dozierenden (aus der Kursart, je Kurs ergänzbar, z. B. Alpha-Zusatzqualifikation).</summary>
    public List<string> BenoetigteDozierendenmerkmale { get; private set; } = [];

    public Kurs MitDozierendenmerkmalen(IEnumerable<string> schluessel)
    {
        BenoetigteDozierendenmerkmale = schluessel.Distinct().ToList();
        return this;
    }

    /// <summary>Als Bildungsurlaub nach AWbG NRW anerkannt (Anerkennungsnummer); dann gibt es die Bildungsurlaubsbescheinigung.</summary>
    public string? Bildungsurlaub { get; private set; }

    public Kurs AlsBildungsurlaub(string? anerkennung)
    {
        Bildungsurlaub = string.IsNullOrWhiteSpace(anerkennung) ? null : anerkennung.Trim();
        return this;
    }

    public Sichtbarkeit Sichtbarkeit { get; private set; }

    /// <summary>Bei geschlossenen Kursen: die beauftragende Organisation.</summary>
    public Guid? AuftraggeberId { get; private set; }

    public string? AuftraggeberName { get; private set; }

    public Buchungsrichtlinie Richtlinie { get; private set; }

    public int MinTeilnehmende { get; private set; }

    public int MaxTeilnehmende { get; private set; }

    public Kursstatus Status { get; private set; }

    public Kursumfang Umfang { get; private set; }

    public Serienregel? Serie { get; private set; }

    /// <summary>Bis zu diesem Tag sind Veranstaltungen erzeugt (bei unbefristeten Kursen rollierend).</summary>
    public DateOnly? ErzeugtBis { get; private set; }

    public List<KursDozent> Team { get; private set; } = [];

    public IReadOnlyCollection<KursEntgelt> Entgelte => _entgelte;

    public IReadOnlyCollection<Veranstaltung> Veranstaltungen => _veranstaltungen;

    public string Suchtext { get; private set; }

    /// <summary>Archiviert: schreibgeschützt und aus Standardlisten ausgeblendet; rückgängig machbar.</summary>
    public DateTimeOffset? ArchiviertAm { get; private set; }

    public bool Archiviert => ArchiviertAm is not null;

    public void Archivieren(DateTimeOffset jetzt) => ArchiviertAm ??= jetzt;

    public void Wiederherstellen() => ArchiviertAm = null;

    /// <summary>Letzter Termin (für die Suche "Kursende vor …").</summary>
    public DateOnly? Kursende => _veranstaltungen.Count == 0 ? null : DateOnly.FromDateTime(_veranstaltungen.Max(v => v.Ende));

    /// <summary>Beginn des ersten stattfindenden Termins (für Entgelt, Storno- und Umbuchungsfristen).</summary>
    public DateTime? ErsterTermin => _veranstaltungen.Where(v => !v.Ausgefallen).Select(v => (DateTime?)v.Beginn).Min();

    /// <summary>Begrenzter Kurs, dessen letzter Termin vorbei ist.</summary>
    public bool BeendetAm(DateTime jetzt) => Umfang == Kursumfang.Begrenzt && _veranstaltungen.Count > 0 && _veranstaltungen.All(v => v.Ende < jetzt);

    /// <summary>Entgelt, das bei einer Buchung jetzt gilt: gültig zum Kursbeginn (oder heute, falls der Kurs schon läuft), nach heutigem Wissensstand.</summary>
    public KursEntgelt? EntgeltFuerBuchung(DateTimeOffset jetzt, DateTimeOffset? bekanntAm = null)
    {
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var beginn = ErsterTermin is { } b ? DateOnly.FromDateTime(b) : heute;
        var stand = bekanntAm ?? jetzt;
        return EntgeltAm(beginn > heute ? beginn : heute, stand) ?? EntgeltAm(beginn, stand);
    }

    /// <summary>Nach Anonymisierung einer Dozentin: Platzhalter statt Name im Team und an den Terminen; die Verknüpfung bleibt.</summary>
    public void DozentAnonymisieren(Guid personId, string platzhalter)
    {
        Team = Team.Select(d => d.PersonId == personId ? d with { Name = platzhalter } : d).ToList();
        foreach (var v in _veranstaltungen)
        {
            v.DozentUmbenennen(personId, platzhalter);
        }
    }

    public KursDozent? Leitung => Team.FirstOrDefault(d => d.Leitung) ?? Team.FirstOrDefault();

    public Kurs MitBeschreibung(string? text)
    {
        Beschreibung = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return this;
    }

    public Kurs MitSichtbarkeit(Sichtbarkeit sichtbarkeit, Guid? auftraggeberId = null, string? auftraggeberName = null)
    {
        if (sichtbarkeit == Sichtbarkeit.Geschlossen && auftraggeberId is null)
        {
            throw new ArgumentException("Geschlossene Kurse brauchen eine beauftragende Organisation.");
        }

        Sichtbarkeit = sichtbarkeit;
        AuftraggeberId = auftraggeberId;
        AuftraggeberName = auftraggeberName;
        return this;
    }

    public Kurs MitRichtlinie(Buchungsrichtlinie richtlinie)
    {
        Richtlinie = richtlinie;
        return this;
    }

    public Kurs MitTeam(IEnumerable<KursDozent> team)
    {
        Team = team.DistinctBy(d => d.PersonId).ToList();
        return this;
    }

    public void StatusSetzen(Kursstatus status) => Status = status;

    public void EntgeltSetzenAb(Geld betrag, Geld? ermaessigt, DateOnly ab, Erfassung erfassung)
        => ZeitlicheHistorie.AendernAb(_entgelte, new KursEntgelt(betrag.Cent, ermaessigt?.Cent, ab, null, erfassung));

    public KursEntgelt? EntgeltAm(DateOnly tag, DateTimeOffset bekanntAm) => _entgelte.AlsStichtag(tag, bekanntAm);

    /// <summary>
    /// Schnellweg: Serie festlegen und Veranstaltungen erzeugen. Unbefristete Kurse werden bis <paramref name="horizont"/> erzeugt
    /// und später rollierend fortgeschrieben.
    /// </summary>
    public IReadOnlyList<Serientermin> SerieFestlegen(Serienregel regel, Kalender kalender, Func<Guid, string> ortName, DateOnly horizont)
    {
        ArgumentNullException.ThrowIfNull(regel);
        regel.Pruefen();
        if (regel.OrtIds.Count == 0)
        {
            throw new ArgumentException("Bitte mindestens einen Ort wählen.");
        }

        Serie = regel;
        Umfang = regel.Unbefristet ? Kursumfang.Unbefristet : Kursumfang.Begrenzt;
        _veranstaltungen.RemoveAll(v => v.AusSerie && !v.Ausnahme);
        var termine = Seriengenerator.Erzeugen(regel, kalender, regel.Unbefristet ? horizont : null);
        Hinzufuegen(termine, regel, ortName);
        ErzeugtBis = regel.Unbefristet ? horizont : termine.LastOrDefault()?.Datum;
        Nummerieren();
        return termine;
    }

    /// <summary>"Alle folgenden ändern": ab <paramref name="ab"/> gilt die neue Regel; frühere Termine und Ausnahmen bleiben.</summary>
    public IReadOnlyList<Serientermin> SerieAendernAb(DateOnly ab, Serienregel neueRegel, Kalender kalender, Func<Guid, string> ortName, DateOnly horizont)
    {
        ArgumentNullException.ThrowIfNull(neueRegel);
        if (Serie is null)
        {
            throw new InvalidOperationException("Dieser Kurs hat keine Serie.");
        }

        var regel = neueRegel with { Start = ab };
        regel.Pruefen();
        var schonStattgefunden = _veranstaltungen.Count(v => v.AusSerie && !v.Ausgefallen && v.Datum < ab);
        if (regel.Anzahl is { } anzahl)
        {
            regel = regel with { Anzahl = Math.Max(1, anzahl - schonStattgefunden) };
        }

        _veranstaltungen.RemoveAll(v => v.AusSerie && !v.Ausnahme && v.Datum >= ab);
        var termine = Seriengenerator.Erzeugen(regel, kalender, regel.Unbefristet ? horizont : null);
        Hinzufuegen(termine, regel, ortName);
        Serie = neueRegel with { Start = Serie.Start, Anzahl = neueRegel.Anzahl };
        Umfang = regel.Unbefristet ? Kursumfang.Unbefristet : Kursumfang.Begrenzt;
        ErzeugtBis = regel.Unbefristet ? horizont : termine.LastOrDefault()?.Datum ?? ErzeugtBis;
        Nummerieren();
        return termine;
    }

    /// <summary>Unbefristete Kurse rollierend bis zum Horizont fortschreiben. Liefert die Anzahl neuer Termine.</summary>
    public int Fortschreiben(Kalender kalender, Func<Guid, string> ortName, DateOnly horizont)
    {
        if (Umfang != Kursumfang.Unbefristet || Serie is null || Status == Kursstatus.Abgesagt || (ErzeugtBis is { } e && e >= horizont))
        {
            return 0;
        }

        var start = ErzeugtBis?.AddDays(1) ?? Serie.Start;
        var termine = Seriengenerator.Erzeugen(Serie with { Start = start, Ende = null, Anzahl = null }, kalender, horizont);
        Hinzufuegen(termine, Serie, ortName);
        ErzeugtBis = horizont;
        Nummerieren();
        return termine.Count(t => t.Findet);
    }

    public Veranstaltung EinzelveranstaltungHinzufuegen(DateTime beginn, DateTime ende, IEnumerable<VeranstaltungsOrt> orte, KursDozent? dozent)
    {
        var v = new Veranstaltung(Id, 0, beginn, ende, orte, dozent ?? Leitung, ausSerie: false);
        _veranstaltungen.Add(v);
        Nummerieren();
        return v;
    }

    private void Hinzufuegen(IEnumerable<Serientermin> termine, Serienregel regel, Func<Guid, string> ortName)
    {
        var dozent = regel.DozentId is { } d ? Team.FirstOrDefault(t => t.PersonId == d) : Leitung;
        var orte = regel.OrtIds.Select(id => new VeranstaltungsOrt(id, ortName(id))).ToList();
        foreach (var t in termine.Where(t => t.Findet))
        {
            if (_veranstaltungen.Any(v => v.Ausnahme && v.Datum == t.Datum))
            {
                continue; // einzeln geänderter Termin an diesem Tag bleibt bestehen
            }

            _veranstaltungen.Add(new Veranstaltung(Id, 0, t.Beginn, t.Ende, orte, dozent, ausSerie: true));
        }
    }

    private void Nummerieren()
    {
        var i = 1;
        foreach (var v in _veranstaltungen.OrderBy(v => v.Beginn))
        {
            v.Nummerieren(i++);
        }
    }

    /// <summary>Kopie in ein anderes Semester: Serie und Termine um <paramref name="verschiebungTage"/> verschoben, Status Entwurf.</summary>
    public Kurs KopieFuer(Semester ziel, string neueNummer, int verschiebungTage, Kalender kalender, Func<Guid, string> ortName, DateOnly horizont, Erfassung erfassung)
    {
        var kopie = new Kurs
        {
            Id = Ids.Neu(),
            Kursnummer = neueNummer,
            Titel = Titel,
            Beschreibung = Beschreibung,
            SemesterId = ziel.Id,
            Fachbereich = Fachbereich,
            KursartId = KursartId,
            Nutzungsart = Nutzungsart,
            BenoetigteMerkmale = [.. BenoetigteMerkmale],
            BenoetigteDozierendenmerkmale = [.. BenoetigteDozierendenmerkmale],
            Sichtbarkeit = Sichtbarkeit,
            AuftraggeberId = AuftraggeberId,
            AuftraggeberName = AuftraggeberName,
            Richtlinie = Richtlinie,
            MinTeilnehmende = MinTeilnehmende,
            MaxTeilnehmende = MaxTeilnehmende,
            Status = Kursstatus.Entwurf,
            Team = [.. Team],
            Suchtext = Gemeinsam.Suchtext.Normalisieren($"{neueNummer} {Titel}"),
        };

        var entgelt = EntgeltAm(Serie?.Start ?? ziel.Beginn, erfassung.Am) ?? _entgelte.AktuellerVerlauf().LastOrDefault();
        if (entgelt is not null)
        {
            kopie._entgelte.Add(new KursEntgelt(entgelt.Cent, entgelt.ErmaessigtCent, ziel.Beginn, null, erfassung with { Art = Erfassungsart.Ersterfassung }));
        }

        if (Serie is not null)
        {
            kopie.SerieFestlegen(Serie.Verschoben(verschiebungTage), kalender, ortName, horizont);
        }

        return kopie;
    }

    /// <summary>Kursnummer für ein anderes Semester: Präfix tauschen ("26H-301" → "27F-301").</summary>
    public static string NummerFuer(string nummer, Semester ziel)
    {
        var i = nummer.IndexOf('-', StringComparison.Ordinal);
        return i < 0 ? $"{ziel.Kuerzel}-{nummer}" : $"{ziel.Kuerzel}{nummer[i..]}";
    }

    /// <summary>Verschiebung in ganzen Wochen (Wochentage bleiben erhalten).</summary>
    public static int Verschiebung(Semester von, Semester nach)
        => (int)Math.Round((nach.Beginn.DayNumber - von.Beginn.DayNumber) / 7.0, MidpointRounding.AwayFromZero) * 7;

    public override string ToString() => $"{Kursnummer} {Titel}";
}

public static class KursAnzeige
{
    public static string Sichtbarkeit(Sichtbarkeit s) => s switch
    {
        Programm.Sichtbarkeit.Oeffentlich => "öffentlich",
        Programm.Sichtbarkeit.Intern => "intern",
        _ => "geschlossen",
    };

    public static string Status(Kursstatus s) => s switch
    {
        Kursstatus.Veroeffentlicht => "veröffentlicht",
        Kursstatus.Abgesagt => "abgesagt",
        _ => "Entwurf",
    };

    public static string Wochentag(DayOfWeek d) => CultureInfo.GetCultureInfo("de-DE").DateTimeFormat.GetAbbreviatedDayName(d);
}
