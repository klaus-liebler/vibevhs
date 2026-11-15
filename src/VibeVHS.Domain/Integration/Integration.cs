using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Integration;

public enum Integrationskursart
{
    Allgemein,
    Alphabetisierung,
    Jugend,
    Eltern,
    Frauen,
    Intensiv,
    Zweitschrift,
}

/// <summary>Kursabschnitt (Modul) mit geplanten Unterrichtseinheiten, z. B. „Modul 1“ (100 UE) oder „Orientierungskurs“.</summary>
public sealed record Kursabschnitt(int Nr, string Bezeichnung, int Ue);

public static class IntegrationskursVorlagen
{
    public static string Name(Integrationskursart art) => art switch
    {
        Integrationskursart.Allgemein => "Allgemeiner Integrationskurs",
        Integrationskursart.Alphabetisierung => "Integrationskurs mit Alphabetisierung",
        Integrationskursart.Jugend => "Jugendintegrationskurs",
        Integrationskursart.Eltern => "Elternintegrationskurs",
        Integrationskursart.Frauen => "Frauenintegrationskurs",
        Integrationskursart.Intensiv => "Intensivkurs",
        _ => "Zweitschriftlernerkurs",
    };

    /// <summary>Übliche Gliederung in Abschnitte zu 100 UE plus Orientierungskurs (Werte prüfen: Konzeption des BAMF).</summary>
    public static IReadOnlyList<Kursabschnitt> Abschnitte(Integrationskursart art)
    {
        var sprachModule = art switch
        {
            Integrationskursart.Intensiv => 4,
            Integrationskursart.Allgemein => 6,
            _ => 9,
        };
        var liste = Enumerable.Range(1, sprachModule).Select(i => new Kursabschnitt(i, $"Modul {i}", 100)).ToList();
        liste.Add(new Kursabschnitt(sprachModule + 1, "Orientierungskurs", 100));
        return liste;
    }
}

/// <summary>Abrechnung eines Abschnitts gegenüber dem BAMF (Beleg und Vorgangsnummer).</summary>
public sealed record Abschnittsabrechnung(int Nr, Guid BelegId, string Vorgang, DateTimeOffset Am);

/// <summary>BAMF-spezifische Angaben eines Integrationskurses (1:1 zum Kurs).</summary>
public sealed class Integrationskursdaten
{
    private Integrationskursdaten()
    {
        BamfKursnummer = string.Empty;
    }

    public Integrationskursdaten(Guid kursId, string bamfKursnummer, Integrationskursart art, IEnumerable<Kursabschnitt> abschnitte, long kostenerstattungCentJeUe,
        long kostenbeitragCentJeUe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bamfKursnummer);
        KursId = kursId;
        BamfKursnummer = bamfKursnummer.Trim();
        Art = art;
        Abschnitte = abschnitte.OrderBy(a => a.Nr).ToList();
        KostenerstattungCentJeUe = kostenerstattungCentJeUe;
        KostenbeitragCentJeUe = kostenbeitragCentJeUe;
    }

    public Guid KursId { get; private set; }

    public string BamfKursnummer { get; private set; }

    public Integrationskursart Art { get; private set; }

    public List<Kursabschnitt> Abschnitte { get; private set; } = [];

    /// <summary>Kostenerstattungssatz des BAMF je UE und Teilnehmer (Platzhalter, mit der aktuellen Festsetzung abgleichen).</summary>
    public long KostenerstattungCentJeUe { get; private set; }

    /// <summary>Kostenbeitrag der Teilnehmenden je UE, wenn nicht befreit.</summary>
    public long KostenbeitragCentJeUe { get; private set; }

    public DateTimeOffset? KursbeginnGemeldetAm { get; private set; }

    public string? KursbeginnVorgang { get; private set; }

    public List<Abschnittsabrechnung> Abrechnungen { get; private set; } = [];

    public void AbrechnungVermerken(int nr, Guid belegId, string vorgang, DateTimeOffset jetzt)
    {
        if (Abrechnungen.Any(a => a.Nr == nr))
        {
            throw new InvalidOperationException($"Abschnitt {nr} ist bereits abgerechnet.");
        }

        Abrechnungen = [.. Abrechnungen, new Abschnittsabrechnung(nr, belegId, vorgang, jetzt)];
    }

    public int GesamtUe => Abschnitte.Sum(a => a.Ue);

    /// <summary>Abschnitt, zu dem eine Unterrichtseinheit (laufende Nummer ab 1) gehört.</summary>
    public Kursabschnitt? AbschnittZu(decimal ueLaufend)
    {
        var bisher = 0m;
        foreach (var a in Abschnitte)
        {
            bisher += a.Ue;
            if (ueLaufend <= bisher)
            {
                return a;
            }
        }

        return null;
    }

    public void KursbeginnGemeldet(string vorgang, DateTimeOffset jetzt)
    {
        KursbeginnGemeldetAm ??= jetzt;
        KursbeginnVorgang ??= vorgang;
    }

    public void Aendern(string bamfKursnummer, Integrationskursart art, IEnumerable<Kursabschnitt> abschnitte, long erstattung, long beitrag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bamfKursnummer);
        BamfKursnummer = bamfKursnummer.Trim();
        Art = art;
        Abschnitte = abschnitte.OrderBy(a => a.Nr).ToList();
        KostenerstattungCentJeUe = erstattung;
        KostenbeitragCentJeUe = beitrag;
    }
}

