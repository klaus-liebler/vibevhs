using System.Security.Cryptography;
using System.Text;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Shop;

public enum Warenkorbstatus
{
    /// <summary>Plätze sind reserviert, bis <see cref="Warenkorb.ReserviertBis"/> abläuft.</summary>
    Offen,
    Gebucht,
    Verfallen,
}

/// <summary>Ein Kurs im Warenkorb. <see cref="Warteliste"/>: kein freier Platz bei der Reservierung, Buchung auf die Warteliste.</summary>
public sealed record Warenkorbposition(Guid KursId, string Kursnummer, bool Warteliste);

/// <summary>
/// Warenkorb des Webshops für genau eine teilnehmende Person (für weitere Personen ein weiterer Warenkorb).
/// Solange er offen ist, hält er seine Plätze für <see cref="ReserviertBis"/>; danach verfällt er (Job).
/// Gespeichert wird nur Kurs und Zeit – Personendaten kommen erst mit der Buchung.
/// </summary>
public sealed class Warenkorb
{
    private Warenkorb()
    {
    }

    public Warenkorb(DateTimeOffset jetzt, TimeSpan reservierung, Guid? kontoId = null)
    {
        Id = Ids.Neu();
        ErstelltAm = jetzt;
        ReserviertBis = jetzt + reservierung;
        KontoId = kontoId;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }

    public DateTimeOffset ReserviertBis { get; private set; }

    public Warenkorbstatus Status { get; private set; }

    public Guid? KontoId { get; private set; }

    public List<Warenkorbposition> Positionen { get; private set; } = [];

    public DateTimeOffset? AbgeschlossenAm { get; private set; }

    /// <summary>Hält der Warenkorb zum Zeitpunkt noch Plätze?</summary>
    public bool Haelt(DateTimeOffset jetzt) => Status == Warenkorbstatus.Offen && ReserviertBis > jetzt;

    /// <summary>Ersetzt die Positionen; jede Änderung verlängert die Reservierung (gleitend, ab jetzt).</summary>
    public void PositionenSetzen(IEnumerable<Warenkorbposition> positionen, DateTimeOffset jetzt, TimeSpan reservierung)
    {
        if (!Haelt(jetzt))
        {
            throw new InvalidOperationException(Status == Warenkorbstatus.Gebucht
                ? "Dieser Warenkorb ist bereits gebucht."
                : "Die Reservierung ist abgelaufen; bitte einen neuen Warenkorb anlegen.");
        }

        var liste = positionen.ToList();
        if (liste.Select(p => p.KursId).Distinct().Count() != liste.Count)
        {
            throw new InvalidOperationException("Jeder Kurs kann je Warenkorb nur einmal gebucht werden (ein Warenkorb gilt für eine Person).");
        }

        Positionen = liste;
        ReserviertBis = jetzt + reservierung;
    }

    public void AlsGebuchtMarkieren(DateTimeOffset jetzt)
    {
        Status = Warenkorbstatus.Gebucht;
        AbgeschlossenAm = jetzt;
    }

    /// <summary>Gibt die Plätze frei (Ablauf). Gebuchte Warenkörbe bleiben unverändert.</summary>
    public bool Verfallen(DateTimeOffset jetzt)
    {
        if (Status != Warenkorbstatus.Offen || ReserviertBis > jetzt)
        {
            return false;
        }

        Status = Warenkorbstatus.Verfallen;
        AbgeschlossenAm = jetzt;
        return true;
    }
}

/// <summary>Nachweis einer Einwilligung bei der Buchung (Art, Textversion, Zeitpunkt).</summary>
public sealed record Einwilligungsnachweis(string Art, string Version, bool Erteilt, DateTimeOffset Zeitpunkt);

/// <summary>Ergebnis einer Position der Buchung.</summary>
public sealed record Buchungsposition(string Kursnummer, Guid? BelegungId, string Status, string? Hinweis = null);

