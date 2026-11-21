using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Finanzen;

/// <summary>Mahnstufe: so viele Tage nach Fälligkeit bzw. nach der vorigen Stufe, mit Gebühr.</summary>
public sealed record Mahnstufe(int Stufe, string Bezeichnung, int TageNachVorstufe, long GebuehrCent);

/// <summary>Finanzeinstellungen der Einrichtung (eine Zeile).</summary>
public sealed class Finanzeinstellungen
{
    private Finanzeinstellungen()
    {
        Mahnstufen = []; // beim Laden füllt EF die Liste aus der Tabelle Mahnstufen (keine Standardwerte vorbelegen)
        Kontoinhaber = string.Empty;
        Iban = string.Empty;
        GlaeubigerId = string.Empty;
        Steuerhinweis = string.Empty;
        Anschrift = string.Empty;
    }

    public Finanzeinstellungen(string kontoinhaber, string anschrift, string iban, string? bic, string glaeubigerId, string? steuernummer)
    {
        Id = 1;
        Kontoinhaber = kontoinhaber;
        Anschrift = anschrift;
        Iban = Finanzen.Iban.Normalisieren(iban);
        Bic = bic;
        GlaeubigerId = glaeubigerId;
        Steuernummer = steuernummer;
        Steuerhinweis = "Umsatzsteuerfrei nach § 4 Nr. 22 a UStG.";
    }

    public int Id { get; private set; }

    public string Kontoinhaber { get; private set; }

    public string Anschrift { get; private set; }

    public string Iban { get; private set; }

    public string? Bic { get; private set; }

    /// <summary>Gläubiger-Identifikationsnummer für SEPA-Lastschriften.</summary>
    public string GlaeubigerId { get; private set; }

    public string? Steuernummer { get; private set; }

    public string? UstId { get; private set; }

    public string Steuerhinweis { get; private set; }

    public int ZahlungszielTage { get; private set; } = 14;

    /// <summary>Frist der Vorabankündigung (Pre-Notification) vor dem Einzug; mit dem Mandat auf wenige Tage verkürzbar.</summary>
    public int VorabankuendigungTage { get; private set; } = 5;

    public long RuecklastschriftGebuehrCent { get; private set; } = 300;

    public List<Mahnstufe> Mahnstufen { get; private set; } =
    [
        new(1, "Zahlungserinnerung", 7, 0),
        new(2, "1. Mahnung", 14, 500),
        new(3, "2. Mahnung", 14, 1000),
    ];

    public void Aendern(string kontoinhaber, string anschrift, string iban, string? bic, string glaeubigerId, string? steuernummer, string? ustId,
        string steuerhinweis, int zahlungszielTage, int vorabankuendigungTage, long ruecklastschriftGebuehrCent, IEnumerable<Mahnstufe> mahnstufen)
    {
        if (Finanzen.Iban.Fehler(iban) is { } fehler)
        {
            throw new ArgumentException(fehler);
        }

        if (zahlungszielTage < 0 || vorabankuendigungTage < 1)
        {
            throw new ArgumentException("Zahlungsziel und Vorabankündigung brauchen positive Tage (Vorabankündigung mindestens 1 Tag).");
        }

        var stufen = mahnstufen.OrderBy(s => s.Stufe).ToList();
        if (stufen.Select(s => s.Stufe).Distinct().Count() != stufen.Count || stufen.Any(s => s.TageNachVorstufe < 1 || s.GebuehrCent < 0))
        {
            throw new ArgumentException("Mahnstufen brauchen eindeutige Nummern, mindestens einen Tag Abstand und keine negativen Gebühren.");
        }

        Kontoinhaber = kontoinhaber.Trim();
        Anschrift = anschrift.Trim();
        Iban = Finanzen.Iban.Normalisieren(iban);
        Bic = string.IsNullOrWhiteSpace(bic) ? null : bic.Trim().ToUpperInvariant();
        GlaeubigerId = glaeubigerId.Trim().ToUpperInvariant();
        Steuernummer = string.IsNullOrWhiteSpace(steuernummer) ? null : steuernummer.Trim();
        UstId = string.IsNullOrWhiteSpace(ustId) ? null : ustId.Trim();
        Steuerhinweis = steuerhinweis.Trim();
        ZahlungszielTage = zahlungszielTage;
        VorabankuendigungTage = vorabankuendigungTage;
        RuecklastschriftGebuehrCent = ruecklastschriftGebuehrCent;
        Mahnstufen = stufen;
    }
}

