using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

public enum Organisationsart
{
    Firma,
    Behoerde,
    Jobcenter,
    Verein,
    Schule,
    Sonstige,
}

/// <summary>Organisation (Firma, Behörde, Jobcenter …), z. B. als Auftraggeber geschlossener Kurse oder Kostenträger.</summary>
public sealed class Organisation
{
    private Organisation()
    {
        Name = string.Empty;
    }

    public Organisation(string name, Organisationsart art, string? ort = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = Ids.Neu();
        Name = name.Trim();
        Art = art;
        Ort = string.IsNullOrWhiteSpace(ort) ? null : ort.Trim();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public Organisationsart Art { get; private set; }

    public string? Ort { get; private set; }

    /// <summary>Rechnungsanschrift (mehrzeilig) für Firmenkunden.</summary>
    public string? Rechnungsanschrift { get; private set; }

    /// <summary>Leitweg-ID öffentlicher Auftraggeber (XRechnung, BT-10); sonst eine Bestellnummer o. Ä.</summary>
    public string? LeitwegId { get; private set; }

    /// <summary>Elektronische Adresse für XRechnungen.</summary>
    public string? RechnungsEMail { get; private set; }

    public void RechnungsdatenSetzen(string? anschrift, string? leitwegId, string? email)
    {
        Rechnungsanschrift = string.IsNullOrWhiteSpace(anschrift) ? null : anschrift.Trim();
        LeitwegId = string.IsNullOrWhiteSpace(leitwegId) ? null : leitwegId.Trim();
        RechnungsEMail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    public static string ArtAnzeige(Organisationsart art) => art switch
    {
        Organisationsart.Behoerde => "Behörde",
        _ => art.ToString(),
    };
}

/// <summary>Zeitlich gültige Zugehörigkeit einer Person zu einer Organisation (z. B. Mitarbeitende der Stadtwerke).</summary>
public sealed class Organisationszugehoerigkeit : BitemporaleVersion<Organisationszugehoerigkeit>
{
    private Organisationszugehoerigkeit()
    {
        OrganisationName = string.Empty;
    }

    public Organisationszugehoerigkeit(Guid organisationId, string organisationName, string? funktion, DateOnly gueltigVon,
        DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        OrganisationId = organisationId;
        OrganisationName = organisationName;
        Funktion = string.IsNullOrWhiteSpace(funktion) ? null : funktion.Trim();
    }

    public Guid OrganisationId { get; private set; }

    /// <summary>Name zum Erfassungszeitpunkt (für die Historie).</summary>
    public string OrganisationName { get; private set; }

    public string? Funktion { get; private set; }

    internal void Anonymisieren() => Funktion = null;

    public override string Datenart => "Organisationen";

    public override string Feld => "Zugehörigkeit";

    public override string Gegenstand => OrganisationId.ToString("N");

    public override string Wert => IstEndemarke ? "(beendet)" : Funktion is null ? OrganisationName : $"{OrganisationName} ({Funktion})";

    protected internal override Organisationszugehoerigkeit KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(OrganisationId, OrganisationName, Funktion, gueltigVon, gueltigBis, erfassung);
}

public enum Bevollmaechtigtenart
{
    Person,
    Organisation,
}

/// <summary>
/// Buchungsvollmacht: Eine andere Person (z. B. Elternteil, Betreuerin) oder eine Organisation (z. B. Arbeitgeber,
/// Jobcenter) darf diese Person zu Kursen anmelden. Gilt im angegebenen Zeitraum; der Nachweis ist Pflicht.
/// </summary>
public sealed class Buchungsvollmacht : BitemporaleVersion<Buchungsvollmacht>
{
    private Buchungsvollmacht()
    {
        BevollmaechtigterName = string.Empty;
        Nachweis = string.Empty;
    }

    public Buchungsvollmacht(Guid vollmachtId, Bevollmaechtigtenart art, Guid bevollmaechtigterId, string bevollmaechtigterName,
        string nachweis, DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nachweis);
        VollmachtId = vollmachtId;
        Art = art;
        BevollmaechtigterId = bevollmaechtigterId;
        BevollmaechtigterName = bevollmaechtigterName;
        Nachweis = nachweis.Trim();
    }

    public Guid VollmachtId { get; private set; }

    public Bevollmaechtigtenart Art { get; private set; }

    public Guid BevollmaechtigterId { get; private set; }

    public string BevollmaechtigterName { get; private set; }

    /// <summary>z. B. "Sorgerecht, Geburtsurkunde" oder "Betreuerausweis AG Steinfurt".</summary>
    public string Nachweis { get; private set; }

    internal void Anonymisieren()
    {
        BevollmaechtigterName = "–";
        Nachweis = "–";
    }

    internal void BevollmaechtigtenUmbenennen(string name) => BevollmaechtigterName = name;

    public override string Datenart => "Vollmachten";

    public override string Feld => "Buchungsvollmacht";

    public override string Gegenstand => VollmachtId.ToString("N");

    public override string Wert => IstEndemarke
        ? "(widerrufen)"
        : $"{BevollmaechtigterName} ({(Art == Bevollmaechtigtenart.Person ? "Person" : "Organisation")}) · Nachweis: {Nachweis}";

    protected internal override Buchungsvollmacht KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(VollmachtId, Art, BevollmaechtigterId, BevollmaechtigterName, Nachweis, gueltigVon, gueltigBis, erfassung);
}

public enum Kontaktkanal
{
    EMail,
    Sms,
    Messenger,
    Telefon,
    Brief,
}

public enum Kontaktzweck
{
    /// <summary>Kursorganisation: Erinnerungen, Raumänderungen, Ausfälle.</summary>
    Kursorganisation,

