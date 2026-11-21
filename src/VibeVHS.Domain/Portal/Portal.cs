using System.Security.Cryptography;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Portal;

/// <summary>Hochgeladene Datei (Kursunterlage, Qualifikationsnachweis). Inhalt in der Datenbank, mit SHA-256.</summary>
public sealed class Datei
{
    private Datei()
    {
        Name = string.Empty;
        Typ = string.Empty;
        Sha256 = string.Empty;
        Inhalt = [];
    }

    public Datei(string name, string typ, byte[] inhalt, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        Id = Ids.Neu();
        Name = Path.GetFileName(name).Trim();
        Typ = typ;
        Inhalt = inhalt;
        Groesse = inhalt.LongLength;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(inhalt));
        ErstelltAm = jetzt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Typ { get; private set; }

    public long Groesse { get; private set; }

    public string Sha256 { get; private set; }

    public byte[] Inhalt { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }
}

/// <summary>
/// Unterlage einer Kursleitung für einen Kurs. Teilnehmende erhalten einen Link (nicht erratbares Token, nur als Hash gespeichert),
/// gültig bis einige Wochen nach Kursende.
/// </summary>
public sealed class Kursunterlage
{
    private Kursunterlage()
    {
        Titel = string.Empty;
        TokenHash = string.Empty;
    }

    public Kursunterlage(Guid kursId, Guid dateiId, string titel, Guid hochgeladenVon, string token, DateTimeOffset jetzt, DateTimeOffset gueltigBis)
    {
        Id = Ids.Neu();
        KursId = kursId;
        DateiId = dateiId;
        Titel = titel.Trim();
        HochgeladenVon = hochgeladenVon;
        TokenHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        HochgeladenAm = jetzt;
        GueltigBis = gueltigBis;
    }

    public Guid Id { get; private set; }

    public Guid KursId { get; private set; }

    public Guid DateiId { get; private set; }

    public string Titel { get; private set; }

    public Guid HochgeladenVon { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset HochgeladenAm { get; private set; }

    public DateTimeOffset GueltigBis { get; private set; }

    public DateTimeOffset? ZurueckgezogenAm { get; private set; }

    public int Abrufe { get; private set; }

    public bool Abrufbar(DateTimeOffset jetzt) => ZurueckgezogenAm is null && jetzt <= GueltigBis;

    public void Abgerufen() => Abrufe++;

    public void Zurueckziehen(DateTimeOffset jetzt) => ZurueckgezogenAm ??= jetzt;
}

public enum Antragsart
{
    /// <summary>Ausfall eines Termins (Krankheit): Fachbereichsleitung entscheidet über Absage/Vertretung.</summary>
    Ausfall,

    /// <summary>Einwand gegen eine Honorarabrechnung.</summary>
    Honorareinwand,

    /// <summary>Neue Bankverbindung: wirksam erst nach Freigabe durch die Buchhaltung (Vier-Augen).</summary>
    Bankverbindung,

    /// <summary>Neuer Qualifikationsnachweis zur Prüfung.</summary>
    Qualifikationsnachweis,

    /// <summary>Verfügbarkeit und Wunschzeiten fürs nächste Semester.</summary>
    Verfuegbarkeit,

    /// <summary>Kursvorschlag fürs nächste Semester.</summary>
    Kursvorschlag,

    /// <summary>Teilnehmende: Auskunft nach Art. 15 DSGVO anfordern.</summary>
    Auskunft,

    /// <summary>Teilnehmende: Löschung nach Art. 17 DSGVO verlangen (Prüfung durch die Verwaltung, kein Sofortlöschen).</summary>
    Loeschung,