public enum Mandatsstatus
{
    Aktiv,
    Widerrufen,
}

/// <summary>SEPA-Lastschriftmandat (Basislastschrift, wiederkehrend).</summary>
public sealed class SepaMandat
{
    private SepaMandat()
    {
        Referenz = string.Empty;
        Kontoinhaber = string.Empty;
        Iban = string.Empty;
    }

    public SepaMandat(Guid personId, string referenz, string kontoinhaber, string iban, string? bic, DateOnly unterschriebenAm, DateTimeOffset jetzt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenz);
        ArgumentException.ThrowIfNullOrWhiteSpace(kontoinhaber);
        if (Finanzen.Iban.Fehler(iban) is { } fehler)
        {
            throw new ArgumentException(fehler);
        }

        Id = Ids.Neu();
        PersonId = personId;
        Referenz = referenz;
        Kontoinhaber = kontoinhaber.Trim();
        Iban = Finanzen.Iban.Normalisieren(iban);
        Bic = string.IsNullOrWhiteSpace(bic) ? null : bic.Trim().ToUpperInvariant();
        UnterschriebenAm = unterschriebenAm;
        ErfasstAm = jetzt;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    /// <summary>Mandatsreferenz (eindeutig je Gläubiger).</summary>
    public string Referenz { get; private set; }

    public string Kontoinhaber { get; private set; }

    public string Iban { get; private set; }

    public string? Bic { get; private set; }

    public DateOnly UnterschriebenAm { get; private set; }

    public DateTimeOffset ErfasstAm { get; private set; }

    public Mandatsstatus Status { get; private set; }

    public DateOnly? WiderrufenAm { get; private set; }

    /// <summary>Erster Einzug: Sequenz FRST, danach RCUR.</summary>
    public DateOnly? ErsteNutzungAm { get; private set; }

    public DateOnly? LetzteNutzungAm { get; private set; }

    /// <summary>Ein Mandat verfällt, wenn es 36 Monate nicht genutzt wurde.</summary>
    public bool NutzbarAm(DateOnly tag) => Status == Mandatsstatus.Aktiv && UnterschriebenAm <= tag
                                          && (LetzteNutzungAm ?? UnterschriebenAm).AddMonths(36) >= tag;

    public string Sequenz => ErsteNutzungAm is null ? "FRST" : "RCUR";

    public void Genutzt(DateOnly am)
    {
        ErsteNutzungAm ??= am;
        LetzteNutzungAm = am;
    }

    public void Widerrufen(DateOnly am)
    {
        Status = Mandatsstatus.Widerrufen;
        WiderrufenAm = am;
    }

    public void Anonymisieren(string platzhalter)
    {
        Kontoinhaber = platzhalter;
        Iban = "anonymisiert";
        Bic = null;
        if (Status == Mandatsstatus.Aktiv)
        {
            Widerrufen(DateOnly.FromDateTime(DateTime.Today));
        }
    }
}

/// <summary>Konto für Auszahlungen (Honorare, Erstattungen) – eines je Person.</summary>
public sealed class Bankverbindung
{
    private Bankverbindung()
    {
        Kontoinhaber = string.Empty;
        Iban = string.Empty;
    }

    public Bankverbindung(Guid personId, string kontoinhaber, string iban, string? bic)
    {
        PersonId = personId;
        Kontoinhaber = string.Empty;
        Iban = string.Empty;
        Aendern(kontoinhaber, iban, bic);
    }

    public Guid PersonId { get; private set; }

    public string Kontoinhaber { get; private set; }

    public string Iban { get; private set; }

    public string? Bic { get; private set; }

    public void Aendern(string kontoinhaber, string iban, string? bic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kontoinhaber);
        if (Finanzen.Iban.Fehler(iban) is { } fehler)
        {
            throw new ArgumentException(fehler);
        }

        Kontoinhaber = kontoinhaber.Trim();
        Iban = Finanzen.Iban.Normalisieren(iban);
        Bic = string.IsNullOrWhiteSpace(bic) ? null : bic.Trim().ToUpperInvariant();
    }

    public void Anonymisieren(string platzhalter)
    {
        Kontoinhaber = platzhalter;
        Iban = "anonymisiert";
        Bic = null;
    }
}

