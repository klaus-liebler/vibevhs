using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

/// <summary>Eine Postadresse einer Person. Mehrere Adressen sind möglich; jede hat eine feste <see cref="AdressId"/>.</summary>
public sealed class Postadresse : BitemporaleVersion<Postadresse>
{
    private Postadresse()
    {
        Strasse = string.Empty;
        Postleitzahl = string.Empty;
        Ort = string.Empty;
        Land = "DE";
    }

    public Postadresse(Guid adressId, string? zusatz, string strasse, string postleitzahl, string ort, string land,
        DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strasse);
        ArgumentException.ThrowIfNullOrWhiteSpace(postleitzahl);
        ArgumentException.ThrowIfNullOrWhiteSpace(ort);
        AdressId = adressId;
        Zusatz = string.IsNullOrWhiteSpace(zusatz) ? null : zusatz.Trim();
        Strasse = strasse.Trim();
        Postleitzahl = postleitzahl.Trim();
        Ort = ort.Trim();
        Land = string.IsNullOrWhiteSpace(land) ? "DE" : land.Trim().ToUpperInvariant();
    }

    public Guid AdressId { get; private set; }

    /// <summary>Adresszusatz, z. B. "c/o Caritasverband".</summary>
    public string? Zusatz { get; private set; }

    public string Strasse { get; private set; }

    public string Postleitzahl { get; private set; }

    public string Ort { get; private set; }

    public string Land { get; private set; }

    public string Anzeige => string.Join(", ", new[] { Zusatz, Strasse, $"{Postleitzahl} {Ort}", Land == "DE" ? null : Land }
        .Where(t => !string.IsNullOrWhiteSpace(t)));

    public override string Datenart => "Adressen";

    public override string Feld => "Postadresse";

    public override string Gegenstand => AdressId.ToString("N");

    public override string Wert => IstEndemarke ? "(aufgegeben)" : Anzeige;

    protected internal override Postadresse KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(AdressId, Zusatz, Strasse, Postleitzahl, Ort, Land, gueltigVon, gueltigBis, erfassung);
}

public enum AdressKennzeichenArt
{
    /// <summary>Üblicher Aufenthaltsort (Wohnadresse, z. B. für Statistik und Integrationskurs-Träger).</summary>
    UeblicherAufenthaltsort,

    /// <summary>Standard-Korrespondenzadresse (wohin Briefe gehen).</summary>
    Korrespondenzadresse,
}

/// <summary>
/// Zeitlich gültiges Kennzeichen, welche Adresse der übliche Aufenthaltsort bzw. die Korrespondenzadresse ist.
/// Beide wechseln unabhängig voneinander. Der Adresstext wird zur Nachvollziehbarkeit mitgespeichert.
/// </summary>
public sealed class AdressKennzeichen : BitemporaleVersion<AdressKennzeichen>
{
    private AdressKennzeichen()
    {
        AdressAnzeige = string.Empty;
    }

    public AdressKennzeichen(AdressKennzeichenArt art, Guid adressId, string adressAnzeige, DateOnly gueltigVon, DateOnly? gueltigBis,
        Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        Art = art;
        AdressId = adressId;
        AdressAnzeige = adressAnzeige;
    }

    public AdressKennzeichenArt Art { get; private set; }

    public Guid AdressId { get; private set; }

    public string AdressAnzeige { get; private set; }

    public override string Datenart => "Adressen";

    public override string Feld => Art == AdressKennzeichenArt.UeblicherAufenthaltsort
        ? "Üblicher Aufenthaltsort"
        : "Standard-Korrespondenzadresse";

    public override string Gegenstand => Art.ToString();

    public override string Wert => IstEndemarke ? "(keine)" : AdressAnzeige;

    protected internal override AdressKennzeichen KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Art, AdressId, AdressAnzeige, gueltigVon, gueltigBis, erfassung);
}

public enum RufnummerArt
{
    Mobil,
    Festnetz,
    Dienstlich,
}

