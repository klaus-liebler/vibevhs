using System.Security.Cryptography;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Dokumente;

public enum Dokumentart
{
    Teilnahmebescheinigung,
    Bildungsurlaub,
    Teilnehmerausweis,
    Auskunft,

    /// <summary>Druckstück eines Finanzbelegs (Rechnung, Mahnung, Honorarabrechnung, Gutschrift, Storno).</summary>
    Finanzbeleg,

    /// <summary>Antwortbrief zum Löschverlangen (Art. 17 DSGVO): im Archiv nur der Text ohne Anschrift und ohne Personenbezug.</summary>
    Datenschutzantwort,
}

public static class DokumentAnzeige
{
    public static string Art(Dokumentart a) => a switch
    {
        Dokumentart.Teilnahmebescheinigung => "Teilnahmebescheinigung",
        Dokumentart.Bildungsurlaub => "Bescheinigung Bildungsurlaub (AWbG)",
        Dokumentart.Teilnehmerausweis => "Teilnehmerausweis",
        Dokumentart.Finanzbeleg => "Finanzbeleg",
        Dokumentart.Datenschutzantwort => "Antwort zum Löschverlangen (Art. 17 DSGVO)",
        _ => "Auskunft nach Art. 15 DSGVO",
    };
}

/// <summary>
/// Archiviertes Dokument: das erzeugte PDF, der eingefrorene Datenstand (JSON) und ein SHA-256-Hash über das PDF.
/// Ein Nachdruck liefert immer dieses Original; eine Neuausstellung ist ein neues Dokument mit Verweis.
/// </summary>
public sealed class Dokument
{
    private Dokument()
    {
        Titel = string.Empty;
        ErstelltVon = string.Empty;
        Pdf = [];
        DatenJson = "{}";
        Sha256 = string.Empty;
        Klartext = string.Empty;
        UebersetzungsToken = string.Empty;
    }

    public Dokument(Guid id, Dokumentart art, Guid personId, Guid? kursId, Guid? belegungId, string titel, byte[] pdf, string datenJson, string klartext,
        string uebersetzungsToken, DateTimeOffset erstelltAm, string erstelltVon, TimeSpan uebersetzbarFuer, Guid? neuausstellungVon = null, string? neuausstellungGrund = null)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentException.ThrowIfNullOrWhiteSpace(titel);
        Id = id;
        Art = art;
        PersonId = personId;
        KursId = kursId;
        BelegungId = belegungId;
        Titel = titel;
        Pdf = pdf;
        DatenJson = datenJson;
        Klartext = klartext;
        Sha256 = Hash(pdf);
        UebersetzungsToken = uebersetzungsToken;
        UebersetzbarBis = erstelltAm + uebersetzbarFuer;
        ErstelltAm = erstelltAm;
        ErstelltVon = erstelltVon;
        NeuausstellungVon = neuausstellungVon;
        NeuausstellungGrund = neuausstellungGrund;
    }

    public Guid Id { get; private set; }

    public Dokumentart Art { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid? KursId { get; private set; }

    public Guid? BelegungId { get; private set; }

    public string Titel { get; private set; }

    public byte[] Pdf { get; private set; }

    /// <summary>Datenstand, aus dem das Dokument erzeugt wurde (eingefroren).</summary>
    public string DatenJson { get; private set; }

    /// <summary>Text des Briefs für die Übersetzungsseite.</summary>
    public string Klartext { get; private set; }

    /// <summary>SHA-256 über das PDF (hexadezimal, klein).</summary>
    public string Sha256 { get; private set; }

    /// <summary>Nicht erratbares Token für die Übersetzungsseite (QR-Code „Translate this letter“).</summary>
    public string UebersetzungsToken { get; private set; }

    public DateTimeOffset UebersetzbarBis { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }

    public string ErstelltVon { get; private set; }

    /// <summary>Bei einer bewussten Neuausstellung: das ersetzte Dokument.</summary>
    public Guid? NeuausstellungVon { get; private set; }

    public string? NeuausstellungGrund { get; private set; }

    /// <summary>Durch eine Neuausstellung ersetzt.</summary>
    public Guid? ErsetztDurch { get; private set; }

    public DateTimeOffset? AnonymisiertAm { get; private set; }

    public bool IstAnonymisiert => AnonymisiertAm is not null;

    /// <summary>Stimmt das gespeicherte PDF noch mit dem Hash bei der Erzeugung überein?</summary>
    public bool Unveraendert => !IstAnonymisiert && Hash(Pdf) == Sha256;

    public void ErsetzenDurch(Guid neu) => ErsetztDurch = neu;

    /// <summary>Anonymisierung: Inhalt entfernt, Zeile, Art, Datum und Hash bleiben als Nachweis, dass es das Dokument gab.</summary>
    public void Anonymisieren(DateTimeOffset jetzt)
    {
        Pdf = [];
        DatenJson = "{}";
        Klartext = string.Empty;
        UebersetzungsToken = Ids.Neu().ToString("N");
        UebersetzbarBis = jetzt;
        AnonymisiertAm = jetzt;
    }

    public static string Hash(byte[] daten) => Convert.ToHexStringLower(SHA256.HashData(daten));
}
