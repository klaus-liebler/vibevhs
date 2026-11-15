using System.Security.Cryptography;
using System.Text;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Finanzen;

public enum Belegart
{
    /// <summary>Forderung an Teilnehmende oder Firmenkunden (Kursentgelt, Gebühren).</summary>
    Rechnung,

    /// <summary>Minderung einer Forderung bzw. Erstattungsanspruch.</summary>
    Gutschrift,

    /// <summary>Eingegangene Zahlung (Überweisung, Lastschrift, bar).</summary>
    Zahlungseingang,

    /// <summary>Ausgezahlter Betrag (Honorar, Erstattung) bzw. Rücklastschrift.</summary>
    Zahlungsausgang,

    /// <summary>Mahnung mit Mahngebühr.</summary>
    Mahnung,

    /// <summary>Honorarabrechnung für freiberufliche Kursleitungen (Verbindlichkeit der VHS).</summary>
    Honorarabrechnung,

    /// <summary>Hebt einen festgeschriebenen Beleg vollständig auf.</summary>
    Storno,
}

public static class BelegartInfo
{
    public static string Kuerzel(Belegart art) => art switch
    {
        Belegart.Rechnung => "RE",
        Belegart.Gutschrift => "GS",
        Belegart.Zahlungseingang => "ZE",
        Belegart.Zahlungsausgang => "ZA",
        Belegart.Mahnung => "MA",
        Belegart.Honorarabrechnung => "HO",
        _ => "ST",
    };

    public static string Name(Belegart art) => art switch
    {
        Belegart.Rechnung => "Rechnung",
        Belegart.Gutschrift => "Gutschrift",
        Belegart.Zahlungseingang => "Zahlungseingang",
        Belegart.Zahlungsausgang => "Zahlungsausgang",
        Belegart.Mahnung => "Mahnung",
        Belegart.Honorarabrechnung => "Honorarabrechnung",
        _ => "Storno",
    };

    /// <summary>
    /// Wirkung auf das Personenkonto: +1 = die Person schuldet der VHS (Soll), −1 = die VHS schuldet der Person bzw. Zahlung erhalten (Haben).
    /// Ein Storno hat die umgekehrte Wirkung des stornierten Belegs.
    /// </summary>
    public static int Vorzeichen(Belegart art) => art switch
    {
        Belegart.Rechnung or Belegart.Mahnung or Belegart.Zahlungsausgang => 1,
        Belegart.Gutschrift or Belegart.Zahlungseingang or Belegart.Honorarabrechnung => -1,
        _ => 0,
    };

    /// <summary>Aufbewahrung: Buchungsbelege 8 Jahre, Geschäftsbriefe (Mahnungen) 6 Jahre (FA-130).</summary>
    public static string Frist(Belegart art) => art == Belegart.Mahnung ? "handelsbriefe" : "buchhaltung";
}

public enum Positionsart
{
    Sonstiges,
    Kursentgelt,
    Gebuehr,
    Mahngebuehr,
    Honorar,
    Zahlung,
}

/// <summary>Eine Zeile auf dem Beleg. Bezüge (Belegung, Veranstaltung) machen Doppelabrechnungen erkennbar.</summary>
public sealed class Belegposition
{
    private Belegposition()
    {
        Text = string.Empty;
    }

    public Belegposition(string text, long betragCent, decimal menge = 1, long? einzelCent = null, Guid? belegungId = null, Guid? kursId = null,
        Guid? veranstaltungId = null, Positionsart art = Positionsart.Sonstiges)
    {
        Art = art;
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text.Trim();
        BetragCent = betragCent;
        Menge = menge;
        EinzelCent = einzelCent ?? betragCent;
        BelegungId = belegungId;
        KursId = kursId;
        VeranstaltungId = veranstaltungId;
    }

    public int Nr { get; internal set; }

    public Positionsart Art { get; private set; }

    public string Text { get; private set; }

    public decimal Menge { get; private set; }

    public long EinzelCent { get; private set; }

    public long BetragCent { get; private set; }

    public Guid? BelegungId { get; private set; }

    public Guid? KursId { get; private set; }

    public Guid? VeranstaltungId { get; private set; }

    internal void Anonymisieren() => Text = BelegungId is null && VeranstaltungId is null ? Text : Text.Split(" – ")[0];
}

/// <summary>
/// Beleg nach GoBD (FA-126 bis FA-130). Ein Entwurf hat noch keine Nummer und darf verworfen werden. Mit der
/// Festschreibung bekommt er die nächste lückenlose Nummer seiner Belegart und eine Prüfsumme; danach ändert sich am
/// Inhalt nichts mehr – Korrekturen nur per Storno und Neubeleg.
/// </summary>
public sealed class Beleg
{
    private readonly List<Belegposition> _positionen = [];

    private Beleg()
    {
        ErfasstVon = string.Empty;
        EmpfaengerName = string.Empty;
    }