    /// <summary>Werbung: Programmheft, Newsletter, Kursempfehlungen.</summary>
    Werbung,

    /// <summary>Fotos aus dem Kurs veröffentlichen (Website, Programmheft).</summary>
    Fotos,
}

/// <summary>Einwilligung je Kanal und Zweck (DSGVO), zeitlich versioniert: erteilt oder verweigert/widerrufen.</summary>
public sealed class Einwilligung : BitemporaleVersion<Einwilligung>
{
    private Einwilligung()
    {
    }

    public Einwilligung(Kontaktkanal kanal, Kontaktzweck zweck, bool erteilt, DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        Kanal = kanal;
        Zweck = zweck;
        Erteilt = erteilt;
    }

    public Kontaktkanal Kanal { get; private set; }

    public Kontaktzweck Zweck { get; private set; }

    public bool Erteilt { get; private set; }

    public static string KanalAnzeige(Kontaktkanal k) => k switch
    {
        Kontaktkanal.EMail => "E-Mail",
        Kontaktkanal.Sms => "SMS",
        _ => k.ToString(),
    };

    public static string ZweckAnzeige(Kontaktzweck z) => z switch
    {
        Kontaktzweck.Kursorganisation => "Kursorganisation",
        Kontaktzweck.Fotos => "Fotos veröffentlichen",
        _ => "Werbung",
    };

    public override string Datenart => "Einwilligungen";

    public override string Feld => $"{KanalAnzeige(Kanal)} · {ZweckAnzeige(Zweck)}";

    public override string Gegenstand => $"{Kanal}:{Zweck}";

    public override string Wert => IstEndemarke ? "(keine Angabe)" : Erteilt ? "erteilt" : "nicht erteilt";

    protected internal override Einwilligung KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Kanal, Zweck, Erteilt, gueltigVon, gueltigBis, erfassung);
}

/// <summary>Lebenszyklus eines Personendatensatzes (für Listen, Löschfristen und Dubletten).</summary>
public enum Personenstatus
{
    Aktiv,
    Ruhend,
    LoeschungVorgemerkt,
    Zusammengefuehrt,
}

public static class PersonenstatusAnzeige
{
    public static string Text(Personenstatus s) => s switch
    {
        Personenstatus.Aktiv => "aktiv",
        Personenstatus.Ruhend => "ruhend",
        Personenstatus.LoeschungVorgemerkt => "Löschung vorgemerkt",
        _ => "zusammengeführt",
    };
}