/// <summary>Über welche Dienste eine Nummer erreichbar ist.</summary>
[Flags]
public enum Messagingdienst
{
    Keiner = 0,
    Anruf = 1,
    Sms = 2,
    Signal = 4,
    WhatsApp = 8,
}

/// <summary>Telefon- bzw. Messaging-Nummer mit Einwilligung (DSGVO) und Kennzeichen "bevorzugt".</summary>
public sealed class Rufnummer : BitemporaleVersion<Rufnummer>
{
    private Rufnummer()
    {
        Nummer = string.Empty;
    }

    public Rufnummer(Guid nummerId, RufnummerArt art, string nummer, Messagingdienst dienste, bool einwilligung, bool bevorzugt,
        DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nummer);
        NummerId = nummerId;
        Art = art;
        Nummer = nummer.Trim();
        Dienste = dienste;
        Einwilligung = einwilligung;
        Bevorzugt = bevorzugt;
    }

    public Guid NummerId { get; private set; }

    public RufnummerArt Art { get; private set; }

    public string Nummer { get; private set; }

    public Messagingdienst Dienste { get; private set; }

    /// <summary>Einwilligung zur Kontaktaufnahme über Messaging-Dienste liegt vor.</summary>
    public bool Einwilligung { get; private set; }

    public bool Bevorzugt { get; private set; }

    public string DiensteAnzeige => Dienste == Messagingdienst.Keiner
        ? "–"
        : string.Join(", ", Enum.GetValues<Messagingdienst>()
            .Where(d => d != Messagingdienst.Keiner && Dienste.HasFlag(d))
            .Select(d => d switch { Messagingdienst.Sms => "SMS", Messagingdienst.WhatsApp => "WhatsApp", _ => d.ToString() }));

    public override string Datenart => "Telefon";

    public override string Feld => Art switch
    {
        RufnummerArt.Mobil => "Rufnummer (mobil)",
        RufnummerArt.Festnetz => "Rufnummer (Festnetz)",
        _ => "Rufnummer (dienstlich)",
    };

    public override string Gegenstand => NummerId.ToString("N");

    public override string Wert => IstEndemarke
        ? "(abgemeldet)"
        : $"{Nummer} · {DiensteAnzeige}{(Einwilligung ? " · Einwilligung" : " · ohne Einwilligung")}{(Bevorzugt ? " · bevorzugt" : string.Empty)}";

    protected internal override Rufnummer KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(NummerId, Art, Nummer, Dienste, Einwilligung, Bevorzugt, gueltigVon, gueltigBis, erfassung);
}

public enum EMailArt
{
    Privat,
    Dienstlich,
}

/// <summary>E-Mail-Adresse mit Kennzeichen "bevorzugt".</summary>
public sealed class EMailAdresse : BitemporaleVersion<EMailAdresse>
{
    private EMailAdresse()
    {
        Adresse = string.Empty;
    }

    public EMailAdresse(Guid adressId, EMailArt art, string adresse, bool bevorzugt, DateOnly gueltigVon, DateOnly? gueltigBis,
        Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adresse);
        AdressId = adressId;
        Art = art;
        Adresse = adresse.Trim();
        Bevorzugt = bevorzugt;
    }

    public Guid AdressId { get; private set; }

    public EMailArt Art { get; private set; }

    public string Adresse { get; private set; }

    public bool Bevorzugt { get; private set; }

    public override string Datenart => "E-Mail";

    public override string Feld => Art == EMailArt.Privat ? "E-Mail (privat)" : "E-Mail (dienstlich)";

    public override string Gegenstand => AdressId.ToString("N");

    public override string Wert => IstEndemarke ? "(aufgegeben)" : Adresse + (Bevorzugt ? " · bevorzugt" : string.Empty);

    protected internal override EMailAdresse KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(AdressId, Art, Adresse, Bevorzugt, gueltigVon, gueltigBis, erfassung);
}
