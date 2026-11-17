using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Dokumentation;

/// <summary>
/// Anmerkung von Leitung oder Systemverwaltung zu einem Abschnitt der Spezifikation, Hilfe oder Doku (oder eine Antwort darauf).
/// Liegt in einer eigenen Datenbankdatei (kommentare.db), damit sie das Neuanlegen der Hauptdatenbank übersteht.
/// </summary>
/// <remarks>
/// Verfasser und Abschnittstitel werden bewusst als Text gespeichert: Die Benutzerkonten der Hauptdatenbank können neu
/// entstehen (neue Ids), und ein Abschnitt kann in einem späteren Schnappschuss umbenannt sein – der Kommentar soll dann
/// weiterhin zeigen, wer was zu welcher Überschrift geschrieben hat.
/// </remarks>
public sealed class Kommentar
{
    public const int TextMaxZeichen = 4000;

    private Kommentar()
    {
        Dokument = Abschnitt = AbschnittTitel = Text = VerfasstVonKennung = VerfasstVonName = string.Empty;
    }

    public Kommentar(string dokument, string abschnitt, string abschnittTitel, string text, string kennung, string name,
        string? rolle, DateTimeOffset am, Guid? antwortAuf = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dokument);
        ArgumentException.ThrowIfNullOrWhiteSpace(kennung);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Bitte einen Text eingeben.", nameof(text));
        }

        if (text.Trim().Length > TextMaxZeichen)
        {
            throw new ArgumentException($"Der Kommentar ist zu lang (höchstens {TextMaxZeichen} Zeichen).", nameof(text));
        }

        Id = Ids.Neu(am.ToUnixTimeMilliseconds());
        Dokument = dokument.Trim();
        Abschnitt = (abschnitt ?? string.Empty).Trim();
        AbschnittTitel = (abschnittTitel ?? string.Empty).Trim();
        Text = text.Trim().ReplaceLineEndings("\n");
        VerfasstVonKennung = kennung;
        VerfasstVonName = string.IsNullOrWhiteSpace(name) ? kennung : name;
        VerfasstVonRolle = rolle;
        Am = am;
        AntwortAuf = antwortAuf;
    }

    public Guid Id { get; private set; }

    /// <summary>Schlüssel des Dokuments, z. B. „spezifikation/anforderungen“ oder „hilfe“.</summary>
    public string Dokument { get; private set; }

    /// <summary>Anker der Überschrift (z. B. „bedienung“); leer = Dokument als Ganzes.</summary>
    public string Abschnitt { get; private set; }

    public string AbschnittTitel { get; private set; }

    public string Text { get; private set; }

    public string VerfasstVonKennung { get; private set; }

    public string VerfasstVonName { get; private set; }

    public string? VerfasstVonRolle { get; private set; }

    public DateTimeOffset Am { get; private set; }

    /// <summary>Antwort auf diesen Kommentar (nur eine Ebene: Antworten hängen immer am ersten Kommentar des Fadens).</summary>
    public Guid? AntwortAuf { get; private set; }

    /// <summary>Erledigt (eingearbeitet oder beantwortet); gilt für den ganzen Faden.</summary>
    public bool Erledigt { get; private set; }

    public string? ErledigtVonName { get; private set; }

    public DateTimeOffset? ErledigtAm { get; private set; }

    public void ErledigtSetzen(bool erledigt, string name, DateTimeOffset am)
    {
        if (AntwortAuf is not null)
        {
            throw new InvalidOperationException("Nur der erste Kommentar eines Fadens wird als erledigt markiert.");
        }

        Erledigt = erledigt;
        ErledigtVonName = erledigt ? name : null;
        ErledigtAm = erledigt ? am : null;
    }
}
