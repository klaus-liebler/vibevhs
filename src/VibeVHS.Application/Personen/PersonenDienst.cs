using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

public interface IPersonenSpeicher
{
    void Hinzufuegen(Person person);

    Task<Person?> LadenAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Person>> AlleAsync(CancellationToken abbruch = default);

    Task<int> AnzahlAsync(CancellationToken abbruch = default);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Anwendungsfälle rund um Personen. Jede Änderung wird protokolliert.</summary>
public sealed class PersonenDienst(IPersonenSpeicher speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    public async Task<Person> AnlegenAsync(string vorname, string nachname, DateOnly? geburtsdatum, string? eMail,
        string sprache, CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetLocalNow();
        var von = (await benutzer.ErmittelnAsync(abbruch)).Anzeige;
        var person = Person.Anlegen(vorname, nachname, DateOnly.FromDateTime(jetzt.Date), jetzt, geburtsdatum, eMail, sprache, von);
        speicher.Hinzufuegen(person);
        await protokoll.ProtokollierenAsync("Person.Angelegt", $"Person {vorname} {nachname} angelegt",
            nameof(Person), person.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return person;
    }

    public async Task<PersonName> NameAendernAbAsync(Guid personId, string vorname, string nachname, DateOnly ab, string grund,
        CancellationToken abbruch = default)
    {
        var person = await PersonLadenAsync(personId, abbruch);
        var jetzt = uhr.GetLocalNow();
        var alt = person.AktuellerName(jetzt).Anzeige;
        var neu = person.NameAendernAb(vorname, nachname, ab, grund, jetzt, (await benutzer.ErmittelnAsync(abbruch)).Anzeige);
        await protokoll.ProtokollierenAsync("Person.NameGeaendert",
            $"Name von {alt} ab {ab:dd.MM.yyyy} geändert in {neu.Anzeige}. Grund: {grund}", nameof(Person), personId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return neu;
    }

    public async Task<PersonName> NameKorrigierenAsync(Guid personId, string vorname, string nachname, DateOnly betroffenAm,
        string grund, CancellationToken abbruch = default)
    {
        var person = await PersonLadenAsync(personId, abbruch);
        var jetzt = uhr.GetLocalNow();
        var alt = person.NameAm(betroffenAm, jetzt)?.Anzeige ?? "(unbekannt)";
        var neu = person.NameKorrigieren(vorname, nachname, betroffenAm, grund, jetzt, (await benutzer.ErmittelnAsync(abbruch)).Anzeige);
        await protokoll.ProtokollierenAsync("Person.NameKorrigiert",
            $"Name {alt} korrigiert in {neu.Anzeige} (gilt rückwirkend ab {neu.GueltigVon:dd.MM.yyyy}). Grund: {grund}",
            nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return neu;
    }

    private async Task<Person> PersonLadenAsync(Guid id, CancellationToken abbruch)
        => await speicher.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
}
