using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Postausgang;

public enum Kanal
{
    EMail,
    Schnittstelle,
}

public enum NachrichtenStatus
{
    Wartend,
    Zugestellt,
    Fehlgeschlagen,
}

/// <summary>
/// Outbox-Eintrag: Jede ausgehende Mail und jeder Schnittstellenaufruf wird zuerst hier abgelegt,
/// gemeinsam mit der fachlichen Änderung gespeichert und erst danach von einem Hintergrundjob zugestellt.
/// So geht nichts verloren und alles ist im Postausgang sichtbar.
/// </summary>
public sealed class AusgehendeNachricht
{
    public const int MaxVersuche = 5;

    private AusgehendeNachricht()
    {
        Empfaenger = string.Empty;
        Betreff = string.Empty;
        Inhalt = string.Empty;
        AusgeloestVon = string.Empty;
        Sprache = "de";
    }

    public AusgehendeNachricht(Kanal kanal, string empfaenger, string? empfaengerAnzeige, string betreff, string inhalt,
        string sprache, DateTimeOffset erstelltAm, string ausgeloestVon, Guid? personId = null, Uri? uebersetzungslink = null,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(empfaenger);
        ArgumentException.ThrowIfNullOrWhiteSpace(betreff);
        Id = id ?? Ids.Neu();
        Kanal = kanal;
        Empfaenger = empfaenger;
        EmpfaengerAnzeige = empfaengerAnzeige;
        Betreff = betreff;
        Inhalt = inhalt;
        Sprache = sprache;
        ErstelltAm = erstelltAm;
        AusgeloestVon = ausgeloestVon;
        PersonId = personId;
        Uebersetzungslink = uebersetzungslink?.ToString();
        Status = NachrichtenStatus.Wartend;
    }

    public Guid Id { get; private set; }

    public Kanal Kanal { get; private set; }

    /// <summary>E-Mail-Adresse oder Name der Schnittstelle (z. B. "BAMF", "Finanzsoftware").</summary>
    public string Empfaenger { get; private set; }

    public string? EmpfaengerAnzeige { get; private set; }

    public string Betreff { get; private set; }

    public string Inhalt { get; private set; }

    public string Sprache { get; private set; }

    public string? Uebersetzungslink { get; private set; }

    public Guid? PersonId { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }

    public string AusgeloestVon { get; private set; }

    public NachrichtenStatus Status { get; private set; }

    public int Versuche { get; private set; }

    public DateTimeOffset? ZugestelltAm { get; private set; }

    /// <summary>Rückmeldung des Kanals, z. B. Vorgangsnummer oder Hinweis "simuliert".</summary>
    public string? Rueckmeldung { get; private set; }

    public string? Fehler { get; private set; }

    public void AlsZugestelltMarkieren(DateTimeOffset zeitpunkt, string? rueckmeldung)
    {
        Versuche++;
        Status = NachrichtenStatus.Zugestellt;
        ZugestelltAm = zeitpunkt;
        Rueckmeldung = rueckmeldung;
        Fehler = null;
    }

    public void FehlversuchVermerken(string fehler)
    {
        Versuche++;
        Fehler = fehler;
        if (Versuche >= MaxVersuche)
        {
            Status = NachrichtenStatus.Fehlgeschlagen;
        }
    }

    /// <summary>Anonymisierung (Löschung der Person): Inhalt, Betreff und Adresse werden überschrieben, die Zeile bleibt.</summary>
    public void Anonymisieren(string platzhalter)
    {
        Empfaenger = "anonymisiert";
        EmpfaengerAnzeige = platzhalter;
        Betreff = "[anonymisiert]";
        Inhalt = "[Inhalt anonymisiert]";
        Uebersetzungslink = null;
        if (Status == NachrichtenStatus.Wartend)
        {
            Status = NachrichtenStatus.Fehlgeschlagen;
            Fehler = "Empfänger wurde anonymisiert; Nachricht wird nicht mehr zugestellt.";
        }
    }

    /// <summary>Fehlgeschlagene Nachricht nach Prüfung erneut in die Warteschlange stellen.</summary>
    public void ErneutVersuchen()
    {
        if (Status != NachrichtenStatus.Fehlgeschlagen)
        {
            throw new InvalidOperationException("Nur fehlgeschlagene Nachrichten können erneut versendet werden.");
        }

        Status = NachrichtenStatus.Wartend;
        Versuche = 0;
    }
}