public enum Berechtigungsart
{
    /// <summary>Berechtigungsschein des BAMF auf Antrag.</summary>
    Berechtigungsschein,

    /// <summary>Verpflichtung durch Ausländerbehörde oder Jobcenter.</summary>
    Verpflichtung,

    /// <summary>Zulassung durch das BAMF (z. B. Unionsbürger, Deutsche).</summary>
    Zulassung,
}

/// <summary>Teilnahmeberechtigung bzw. -verpflichtung mit Kostenbefreiung (nur Daten, keine Kopie des Bescheids).</summary>
public sealed class Teilnahmeberechtigung
{
    private Teilnahmeberechtigung()
    {
        Aktenzeichen = string.Empty;
        Stelle = string.Empty;
        ErfasstVon = string.Empty;
    }

    public Teilnahmeberechtigung(Guid personId, Berechtigungsart art, string aktenzeichen, string stelle, DateOnly ausgestellt, DateOnly? gueltigBis, int ueUmfang,
        bool kostenbefreit, string? befreiungsgrund, bool fahrtkosten, DateTimeOffset jetzt, string von)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aktenzeichen);
        ArgumentException.ThrowIfNullOrWhiteSpace(stelle);
        if (ueUmfang <= 0)
        {
            throw new ArgumentException("Der UE-Umfang muss größer als 0 sein.");
        }

        if (kostenbefreit && string.IsNullOrWhiteSpace(befreiungsgrund))
        {
            throw new ArgumentException("Eine Kostenbefreiung braucht einen Grund (z. B. Bezug von Bürgergeld).");
        }

        Id = Ids.Neu();
        PersonId = personId;
        Art = art;
        Aktenzeichen = aktenzeichen.Trim();
        Stelle = stelle.Trim();
        Ausgestellt = ausgestellt;
        GueltigBis = gueltigBis;
        UeUmfang = ueUmfang;
        Kostenbefreit = kostenbefreit;
        Befreiungsgrund = string.IsNullOrWhiteSpace(befreiungsgrund) ? null : befreiungsgrund.Trim();
        Fahrtkosten = fahrtkosten;
        ErfasstAm = jetzt;
        ErfasstVon = von;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public Berechtigungsart Art { get; private set; }

    /// <summary>BAMF-Teilnehmernummer bzw. Aktenzeichen der ausstellenden Stelle.</summary>
    public string Aktenzeichen { get; private set; }

    /// <summary>Ausstellende Stelle (BAMF, Ausländerbehörde, Jobcenter).</summary>
    public string Stelle { get; private set; }

    public DateOnly Ausgestellt { get; private set; }

    /// <summary>Bis wann der Kurs begonnen sein muss (Berechtigungsschein: in der Regel ein Jahr).</summary>
    public DateOnly? GueltigBis { get; private set; }

    public int UeUmfang { get; private set; }

    public bool Kostenbefreit { get; private set; }

    public string? Befreiungsgrund { get; private set; }

    /// <summary>Anspruch auf Fahrtkostenerstattung (nur bei Kostenbefreiung und ab 3 km Entfernung).</summary>
    public bool Fahrtkosten { get; private set; }

    public DateTimeOffset ErfasstAm { get; private set; }

    public string ErfasstVon { get; private set; }

    public bool GiltZuKursbeginn(DateOnly beginn) => GueltigBis is null || beginn <= GueltigBis;

    public void Anonymisieren()
    {
        Aktenzeichen = "anonymisiert";
        Befreiungsgrund = null;
    }
}