    public Beleg(Belegart art, DateOnly belegdatum, Guid? personId, Guid? organisationId, string empfaengerName, string? empfaengerAnschrift,
        IEnumerable<Belegposition> positionen, DateTimeOffset jetzt, string von, DateOnly? faellig = null, Guid? bezugBelegId = null, string? text = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(empfaengerName);
        Id = Ids.Neu();
        Art = art;
        Belegdatum = belegdatum;
        PersonId = personId;
        OrganisationId = organisationId;
        EmpfaengerName = empfaengerName;
        EmpfaengerAnschrift = empfaengerAnschrift;
        Faellig = faellig;
        BezugBelegId = bezugBelegId;
        Text = text;
        ErfasstAm = jetzt;
        ErfasstVon = von;
        var nr = 1;
        foreach (var p in positionen)
        {
            p.Nr = nr++;
            _positionen.Add(p);
        }

        if (_positionen.Count == 0)
        {
            throw new ArgumentException("Ein Beleg braucht mindestens eine Position.");
        }

        BetragCent = _positionen.Sum(p => p.BetragCent);
        if (BetragCent < 0)
        {
            throw new ArgumentException("Belegbeträge sind nie negativ; die Richtung ergibt sich aus der Belegart.");
        }
    }

    public Guid Id { get; private set; }

    public Belegart Art { get; private set; }

    /// <summary>z. B. RE-2026-00042; leer, solange der Beleg ein Entwurf ist.</summary>
    public string? Nummer { get; private set; }

    public int? Jahr { get; private set; }

    public int? Laufnummer { get; private set; }

    public DateOnly Belegdatum { get; private set; }

    public DateOnly? Faellig { get; private set; }

    /// <summary>Erfassungszeitpunkt (FA-129).</summary>
    public DateTimeOffset ErfasstAm { get; private set; }

    public string ErfasstVon { get; private set; }

    public DateTimeOffset? FestgeschriebenAm { get; private set; }

    public string? FestgeschriebenVon { get; private set; }

    public Guid? PersonId { get; private set; }

    public Guid? OrganisationId { get; private set; }

    /// <summary>Name und Anschrift zum Belegzeitpunkt (Belege bleiben unverändert, auch wenn sich die Adresse ändert).</summary>
    public string EmpfaengerName { get; private set; }

    public string? EmpfaengerAnschrift { get; private set; }

    public IReadOnlyList<Belegposition> Positionen => _positionen;

    public long BetragCent { get; private set; }

    /// <summary>Bezug: Storno → stornierter Beleg; Zahlung/Gutschrift/Mahnung → Rechnung; Zahlungsausgang → Honorar oder Zahlung.</summary>
    public Guid? BezugBelegId { get; private set; }

    public Guid? StorniertDurchId { get; private set; }

    /// <summary>Freitext, z. B. Storno-Begründung oder Zahlungsweg.</summary>
    public string? Text { get; private set; }

    /// <summary>SHA-256 über den festgeschriebenen Inhalt.</summary>
    public string? Sha256 { get; private set; }

    /// <summary>Archiviertes Druckstück (Dokumentarchiv mit Prüfsumme und Übersetzungs-QR).</summary>
    public Guid? DokumentId { get; private set; }

    /// <summary>XRechnung (UBL) bei Rechnungen an Organisationen.</summary>
    public string? Xml { get; private set; }

    public Guid? UebergabeId { get; private set; }

    /// <summary>
    /// Berechnungsgrundlage als Snapshot (JSON), z. B. Honorarmodell, Version und Parameter (GoBD: nachvollziehbar,
    /// auch wenn sich Vertrag oder Plugin später ändern).
    /// </summary>
    public string? Grundlage { get; private set; }

    public DateTimeOffset? AnonymisiertAm { get; private set; }

    public bool Festgeschrieben => Nummer is not null;

    public bool Storniert => StorniertDurchId is not null;

    /// <summary>Vorzeichenbehafteter Betrag auf dem Personenkonto (Storno: Gegenbuchung, Vorzeichen setzt der Aufrufer).</summary>
    public long KontoCent(Belegart? stornierteArt = null)
        => Art == Belegart.Storno ? -BelegartInfo.Vorzeichen(stornierteArt ?? Belegart.Rechnung) * BetragCent : BelegartInfo.Vorzeichen(Art) * BetragCent;

    public string Anzeige => Nummer ?? $"Entwurf {BelegartInfo.Name(Art)}";

    public void GrundlageSetzen(string grundlage)
    {
        if (Festgeschrieben)
        {
            throw new InvalidOperationException("Die Grundlage eines festgeschriebenen Belegs ändert sich nicht.");
        }

        Grundlage = grundlage;
    }

    /// <summary>Vergibt die Nummer und schreibt fest. Danach ist der Inhalt unveränderlich.</summary>
    public void Festschreiben(Nummernkreis kreis, DateTimeOffset jetzt, string von)
    {
        ArgumentNullException.ThrowIfNull(kreis);
        if (Festgeschrieben)
        {
            throw new InvalidOperationException($"{Nummer} ist bereits festgeschrieben.");
        }

        if (kreis.Art != Art || kreis.Jahr != Belegdatum.Year)
        {
            throw new InvalidOperationException("Der Nummernkreis passt nicht zu Belegart und Belegjahr.");
        }

        Laufnummer = kreis.Ziehen();
        Jahr = kreis.Jahr;
        Nummer = $"{BelegartInfo.Kuerzel(Art)}-{Jahr}-{Laufnummer:00000}";
        FestgeschriebenAm = jetzt;
        FestgeschriebenVon = von;
        Sha256 = Pruefsumme();
    }