/// <summary>
/// Abgeschlossene Webshop-Buchung. Der Idempotenzschlüssel ist eindeutig: dieselbe Anfrage liefert dasselbe Ergebnis,
/// eine abweichende Anfrage mit demselben Schlüssel wird abgelehnt.
/// </summary>
public sealed class Shopbuchung
{
    private Shopbuchung()
    {
        Idempotenzschluessel = string.Empty;
        AnfrageHash = string.Empty;
        Zahlart = string.Empty;
    }

    public Shopbuchung(string idempotenzschluessel, string anfrageHash, Guid warenkorbId, Guid personId, Guid? kontoId, string zahlart, Guid? mandatId,
        IEnumerable<Einwilligungsnachweis> einwilligungen, IEnumerable<Buchungsposition> positionen, DateTimeOffset zeitpunkt)
    {
        Id = Ids.Neu();
        Idempotenzschluessel = idempotenzschluessel;
        AnfrageHash = anfrageHash;
        WarenkorbId = warenkorbId;
        PersonId = personId;
        KontoId = kontoId;
        Zahlart = zahlart;
        MandatId = mandatId;
        Einwilligungen = einwilligungen.ToList();
        Positionen = positionen.ToList();
        Zeitpunkt = zeitpunkt;
    }

    public Guid Id { get; private set; }

    public string Idempotenzschluessel { get; private set; }

    /// <summary>SHA-256 des Anfrageinhalts (ohne Zeitstempel), zur Erkennung abweichender Wiederholungen.</summary>
    public string AnfrageHash { get; private set; }

    public Guid WarenkorbId { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid? KontoId { get; private set; }

    public string Zahlart { get; private set; }

    public Guid? MandatId { get; private set; }

    public List<Einwilligungsnachweis> Einwilligungen { get; private set; } = [];

    public List<Buchungsposition> Positionen { get; private set; } = [];

    public DateTimeOffset Zeitpunkt { get; private set; }
}

public enum Kontostatus
{
    /// <summary>Registriert, Bestätigungslink (Double-Opt-in) noch nicht geöffnet.</summary>
    Unbestaetigt,
    Aktiv,
}

/// <summary>
/// Kundenkonto des Webshops. Angemeldet wird über den Identitätsdienst (OIDC; vorerst Mock), das Konto verknüpft
/// die Anmeldung mit genau einer Person. Unbestätigte Konten werden nach Ablauf des Links gelöscht.
/// </summary>
public sealed class Shopkonto
{
    private Shopkonto()
    {
        EMail = string.Empty;
        Vorname = string.Empty;
        Nachname = string.Empty;
        TokenHash = string.Empty;
    }

    public Shopkonto(string eMail, string vorname, string nachname, DateOnly? geburtsdatum, string sprache, string token, DateTimeOffset jetzt, TimeSpan gueltigkeit)
    {
        Id = Ids.Neu();
        EMail = Normalisieren(eMail);
        Vorname = vorname.Trim();
        Nachname = nachname.Trim();
        Geburtsdatum = geburtsdatum;
        Sprache = sprache;
        ErstelltAm = jetzt;
        NeuerToken(token, jetzt, gueltigkeit);
    }

    public Guid Id { get; private set; }

    public string EMail { get; private set; }

    public string Vorname { get; private set; }

    public string Nachname { get; private set; }

    public DateOnly? Geburtsdatum { get; private set; }

    public string Sprache { get; private set; } = "de";

    public Kontostatus Status { get; private set; }

    public Guid? PersonId { get; private set; }

    /// <summary>Nur der SHA-256 des Bestätigungstokens wird gespeichert.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset TokenGueltigBis { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }

    public DateTimeOffset? BestaetigtAm { get; private set; }

    public static string Normalisieren(string eMail) => eMail.Trim().ToLowerInvariant();

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public void NeuerToken(string token, DateTimeOffset jetzt, TimeSpan gueltigkeit)
    {
        TokenHash = Hash(token);
        TokenGueltigBis = jetzt + gueltigkeit;
    }