public enum Ausweisart
{
    Personalausweis,
    Reisepass,
    Aufenthaltstitel,
    Ankunftsnachweis,
    Duldung,
    Aufenthaltsgestattung,
    Sonstiges,
}

/// <summary>
/// Identitätsprüfung am Originaldokument (FA-134): nur Datum, prüfende Person, Dokumentart und Ergebnis – keine Kopie,
/// keine Ausweisnummer.
/// </summary>
public sealed class Identitaetspruefung
{
    private Identitaetspruefung()
    {
        GeprueftVon = string.Empty;
    }

    public Identitaetspruefung(Guid personId, Ausweisart dokument, bool stimmtUeberein, string? bemerkung, DateTimeOffset jetzt, string von)
    {
        Id = Ids.Neu();
        PersonId = personId;
        Dokument = dokument;
        StimmtUeberein = stimmtUeberein;
        Bemerkung = string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim();
        GeprueftAm = jetzt;
        GeprueftVon = von;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public Ausweisart Dokument { get; private set; }

    /// <summary>Stimmen Ausweis und Berechtigung überein (Name, Geburtsdatum)?</summary>
    public bool StimmtUeberein { get; private set; }

    public string? Bemerkung { get; private set; }

    public DateTimeOffset GeprueftAm { get; private set; }

    public string GeprueftVon { get; private set; }

    public void Anonymisieren() => Bemerkung = null;
}

/// <summary>
/// Lichtbild nur für den Sichtvergleich beim Check-in (FA-135): wird angezeigt, nie technisch abgeglichen, auf dem
/// Gerät der Kursleitung nicht gespeichert und nach Kursende plus Frist gelöscht.
/// </summary>
public sealed class Lichtbild
{
    private Lichtbild()
    {
        Inhalt = [];
        Typ = string.Empty;
        AufgenommenVon = string.Empty;
    }

    public Lichtbild(Guid personId, Guid kursId, byte[] inhalt, string typ, DateOnly loeschenAb, DateTimeOffset jetzt, string von)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        if (inhalt.Length == 0 || inhalt.Length > 400_000)
        {
            throw new ArgumentException("Das Lichtbild muss zwischen 1 Byte und 400 KB groß sein.");
        }

        if (typ is not ("image/jpeg" or "image/png" or "image/webp"))
        {
            throw new ArgumentException("Nur JPEG, PNG oder WebP.");
        }

        Id = Ids.Neu();
        PersonId = personId;
        KursId = kursId;
        Inhalt = inhalt;
        Typ = typ;
        LoeschenAb = loeschenAb;
        AufgenommenAm = jetzt;
        AufgenommenVon = von;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid KursId { get; private set; }

    public byte[] Inhalt { get; private set; }

    public string Typ { get; private set; }

    public DateOnly LoeschenAb { get; private set; }

    public DateTimeOffset AufgenommenAm { get; private set; }

    public string AufgenommenVon { get; private set; }

    public DateTimeOffset? GeloeschtAm { get; private set; }

    public bool Vorhanden => GeloeschtAm is null;

    public void Loeschen(DateTimeOffset jetzt)
    {
        Inhalt = [];
        GeloeschtAm ??= jetzt;
    }
}

public enum Fahrtkostenstatus
{
    Beantragt,
    Bewilligt,
    Abgelehnt,
}

/// <summary>Fahrtkostenerstattung für kostenbefreite Teilnehmende (FA-42): je Monat, ab 3 km Entfernung, Erstattung durch das BAMF.</summary>
public sealed class IkFahrtkosten
{
    private IkFahrtkosten()
    {
        Begruendung = string.Empty;
    }

