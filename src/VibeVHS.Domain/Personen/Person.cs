using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

/// <summary>
/// Eine natürliche Person (Teilnehmende, Dozierende, Ansprechpartner). Jede Person gibt es nur einmal;
/// Rollen kommen in späteren Iterationen als eigene Zuordnungen hinzu.
/// </summary>
public sealed class Person
{
    private readonly List<PersonName> _namen = [];

    private Person()
    {
    }

    public Guid Id { get; private set; }

    public DateOnly? Geburtsdatum { get; private set; }

    public string? EMail { get; private set; }

    /// <summary>Bevorzugte Sprache für Korrespondenz (ISO 639-1, z. B. "de", "tr", "uk").</summary>
    public string Sprache { get; private set; } = "de";

    public DateTimeOffset AngelegtAm { get; private set; }

    public IReadOnlyCollection<PersonName> Namen => _namen;

    public static Person Anlegen(string vorname, string nachname, DateOnly nameGueltigAb, DateTimeOffset jetzt,
        DateOnly? geburtsdatum = null, string? eMail = null, string sprache = "de")
    {
        var person = new Person
        {
            Id = Ids.Neu(),
            Geburtsdatum = geburtsdatum,
            EMail = string.IsNullOrWhiteSpace(eMail) ? null : eMail.Trim(),
            Sprache = string.IsNullOrWhiteSpace(sprache) ? "de" : sprache.Trim().ToLowerInvariant(),
            AngelegtAm = jetzt,
        };
        person._namen.Add(new PersonName(vorname, nachname, nameGueltigAb, null, jetzt, "Ersterfassung"));
        return person;
    }

    /// <summary>Name, der am <paramref name="gueltigAm"/> galt, nach dem Wissensstand vom <paramref name="bekanntAm"/>.</summary>
    public PersonName? NameAm(DateOnly gueltigAm, DateTimeOffset bekanntAm) => _namen.AlsStichtag(gueltigAm, bekanntAm);

    /// <summary>Heute gültiger Name nach aktuellem Wissensstand (falls heute keiner gilt: der zuletzt gültige).</summary>
    public PersonName AktuellerName(DateTimeOffset jetzt)
        => NameAm(DateOnly.FromDateTime(jetzt.Date), jetzt)
           ?? _namen.AktuellerVerlauf().LastOrDefault()
           ?? throw new InvalidOperationException("Person ohne Namen.");

    /// <summary>Namensänderung ab einem Datum (z. B. Heirat). Ältere Dokumente behalten den alten Namen.</summary>
    public PersonName NameAendernAb(string vorname, string nachname, DateOnly ab, string grund, DateTimeOffset jetzt)
    {
        var neu = new PersonName(vorname, nachname, ab, null, jetzt, grund);
        ZeitlicheHistorie.AendernAb(_namen, neu, jetzt);
        return neu;
    }

    /// <summary>Korrektur eines falsch erfassten Namens für dessen gesamten Gültigkeitszeitraum.</summary>
    public PersonName NameKorrigieren(string vorname, string nachname, DateOnly betroffenAm, string grund, DateTimeOffset jetzt)
        => ZeitlicheHistorie.Korrigieren(_namen, betroffenAm, (von, bis) => new PersonName(vorname, nachname, von, bis, jetzt, grund), jetzt);
}
