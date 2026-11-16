using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

/// <summary>Meine Ansprache und Begrüßung in Verwaltung und Portalen.</summary>
public sealed record MeineAnsprache(Guid? PersonId, Ansprache Ansprache, bool Gesetzt, string Begruessung);

/// <summary>
/// Persönliche Einstellung „Wie möchte ich angesprochen werden?“ (Du/Sie, förmlich/freundlich) – gespeichert als zeitlich versioniertes
/// Merkmal an der eigenen Person (über das Benutzerkonto verknüpft), gültig für Verwaltung, Dozierenden- und Teilnehmendenportal und
/// für Mails an diese Person (geht dem Stil der Vorlage vor).
/// </summary>
public sealed class AnspracheDienst(IBenutzerkonten konten, IPersonenSpeicher personen, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    public async Task<MeineAnsprache> MeineAsync(CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var person = await EigenePersonAsync(wer, abbruch);
        var jetzt = uhr.GetLocalNow();
        if (person is null)
        {
            return new MeineAnsprache(null, Ansprache.Standard, false, $"Guten Tag, {wer.Name}");
        }

        var stand = person.StandAm(DateOnly.FromDateTime(jetzt.Date), jetzt);
        return new MeineAnsprache(person.Id, stand.Ansprache ?? Ansprache.Standard, stand.Ansprache is not null, stand.Briefanrede());
    }

    public async Task SetzenAsync(Ansprache ansprache, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(ansprache);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var person = await EigenePersonAsync(wer, abbruch)
                     ?? throw new InvalidOperationException("Ihr Benutzerkonto ist mit keiner Person verknüpft – bitte die Systemverwaltung, das Konto zu verknüpfen.");
        var jetzt = uhr.GetLocalNow();
        person.AngabeAendern(MerkmalArt.Ansprache, ansprache.Speicherwert, DateOnly.FromDateTime(jetzt.Date), Erfassung.Aenderung(jetzt, "Persönliche Einstellung", wer.Anzeige),
            korrektur: false);
        await protokoll.ProtokollierenAsync("Person.Ansprache", $"{Pseudonym.Person(person.Id)} hat die Ansprache auf „{ansprache.Speicherwert}“ gestellt", nameof(Person), person.Id,
            abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    private async Task<Person?> EigenePersonAsync(Benutzer wer, CancellationToken abbruch)
        => wer.IstAutomatik || await konten.FindenAsync(wer.Kennung, abbruch) is not { PersonId: { } pid }
            ? null
            : await personen.LadenAsync(pid, abbruch);
}