    /// <summary>Teilnehmende: neue E-Mail-Adresse, wirksam nach Bestätigung über einen Link an die neue Adresse (keine Aufgabe).</summary>
    EMailAenderung,
}

public enum Antragsstatus
{
    Offen,
    Angenommen,
    Abgelehnt,
}

/// <summary>
/// Anliegen einer Kursleitung aus dem Portal an die Verwaltung. Jedes offene Anliegen erzeugt eine Aufgabe im passenden Korb;
/// mit der Entscheidung schließt sie sich. Änderungen an Stammdaten (Bankverbindung) werden erst mit der Annahme wirksam.
/// </summary>
public sealed class Portalantrag
{
    private Portalantrag()
    {
        Titel = string.Empty;
        Text = string.Empty;
    }

    public Portalantrag(Guid personId, Antragsart art, string titel, string text, IReadOnlyDictionary<string, string> daten, Guid? bezugId, int? fachbereich,
        Guid? dateiId, DateTimeOffset jetzt)
    {
        Id = Ids.Neu();
        PersonId = personId;
        Art = art;
        Titel = titel.Trim();
        Text = text.Trim();
        Daten = new Dictionary<string, string>(daten);
        BezugId = bezugId;
        Fachbereich = fachbereich;
        DateiId = dateiId;
        EingereichtAm = jetzt;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public Antragsart Art { get; private set; }

    public string Titel { get; private set; }

    public string Text { get; private set; }

    /// <summary>Strukturierte Angaben (z. B. IBAN, Kontoinhaber; Wochentage; Merkmal).</summary>
    /// <remarks>Gespeichert als eigene Tabelle (je Eintrag eine Zeile).</remarks>
    public IReadOnlyDictionary<string, string> Daten
    {
        get => Gemeinsam.Listenwerte.Woerterbuch(_daten);
        private set => _daten = Gemeinsam.Listenwerte.Von(value);
    }

    private List<Gemeinsam.Eintrag> _daten = [];

    /// <summary>Veranstaltung (Ausfall) oder Beleg (Honorareinwand).</summary>
    public Guid? BezugId { get; private set; }

    public int? Fachbereich { get; private set; }

    public Guid? DateiId { get; private set; }

    public Antragsstatus Status { get; private set; }

    public DateTimeOffset EingereichtAm { get; private set; }

    public DateTimeOffset? EntschiedenAm { get; private set; }

    public string? EntschiedenVon { get; private set; }

    public string? Vermerk { get; private set; }

    public void Entscheiden(bool angenommen, string von, string? vermerk, DateTimeOffset jetzt)
    {
        if (Status != Antragsstatus.Offen)
        {
            throw new InvalidOperationException("Dieses Anliegen ist bereits entschieden.");
        }

        Status = angenommen ? Antragsstatus.Angenommen : Antragsstatus.Abgelehnt;
        EntschiedenVon = von;
        Vermerk = string.IsNullOrWhiteSpace(vermerk) ? null : vermerk.Trim();
        EntschiedenAm = jetzt;
    }
}

public static class AntragAnzeige
{
    public static string Art(Antragsart a) => a switch
    {
        Antragsart.Ausfall => "Ausfall gemeldet",
        Antragsart.Honorareinwand => "Einwand zur Honorarabrechnung",
        Antragsart.Bankverbindung => "Neue Bankverbindung",
        Antragsart.Qualifikationsnachweis => "Qualifikationsnachweis",
        Antragsart.Verfuegbarkeit => "Verfügbarkeit nächstes Semester",
        Antragsart.Kursvorschlag => "Kursvorschlag",
        Antragsart.Auskunft => "Auskunft über meine Daten (Art. 15 DSGVO)",
        Antragsart.Loeschung => "Löschung meiner Daten (Art. 17 DSGVO)",
        Antragsart.EMailAenderung => "Neue E-Mail-Adresse",
        _ => a.ToString(),
    };

    public static string Status(Antragsstatus s) => s switch
    {
        Antragsstatus.Offen => "offen",
        Antragsstatus.Angenommen => "angenommen",
        Antragsstatus.Abgelehnt => "abgelehnt",
        _ => s.ToString(),
    };
}