    public IkFahrtkosten(Guid personId, Guid kursId, int jahr, int monat, decimal entfernungKm, long betragCent, string begruendung, DateTimeOffset jetzt)
    {
        if (betragCent <= 0)
        {
            throw new ArgumentException("Der Betrag muss größer als 0 sein.");
        }

        Id = Ids.Neu();
        PersonId = personId;
        KursId = kursId;
        Jahr = jahr;
        Monat = monat;
        EntfernungKm = entfernungKm;
        BetragCent = betragCent;
        Begruendung = begruendung;
        BeantragtAm = jetzt;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid KursId { get; private set; }

    public int Jahr { get; private set; }

    public int Monat { get; private set; }

    public decimal EntfernungKm { get; private set; }

    public long BetragCent { get; private set; }

    public string Begruendung { get; private set; }

    public Fahrtkostenstatus Status { get; private set; }

    public DateTimeOffset BeantragtAm { get; private set; }

    public Guid? GutschriftId { get; private set; }

    public void Entscheiden(bool bewilligt, string? grund, Guid? gutschriftId)
    {
        if (Status != Fahrtkostenstatus.Beantragt)
        {
            throw new InvalidOperationException("Der Antrag ist bereits entschieden.");
        }

        Status = bewilligt ? Fahrtkostenstatus.Bewilligt : Fahrtkostenstatus.Abgelehnt;
        GutschriftId = gutschriftId;
        if (!bewilligt && !string.IsNullOrWhiteSpace(grund))
        {
            Begruendung = grund.Trim();
        }
    }
}

public enum Pruefungsart
{
    /// <summary>Deutsch-Test für Zuwanderer (A2/B1).</summary>
    Dtz,

    /// <summary>Test „Leben in Deutschland“ (Abschluss Orientierungskurs).</summary>
    Lid,
}

/// <summary>Prüfungstermin DTZ oder LiD (Mock-Anbindung an Prüfungsstelle/BAMF).</summary>
public sealed class Pruefungstermin
{
    private Pruefungstermin()
    {
        Ort = string.Empty;
    }

    public Pruefungstermin(Pruefungsart art, DateTime beginn, string ort, Guid? kursId)
    {
        Id = Ids.Neu();
        Art = art;
        Beginn = beginn;
        Ort = ort;
        KursId = kursId;
    }

    public Guid Id { get; private set; }

    public Pruefungsart Art { get; private set; }

    public DateTime Beginn { get; private set; }

    public string Ort { get; private set; }

    public Guid? KursId { get; private set; }

    public DateTimeOffset? GemeldetAm { get; private set; }

    public void Gemeldet(DateTimeOffset jetzt) => GemeldetAm ??= jetzt;
}

public enum Pruefungsergebnis
{
    Offen,

    /// <summary>DTZ: Niveau B1 erreicht.</summary>
    B1,

    /// <summary>DTZ: Niveau A2 erreicht.</summary>
    A2,

    /// <summary>DTZ: unter A2.</summary>
    UnterA2,

    /// <summary>LiD: bestanden (mindestens 15 von 33 Punkten).</summary>
    Bestanden,

    NichtBestanden,

    NichtErschienen,
}

public sealed class Pruefungsanmeldung
{
    private Pruefungsanmeldung()
    {
    }

    public Pruefungsanmeldung(Guid pruefungId, Guid personId, DateTimeOffset jetzt)
    {
        Id = Ids.Neu();
        PruefungId = pruefungId;
        PersonId = personId;
        AngemeldetAm = jetzt;
    }

    public Guid Id { get; private set; }

    public Guid PruefungId { get; private set; }

    public Guid PersonId { get; private set; }

    public DateTimeOffset AngemeldetAm { get; private set; }

    public Pruefungsergebnis Ergebnis { get; private set; }

    public int? Punkte { get; private set; }

    public void ErgebnisSetzen(Pruefungsart art, Pruefungsergebnis ergebnis, int? punkte)
    {
        var passend = art == Pruefungsart.Dtz
            ? ergebnis is Pruefungsergebnis.B1 or Pruefungsergebnis.A2 or Pruefungsergebnis.UnterA2 or Pruefungsergebnis.NichtErschienen
            : ergebnis is Pruefungsergebnis.Bestanden or Pruefungsergebnis.NichtBestanden or Pruefungsergebnis.NichtErschienen;
        if (!passend)
        {
            throw new ArgumentException($"Das Ergebnis „{ergebnis}“ passt nicht zur Prüfung {art}.");
        }

        if (art == Pruefungsart.Lid && punkte is { } p && (p < 0 || p > 33))
        {
            throw new ArgumentException("Beim Test „Leben in Deutschland“ gibt es 0 bis 33 Punkte.");
        }

        if (art == Pruefungsart.Lid && punkte is { } q && ergebnis != Pruefungsergebnis.NichtErschienen && (q >= 15) != (ergebnis == Pruefungsergebnis.Bestanden))
        {
            throw new ArgumentException("Bestanden ist der Test ab 15 von 33 Punkten.");
        }

        Ergebnis = ergebnis;
        Punkte = punkte;
    }
}