public enum Zahlungslaufart
{
    /// <summary>SEPA-Basislastschrift (pain.008).</summary>
    Lastschrift,

    /// <summary>SEPA-Überweisung (pain.001), z. B. Honorare und Erstattungen.</summary>
    Ueberweisung,
}

public enum Zahlungslaufstatus
{
    /// <summary>Von der Automatik zusammengestellt, wartet auf Freigabe.</summary>
    Vorschlag,

    /// <summary>Freigegeben; bei Lastschriften sind die Vorabankündigungen verschickt.</summary>
    Freigegeben,

    /// <summary>SEPA-Datei erzeugt und zur Bank gegeben.</summary>
    Exportiert,

    Verworfen,
}

public sealed record Zahlungsposten(Guid BelegId, string BelegNummer, Guid PersonId, string Name, string Iban, string? Bic, long BetragCent,
    string Verwendungszweck, string EndToEndId, string? MandatReferenz = null, DateOnly? MandatDatum = null, string? Sequenz = null,
    Guid? MandatId = null, bool Ausgeschlossen = false);

/// <summary>Sammlung von Lastschriften oder Überweisungen, die gemeinsam freigegeben und als SEPA-Datei exportiert werden.</summary>
public sealed class Zahlungslauf
{
    private Zahlungslauf()
    {
        ErstelltVon = string.Empty;
        NachrichtId = string.Empty;
    }

    public Zahlungslauf(Zahlungslaufart art, DateOnly ausfuehrung, IEnumerable<Zahlungsposten> posten, DateTimeOffset jetzt, string von)
    {
        Id = Ids.Neu();
        Art = art;
        Ausfuehrung = ausfuehrung;
        Posten = posten.ToList();
        ErstelltAm = jetzt;
        ErstelltVon = von;
        NachrichtId = $"VHS-{(art == Zahlungslaufart.Lastschrift ? "LS" : "UE")}-{jetzt:yyyyMMddHHmmss}-{Id.ToString("N")[..6]}";
        Status = Zahlungslaufstatus.Vorschlag;
    }

    public Guid Id { get; private set; }

    public Zahlungslaufart Art { get; private set; }

    public Zahlungslaufstatus Status { get; private set; }

    /// <summary>Fälligkeit (Lastschrift) bzw. gewünschtes Ausführungsdatum (Überweisung).</summary>
    public DateOnly Ausfuehrung { get; private set; }

    public string NachrichtId { get; private set; }

    public List<Zahlungsposten> Posten { get; private set; } = [];

    public DateTimeOffset ErstelltAm { get; private set; }

    public string ErstelltVon { get; private set; }

    public DateTimeOffset? FreigegebenAm { get; private set; }

    public string? FreigegebenVon { get; private set; }

    public DateTimeOffset? ExportiertAm { get; private set; }

    public string? Xml { get; private set; }

    public IEnumerable<Zahlungsposten> Aktiv => Posten.Where(p => !p.Ausgeschlossen);

    public long SummeCent => Aktiv.Sum(p => p.BetragCent);

    public void Ausschliessen(Guid belegId, bool ausgeschlossen)
    {
        if (Status != Zahlungslaufstatus.Vorschlag)
        {
            throw new InvalidOperationException("Nach der Freigabe ändert sich der Lauf nicht mehr.");
        }

        Posten = Posten.Select(p => p.BelegId == belegId ? p with { Ausgeschlossen = ausgeschlossen } : p).ToList();
    }

    public void Freigeben(DateOnly ausfuehrung, DateTimeOffset jetzt, string von)
    {
        if (Status != Zahlungslaufstatus.Vorschlag)
        {
            throw new InvalidOperationException("Nur ein Vorschlag kann freigegeben werden.");
        }

        if (!Aktiv.Any())
        {
            throw new InvalidOperationException("Der Lauf enthält keine Posten.");
        }

        Ausfuehrung = ausfuehrung;
        Status = Zahlungslaufstatus.Freigegeben;
        FreigegebenAm = jetzt;
        FreigegebenVon = von;
    }

