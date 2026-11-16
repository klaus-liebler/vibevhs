namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Ein Änderungsvorgang (Tabelle Aenderungsvorgaenge): wann, von wem und warum etwas erfasst wurde. Alle Versionszeilen, die eine Aktion anlegt
/// (z. B. ein Umzug: Abgrenzung der alten Adresse und die neue Adresse), verweisen auf denselben Aenderungsvorgang (<c>ErfasstDurchVorgangId</c>);
/// wird eine Zeile durch neueres Wissen ersetzt, verweist sie zusätzlich auf den ersetzenden Aenderungsvorgang (<c>ErsetztDurchVorgangId</c>).
/// So stehen Zeitpunkt, Person und Grund genau einmal in der Datenbank. Die Rolle der Zeile im Aenderungsvorgang (Ersterfassung, Änderung ab
/// Datum, Korrektur, Abgrenzung, Beendet) steht weiter an der Zeile (<see cref="Erfassungsart"/>).
/// </summary>
public sealed class Aenderungsvorgang
{
    private Aenderungsvorgang()
    {
        Von = string.Empty;
        Grund = string.Empty;
    }

    public Aenderungsvorgang(DateTimeOffset am, string? von, string? grund)
    {
        Id = Ids.Neu(am.ToUnixTimeMilliseconds());
        Am = am;
        Von = string.IsNullOrWhiteSpace(von) ? Erfassung.System : von.Trim();
        Grund = grund?.Trim() ?? string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Zeitpunkt der Erfassung (Wissenszeit).</summary>
    public DateTimeOffset Am { get; private set; }

    /// <summary>Benutzer bzw. Weg (Startdaten, Import, Automatik …).</summary>
    public string Von { get; private set; }

    public string Grund { get; private set; }
}
