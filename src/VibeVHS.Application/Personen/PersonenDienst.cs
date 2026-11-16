using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

public interface IPersonenSpeicher
{
    void Hinzufuegen(Person person);

    Task<Person?> LadenAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Person>> LadenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default);

    void AusschlussHinzufuegen(DublettenAusschluss ausschluss);

    Task<IReadOnlyList<Person>> AlleAsync(CancellationToken abbruch = default);

    Task<int> AnzahlAsync(CancellationToken abbruch = default);

    /// <summary>Aktuelle Anzeigenamen (für die Anzeige des Protokolls, das nur Pseudonyme speichert).</summary>
    Task<IReadOnlyDictionary<Guid, string>> AnzeigenamenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Anwendungsfälle rund um Personen. Jede Änderung wird protokolliert.</summary>
public sealed class PersonenDienst(IPersonenSpeicher speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    public async Task<Person> AnlegenAsync(string vorname, string nachname, DateOnly? geburtsdatum, string? eMail,
        string sprache, CancellationToken abbruch = default, string? titel = null, string? vorsatz = null, string? zusatz = null)
    {
        var jetzt = uhr.GetLocalNow();
        var von = (await benutzer.ErmittelnAsync(abbruch)).Anzeige;
        var person = Person.Anlegen(vorname, nachname, DateOnly.FromDateTime(jetzt.Date), jetzt, geburtsdatum, eMail, sprache, von, vorsatz, zusatz);
        person.TitelSetzen(titel);
        speicher.Hinzufuegen(person);
        await protokoll.ProtokollierenAsync("Person.Angelegt", $"{Pseudonym.Person(person.Id)} angelegt",
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
            $"Name von {Pseudonym.Person(personId)} ab {ab:dd.MM.yyyy} geändert (Änderung ab Datum, Details in der Datenhistorie)", nameof(Person), personId,
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
            $"Name von {Pseudonym.Person(personId)} korrigiert (gilt rückwirkend ab {neu.GueltigVon:dd.MM.yyyy}, Details in der Datenhistorie)",
            nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return neu;
    }

    /// <summary>Führt eine Dublette in den Zieldatensatz über. Beide Historien bleiben erhalten; der Vorgang wird protokolliert.</summary>
    public async Task ZusammenfuehrenAsync(Guid zielId, Guid dubletteId, CancellationToken abbruch = default)
    {
        var ziel = await PersonLadenAsync(zielId, abbruch);
        var dublette = await PersonLadenAsync(dubletteId, abbruch);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var jetzt = uhr.GetLocalNow();
        ziel.DubletteUebernehmen(dublette, Domain.Gemeinsam.Erfassung.Aenderung(jetzt,
            $"Übernommen aus Dublette {dublette.Anzeigename} ({dublette.Id.ToString()[..8]})", wer.Anzeige));
        await protokoll.ProtokollierenAsync("Person.Zusammengefuehrt",
            $"Dublette {Pseudonym.Person(dubletteId)} in {Pseudonym.Person(zielId)} zusammengeführt; die Dublette bleibt mit Historie erhalten",
            nameof(Person), zielId, abbruch: abbruch);
        await protokoll.ProtokollierenAsync("Person.AlsDubletteMarkiert",
            $"Zusammengeführt in {Pseudonym.Person(zielId)}", nameof(Person), dubletteId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Hält fest, dass zwei ähnliche Datensätze verschiedene Personen sind (kein Dublettenhinweis mehr).</summary>
    public async Task KeineDubletteAsync(Guid a, Guid b, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        speicher.AusschlussHinzufuegen(new DublettenAusschluss(a, b, uhr.GetUtcNow(), wer.Anzeige));
        await protokoll.ProtokollierenAsync("Person.KeineDublette", "Zwei ähnliche Datensätze als verschiedene Personen bestätigt",
            nameof(Person), a, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task EinwilligungSetzenAsync(Guid personId, Kontaktkanal kanal, Kontaktzweck zweck, bool erteilt, DateOnly ab,
        string nachweis, CancellationToken abbruch = default)
    {
        var person = await PersonLadenAsync(personId, abbruch);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        person.EinwilligungSetzenAb(kanal, zweck, erteilt, ab, Domain.Gemeinsam.Erfassung.Aenderung(uhr.GetLocalNow(), nachweis, wer.Anzeige));
        await protokoll.ProtokollierenAsync("Person.Einwilligung",
            $"Einwilligung {Einwilligung.KanalAnzeige(kanal)} · {Einwilligung.ZweckAnzeige(zweck)} ab {ab:dd.MM.yyyy}: {(erteilt ? "erteilt" : "nicht erteilt")} (Nachweis in der Datenhistorie)",
            nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task VollmachtErteilenAsync(Guid personId, Bevollmaechtigtenart art, Guid bevollmaechtigterId, string bevollmaechtigterName,
        string nachweis, DateOnly von, DateOnly? bis, CancellationToken abbruch = default)
    {
        var person = await PersonLadenAsync(personId, abbruch);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        person.VollmachtErteilen(art, bevollmaechtigterId, bevollmaechtigterName, nachweis, von, bis,
            Domain.Gemeinsam.Erfassung.Aenderung(uhr.GetLocalNow(), $"Vollmacht erteilt, Nachweis: {nachweis}", wer.Anzeige));
        await protokoll.ProtokollierenAsync("Person.Vollmacht",
            $"Buchungsvollmacht für {(art == Bevollmaechtigtenart.Person ? Pseudonym.Person(bevollmaechtigterId) : bevollmaechtigterName)} ab {von:dd.MM.yyyy}{(bis is { } b ? $" bis {b.AddDays(-1):dd.MM.yyyy}" : "")}",
            nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task VollmachtWiderrufenAsync(Guid personId, Guid vollmachtId, DateOnly ab, string grund, CancellationToken abbruch = default)
    {
        var person = await PersonLadenAsync(personId, abbruch);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        person.VollmachtWiderrufenAb(vollmachtId, ab, Domain.Gemeinsam.Erfassung.Ende(uhr.GetLocalNow(), grund, wer.Anzeige));
        await protokoll.ProtokollierenAsync("Person.VollmachtWiderrufen", $"Buchungsvollmacht widerrufen ab {ab:dd.MM.yyyy}",
            nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }
    private async Task<Person> PersonLadenAsync(Guid id, CancellationToken abbruch)
        => await speicher.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
}