    public void Exportieren(string xml, DateTimeOffset jetzt)
    {
        if (Status != Zahlungslaufstatus.Freigegeben)
        {
            throw new InvalidOperationException("Exportiert wird erst nach der Freigabe.");
        }

        Xml = xml;
        ExportiertAm = jetzt;
        Status = Zahlungslaufstatus.Exportiert;
    }

    public void Verwerfen()
    {
        if (Status is Zahlungslaufstatus.Exportiert)
        {
            throw new InvalidOperationException("Ein exportierter Lauf ist bei der Bank; Korrekturen über Rückbuchung.");
        }

        Status = Zahlungslaufstatus.Verworfen;
    }
}

public enum Umsatzstatus
{
    Offen,
    Zugeordnet,
    Ignoriert,
}

public enum Zuordnungssicherheit
{
    Keine,
    Niedrig,
    Mittel,
    Sicher,
}

/// <summary>Eingelesener Kontoauszug (camt.053).</summary>
public sealed class Kontoauszug
{
    private Kontoauszug()
    {
        Datei = string.Empty;
        Sha256 = string.Empty;
        AuszugId = string.Empty;
        EingelesenVon = string.Empty;
    }

    public Kontoauszug(string datei, string sha256, string auszugId, string? iban, DateOnly? von, DateOnly? bis, long? endsaldoCent, DateTimeOffset jetzt, string wer)
    {
        Id = Ids.Neu();
        Datei = datei;
        Sha256 = sha256;
        AuszugId = auszugId;
        Iban = iban;
        Von = von;
        Bis = bis;
        EndsaldoCent = endsaldoCent;
        EingelesenAm = jetzt;
        EingelesenVon = wer;
    }

    public Guid Id { get; private set; }

    public string Datei { get; private set; }

    public string Sha256 { get; private set; }

    public string AuszugId { get; private set; }

    public string? Iban { get; private set; }

    public DateOnly? Von { get; private set; }

    public DateOnly? Bis { get; private set; }

    public long? EndsaldoCent { get; private set; }

    public DateTimeOffset EingelesenAm { get; private set; }

    public string EingelesenVon { get; private set; }
}

/// <summary>Ein Umsatz aus dem Kontoauszug mit Zuordnungsvorschlag.</summary>
public sealed class Umsatz
{
    private Umsatz()
    {
        Verwendungszweck = string.Empty;
    }

    public Umsatz(Guid kontoauszugId, int nr, DateOnly buchungstag, DateOnly? valuta, long betragCent, string? gegenName, string? gegenIban,
        string verwendungszweck, string? endToEndId, string? rueckgabegrund)
    {
        Id = Ids.Neu();
        KontoauszugId = kontoauszugId;
        Nr = nr;
        Buchungstag = buchungstag;
        Valuta = valuta;
        BetragCent = betragCent;
        GegenName = gegenName;
        GegenIban = gegenIban;
        Verwendungszweck = verwendungszweck;
        EndToEndId = string.IsNullOrWhiteSpace(endToEndId) || endToEndId == "NOTPROVIDED" ? null : endToEndId;
        Rueckgabegrund = rueckgabegrund;
    }

    public Guid Id { get; private set; }

    public Guid KontoauszugId { get; private set; }

    public int Nr { get; private set; }

    public DateOnly Buchungstag { get; private set; }

    public DateOnly? Valuta { get; private set; }

    /// <summary>Positiv = Gutschrift auf dem VHS-Konto, negativ = Belastung.</summary>
    public long BetragCent { get; private set; }

    public string? GegenName { get; private set; }

    public string? GegenIban { get; private set; }

    public string Verwendungszweck { get; private set; }

    public string? EndToEndId { get; private set; }

    /// <summary>Rücklastschrift: ISO-Rückgabegrund (z. B. AC04 Konto erloschen, MD06 Widerspruch).</summary>
    public string? Rueckgabegrund { get; private set; }

    public Umsatzstatus Status { get; private set; }

    public Guid? VorschlagBelegId { get; private set; }

    public Zuordnungssicherheit Sicherheit { get; private set; }

    public string? VorschlagText { get; private set; }

    public Guid? ZugeordnetBelegId { get; private set; }

    /// <summary>Der beim Zuordnen erzeugte Beleg (Zahlungseingang, Zahlungsausgang oder Storno).</summary>
    public Guid? ErzeugterBelegId { get; private set; }