    /// <summary>Druckstück und XRechnung gehören zum Beleg; sie werden einmalig bei der Festschreibung abgelegt.</summary>
    public void DokumenteAblegen(Guid? dokumentId, string? xml)
    {
        if (!Festgeschrieben)
        {
            throw new InvalidOperationException("Druckstücke gibt es erst für festgeschriebene Belege.");
        }

        if ((DokumentId is not null && dokumentId is not null) || (Xml is not null && xml is not null))
        {
            throw new InvalidOperationException($"Das Druckstück von {Nummer} liegt bereits vor und wird nicht ersetzt.");
        }

        DokumentId ??= dokumentId;
        Xml ??= xml;
    }

    /// <summary>Erzeugt den Stornobeleg (Entwurf; Festschreibung durch den Aufrufer).</summary>
    public Beleg StornoErzeugen(string grund, DateOnly belegdatum, DateTimeOffset jetzt, string von)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grund);
        if (!Festgeschrieben)
        {
            throw new InvalidOperationException("Ein Entwurf wird nicht storniert, sondern verworfen.");
        }

        if (Art == Belegart.Storno)
        {
            throw new InvalidOperationException("Ein Storno kann nicht storniert werden – bitte einen neuen Beleg erstellen.");
        }

        if (Storniert)
        {
            throw new InvalidOperationException($"{Nummer} ist bereits storniert.");
        }

        var storno = new Beleg(Belegart.Storno, belegdatum, PersonId, OrganisationId, EmpfaengerName, EmpfaengerAnschrift,
            [new Belegposition($"Storno {Nummer}: {grund.Trim()}", BetragCent)], jetzt, von, bezugBelegId: Id, text: grund.Trim());
        return storno;
    }

    public void StornoVermerken(Beleg storno)
    {
        ArgumentNullException.ThrowIfNull(storno);
        if (storno.Art != Belegart.Storno || storno.BezugBelegId != Id || !storno.Festgeschrieben)
        {
            throw new InvalidOperationException("Nur ein festgeschriebener Stornobeleg zu diesem Beleg hebt ihn auf.");
        }

        StorniertDurchId = storno.Id;
    }

    public void UebergabeVermerken(Guid uebergabeId)
    {
        if (!Festgeschrieben)
        {
            throw new InvalidOperationException("Nur festgeschriebene Belege werden übergeben.");
        }

        UebergabeId ??= uebergabeId;
    }

    /// <summary>Nach Ablauf aller Fristen (Löschlauf): Name, Anschrift und Druckstück entfernen. Nummer, Betrag und Prüfsumme bleiben.</summary>
    public void Anonymisieren(string platzhalter, DateTimeOffset jetzt)
    {
        EmpfaengerName = platzhalter;
        EmpfaengerAnschrift = null;
        Xml = null;
        foreach (var p in _positionen)
        {
            p.Anonymisieren();
        }

        AnonymisiertAm ??= jetzt;
    }

    /// <summary>Prüfsumme über alle steuerlich relevanten Inhalte (ohne Druckstück).</summary>
    public string Pruefsumme()
    {
        var sb = new StringBuilder();
        sb.Append(Nummer).Append('|').Append(Art).Append('|').Append(Belegdatum.ToString("yyyy-MM-dd")).Append('|').Append(Faellig?.ToString("yyyy-MM-dd"))
            .Append('|').Append(PersonId).Append('|').Append(OrganisationId).Append('|').Append(BetragCent).Append('|').Append(BezugBelegId)
            .Append('|').Append(ErfasstAm.ToUnixTimeMilliseconds()).Append('|').Append(Text).Append('|').Append(Grundlage);
        foreach (var p in _positionen)
        {
            sb.Append('|').Append(p.Nr).Append(';').Append(p.Menge.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                .Append(p.EinzelCent).Append(';').Append(p.BetragCent).Append(';').Append(p.BelegungId).Append(';').Append(p.VeranstaltungId);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

/// <summary>Lückenloser Nummernkreis je Belegart und Jahr (FA-127). Nummern werden erst bei der Festschreibung gezogen.</summary>
public sealed class Nummernkreis
{
    private Nummernkreis()
    {
    }

    public Nummernkreis(Belegart art, int jahr)
    {
        Art = art;
        Jahr = jahr;
    }

    public Belegart Art { get; private set; }

    public int Jahr { get; private set; }

    public int Letzte { get; private set; }

    /// <summary>Optimistische Sperre: Zwei gleichzeitige Festschreibungen können nicht dieselbe Nummer ziehen.</summary>
    public Guid Version { get; private set; } = Guid.NewGuid();

    public int Ziehen()
    {
        Letzte++;
        Version = Guid.NewGuid();
        return Letzte;
    }
}
