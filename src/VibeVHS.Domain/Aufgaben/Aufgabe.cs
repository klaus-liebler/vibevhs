using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Aufgaben;

/// <summary>
/// Einfache Aufgabe aus einer automatischen Regel (z. B. Zulassung eines Kursorts läuft ab).
/// Das vollständige Aufgabenmodul mit Gruppenkörben und Eskalation folgt in Iteration I5.
/// </summary>
public sealed class Aufgabe
{
    private Aufgabe()
    {
        Titel = string.Empty;
        Schluessel = string.Empty;
        Gruppe = string.Empty;
    }

    public Aufgabe(string schluessel, string titel, string? beschreibung, DateOnly faellig, string gruppe, string? ziel, DateTimeOffset angelegtAm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schluessel);
        ArgumentException.ThrowIfNullOrWhiteSpace(titel);
        Id = Ids.Neu();
        Schluessel = schluessel;
        Titel = titel;
        Beschreibung = beschreibung;
        Faellig = faellig;
        Gruppe = gruppe;
        Ziel = ziel;
        AngelegtAm = angelegtAm;
    }

    public Guid Id { get; private set; }

    /// <summary>Fachlicher Schlüssel, damit eine Regel dieselbe Aufgabe nicht doppelt anlegt.</summary>
    public string Schluessel { get; private set; }

    public string Titel { get; private set; }

    public string? Beschreibung { get; private set; }

    public DateOnly Faellig { get; private set; }

    /// <summary>Zuständige Rolle (Gruppenkorb), z. B. "IntegrationskursTeam".</summary>
    public string Gruppe { get; private set; }

    /// <summary>Relative Adresse in der Anwendung, z. B. "orte?id=…".</summary>
    public string? Ziel { get; private set; }

    public DateTimeOffset AngelegtAm { get; private set; }

    public DateTimeOffset? ErledigtAm { get; private set; }

    public bool Offen => ErledigtAm is null;

    public void Erledigen(DateTimeOffset am) => ErledigtAm ??= am;
}