    public DateTimeOffset? ZugeordnetAm { get; private set; }

    public string? ZugeordnetVon { get; private set; }

    public void Vorschlagen(Guid? belegId, Zuordnungssicherheit sicherheit, string text)
    {
        if (Status != Umsatzstatus.Offen)
        {
            return;
        }

        VorschlagBelegId = belegId;
        Sicherheit = belegId is null ? Zuordnungssicherheit.Keine : sicherheit;
        VorschlagText = text;
    }

    public void Zuordnen(Guid zielBelegId, Guid erzeugterBelegId, DateTimeOffset jetzt, string von)
    {
        if (Status != Umsatzstatus.Offen)
        {
            throw new InvalidOperationException("Der Umsatz ist bereits bearbeitet.");
        }

        Status = Umsatzstatus.Zugeordnet;
        ZugeordnetBelegId = zielBelegId;
        ErzeugterBelegId = erzeugterBelegId;
        ZugeordnetAm = jetzt;
        ZugeordnetVon = von;
    }

    public void Ignorieren(string grund, DateTimeOffset jetzt, string von)
    {
        if (Status != Umsatzstatus.Offen)
        {
            throw new InvalidOperationException("Der Umsatz ist bereits bearbeitet.");
        }

        Status = Umsatzstatus.Ignoriert;
        VorschlagText = grund;
        ZugeordnetAm = jetzt;
        ZugeordnetVon = von;
    }

    public void Anonymisieren()
    {
        GegenName = "anonymisiert";
        GegenIban = null;
    }
}

public enum Mahnlaufstatus
{
    Vorschlag,
    Freigegeben,
    Verworfen,
}

public sealed record Mahnposten(Guid RechnungId, string RechnungNummer, Guid? PersonId, Guid? OrganisationId, string Name, int Stufe, string Stufenname,
    long OffenCent, long GebuehrCent, DateOnly Faellig, bool Ausgeschlossen = false, Guid? MahnungId = null);

/// <summary>Mahnvorschlag: wird von der Automatik erstellt und von der Buchhaltung freigegeben (QA-32).</summary>
public sealed class Mahnlauf
{
    private Mahnlauf()
    {
        ErstelltVon = string.Empty;
    }

    public Mahnlauf(IEnumerable<Mahnposten> posten, DateTimeOffset jetzt, string von)
    {
        Id = Ids.Neu();
        Posten = posten.ToList();
        ErstelltAm = jetzt;
        ErstelltVon = von;
    }

    public Guid Id { get; private set; }

    public Mahnlaufstatus Status { get; private set; }

    public List<Mahnposten> Posten { get; private set; } = [];

    public DateTimeOffset ErstelltAm { get; private set; }

    public string ErstelltVon { get; private set; }

    public DateTimeOffset? FreigegebenAm { get; private set; }

    public string? FreigegebenVon { get; private set; }

    public IEnumerable<Mahnposten> Aktiv => Posten.Where(p => !p.Ausgeschlossen);

    public void Ausschliessen(Guid rechnungId, bool ausgeschlossen)
    {
        if (Status != Mahnlaufstatus.Vorschlag)
        {
            throw new InvalidOperationException("Nach der Freigabe ändert sich der Mahnlauf nicht mehr.");
        }

        Posten = Posten.Select(p => p.RechnungId == rechnungId ? p with { Ausgeschlossen = ausgeschlossen } : p).ToList();
    }

    public void Freigeben(IReadOnlyDictionary<Guid, Guid> mahnungen, DateTimeOffset jetzt, string von)
    {
        if (Status != Mahnlaufstatus.Vorschlag)
        {
            throw new InvalidOperationException("Nur ein Vorschlag kann freigegeben werden.");
        }

        Posten = Posten.Select(p => mahnungen.TryGetValue(p.RechnungId, out var m) ? p with { MahnungId = m } : p).ToList();
        Status = Mahnlaufstatus.Freigegeben;
        FreigegebenAm = jetzt;
        FreigegebenVon = von;
    }

    public void Verwerfen()
    {
        if (Status != Mahnlaufstatus.Vorschlag)
        {
            throw new InvalidOperationException("Nur ein Vorschlag kann verworfen werden.");
        }

        Status = Mahnlaufstatus.Verworfen;
    }
}