    public bool TokenPasst(string token, DateTimeOffset jetzt)
        => Status == Kontostatus.Unbestaetigt && TokenGueltigBis > jetzt
           && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(TokenHash), Encoding.ASCII.GetBytes(Hash(token)));

    public void Bestaetigen(Guid personId, DateTimeOffset jetzt)
    {
        Status = Kontostatus.Aktiv;
        PersonId = personId;
        BestaetigtAm = jetzt;
        TokenHash = string.Empty;
    }
}

/// <summary>
/// Veröffentlichungsstand eines Kurses für den Delta-Abgleich (TYPO3, Portale): Hash der öffentlichen Darstellung
/// und Zeitpunkt der letzten Änderung. Nicht mehr öffentliche Kurse bleiben als „entfernt“ stehen.
/// </summary>
public sealed class Kursstand
{
    private Kursstand()
    {
        Kursnummer = string.Empty;
        Hash = string.Empty;
    }

    public Kursstand(Guid kursId, string kursnummer, string hash, DateTimeOffset jetzt)
    {
        KursId = kursId;
        Kursnummer = kursnummer;
        Hash = hash;
        GeaendertAm = jetzt;
    }

    public Guid KursId { get; private set; }

    public string Kursnummer { get; private set; }

    public string Hash { get; private set; }

    public DateTimeOffset GeaendertAm { get; private set; }

    public bool Entfernt { get; private set; }

    /// <summary>Übernimmt einen neuen Stand; true, wenn sich etwas geändert hat.</summary>
    public bool Aktualisieren(string hash, DateTimeOffset jetzt)
    {
        if (!Entfernt && Hash == hash)
        {
            return false;
        }

        Hash = hash;
        Entfernt = false;
        GeaendertAm = jetzt;
        return true;
    }

    public bool Entfernen(DateTimeOffset jetzt)
    {
        if (Entfernt)
        {
            return false;
        }

        Entfernt = true;
        Hash = string.Empty;
        GeaendertAm = jetzt;
        return true;
    }
}

/// <summary>
/// Schnellanmeldung über den persönlichen Link bzw. ein gemerktes Gerät und den Kurs-QR. Der Storno-Link aus der Bestätigungsmail
/// (nur als Hash gespeichert) erlaubt die Abmeldung – innerhalb von <see cref="KostenfreiBis"/> kostenlos, danach nach den AGB –,
/// damit ein weitergegebener persönlicher Link keinen Schaden anrichtet.
/// </summary>
public sealed class Schnellanmeldung
{
    private Schnellanmeldung()
    {
        Kursnummer = string.Empty;
        StornoHash = string.Empty;
        Weg = string.Empty;
    }

    public Schnellanmeldung(Guid personId, Guid belegungId, string kursnummer, string weg, string stornoToken, IEnumerable<Einwilligungsnachweis> einwilligungen,
        DateTimeOffset jetzt, TimeSpan kostenfrei)
    {
        Id = Ids.Neu();
        PersonId = personId;
        BelegungId = belegungId;
        Kursnummer = kursnummer;
        Weg = weg;
        StornoHash = Shopkonto.Hash(stornoToken);
        Einwilligungen = einwilligungen.ToList();
        Zeitpunkt = jetzt;
        KostenfreiBis = jetzt + kostenfrei;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid BelegungId { get; private set; }

    public string Kursnummer { get; private set; }

    /// <summary>„Link“ (persönlicher Link geöffnet) oder „Gerät“ (gemerktes Gerät).</summary>
    public string Weg { get; private set; }

    public string StornoHash { get; private set; }

    public List<Einwilligungsnachweis> Einwilligungen { get; private set; } = [];

    public DateTimeOffset Zeitpunkt { get; private set; }

    public DateTimeOffset KostenfreiBis { get; private set; }

    public DateTimeOffset? StorniertAm { get; private set; }

    public bool Kostenfrei(DateTimeOffset jetzt) => jetzt <= KostenfreiBis;

    public void Storniert(DateTimeOffset jetzt) => StorniertAm ??= jetzt;
}