/// <summary>
/// Honorarvertrag einer freiberuflichen Kursleitung: welches Honorarmodell (Plugin) mit welchen Parametern gilt, allgemein
/// oder für einen Kurs. Änderungen erzeugen eine neue Vertragsversion; die alte endet am Vortag (Historie bleibt erhalten).
/// </summary>
public sealed class Honorarvertrag
{
    private Honorarvertrag()
    {
        Bezeichnung = string.Empty;
        Modell = string.Empty;
    }

    public Honorarvertrag(Guid personId, Guid? kursId, string bezeichnung, string modell, IReadOnlyDictionary<string, string> parameter, DateOnly gueltigAb,
        DateOnly? gueltigBis, DateTimeOffset jetzt, string von)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modell);
        if (gueltigBis < gueltigAb)
        {
            throw new ArgumentException("Das Vertragsende liegt vor dem Beginn.");
        }

        Id = Ids.Neu();
        PersonId = personId;
        KursId = kursId;
        Bezeichnung = string.IsNullOrWhiteSpace(bezeichnung) ? "Honorarvertrag" : bezeichnung.Trim();
        Modell = modell;
        Parameter = new Dictionary<string, string>(parameter);
        GueltigAb = gueltigAb;
        GueltigBis = gueltigBis;
        ErfasstAm = jetzt;
        ErfasstVon = von;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    /// <summary>Leer = gilt für alle Kurse der Person.</summary>
    public Guid? KursId { get; private set; }

    public string Bezeichnung { get; private set; }

    /// <summary>Kennung des Honorarmodells (Plugin).</summary>
    public string Modell { get; private set; }

    /// <summary>Parameterwerte (invariante Schreibweise, z. B. "32.50").</summary>
    /// <remarks>Gespeichert als eigene Tabelle (je Eintrag eine Zeile).</remarks>
    public IReadOnlyDictionary<string, string> Parameter
    {
        get => Gemeinsam.Listenwerte.Woerterbuch(_parameter);
        private set => _parameter = Gemeinsam.Listenwerte.Von(value);
    }

    private List<Gemeinsam.Eintrag> _parameter = [];

    public DateOnly GueltigAb { get; private set; }

    public DateOnly? GueltigBis { get; private set; }

    public DateTimeOffset ErfasstAm { get; private set; }

    public string? ErfasstVon { get; private set; }

    public bool GiltAm(DateOnly tag, Guid kursId) => GueltigAb <= tag && (GueltigBis is null || tag <= GueltigBis) && (KursId is null || KursId == kursId);

    public void Beenden(DateOnly bis)
    {
        if (bis < GueltigAb.AddDays(-1))
        {
            throw new ArgumentException("Das Vertragsende liegt vor dem Beginn.");
        }

        GueltigBis = bis;
    }

    /// <summary>Kursbezogener Vertrag hat Vorrang vor dem allgemeinen.</summary>
    public static Honorarvertrag? Passend(IEnumerable<Honorarvertrag> vertraege, DateOnly tag, Guid kursId)
        => vertraege.Where(v => v.GiltAm(tag, kursId) && v.GueltigBis != v.GueltigAb.AddDays(-1))
            .OrderByDescending(v => v.KursId is not null).ThenByDescending(v => v.GueltigAb).FirstOrDefault();
}

/// <summary>Übergabe festgeschriebener Belege an die kommunale Finanzsoftware (Buchungsstapel).</summary>
public sealed class Uebergabe
{
    private Uebergabe()
    {
        Datei = string.Empty;
        Inhalt = string.Empty;
        Sha256 = string.Empty;
        Von = string.Empty;
    }

    public Uebergabe(int anzahl, long summeCent, string datei, string inhalt, string sha256, DateTimeOffset jetzt, string von)
    {
        Id = Ids.Neu();
        Anzahl = anzahl;
        SummeCent = summeCent;
        Datei = datei;
        Inhalt = inhalt;
        Sha256 = sha256;
        Am = jetzt;
        Von = von;
    }

    public Guid Id { get; private set; }

    public int Anzahl { get; private set; }

    public long SummeCent { get; private set; }

    public string Datei { get; private set; }

    public string Inhalt { get; private set; }

    public string Sha256 { get; private set; }

    public DateTimeOffset Am { get; private set; }

    public string Von { get; private set; }
}
