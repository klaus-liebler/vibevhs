using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Tests;

internal sealed class FesterBenutzer(Benutzer benutzer) : IAktuellerBenutzer
{
    public static FesterBenutzer Krueger { get; } = new(new Benutzer("jkrueger", "J. Krüger", Rolle.Teilnehmerservice));

    public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default) => ValueTask.FromResult(benutzer);
}

internal sealed class SpeicherProtokoll(IAktuellerBenutzer benutzer, TimeProvider uhr) : IAuditProtokoll
{
    public List<AuditEintrag> Eintraege { get; } = [];

    public async Task ProtokollierenAsync(string aktion, string beschreibung, string? objektTyp = null, Guid? objektId = null,
        bool erfolgreich = true, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        Eintraege.Add(new AuditEintrag(uhr.GetUtcNow(), wer.Kennung, wer.Name, wer.Rolle?.ToString(), aktion, objektTyp,
            objektId, beschreibung, erfolgreich));
    }

    public Task<IReadOnlyList<AuditEintrag>> NeuesteAsync(int anzahl, CancellationToken abbruch = default)
        => Task.FromResult<IReadOnlyList<AuditEintrag>>(Eintraege.AsEnumerable().Reverse().Take(anzahl).ToList());
}

internal sealed class SpeicherPostausgang : INachrichtenSpeicher
{
    public List<AusgehendeNachricht> Nachrichten { get; } = [];

    public int Speichervorgaenge { get; private set; }

    public void Hinzufuegen(AusgehendeNachricht nachricht) => Nachrichten.Add(nachricht);

    public Task<AusgehendeNachricht?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => Task.FromResult(Nachrichten.FirstOrDefault(n => n.Id == id));

    public Task<IReadOnlyList<AusgehendeNachricht>> WartendeAsync(int maximal, CancellationToken abbruch = default)
        => Task.FromResult<IReadOnlyList<AusgehendeNachricht>>(
            Nachrichten.Where(n => n.Status == NachrichtenStatus.Wartend).Take(maximal).ToList());

    public Task<IReadOnlyList<AusgehendeNachricht>> NeuesteAsync(int anzahl, CancellationToken abbruch = default)
        => Task.FromResult<IReadOnlyList<AusgehendeNachricht>>(Nachrichten.Take(anzahl).ToList());

    public Task<int> AnzahlAsync(NachrichtenStatus status, CancellationToken abbruch = default)
        => Task.FromResult(Nachrichten.Count(n => n.Status == status));

    public Task SpeichernAsync(CancellationToken abbruch = default)
    {
        Speichervorgaenge++;
        return Task.CompletedTask;
    }
}

internal sealed class SpeicherPersonen : IPersonenSpeicher
{
    public List<Person> Personen { get; } = [];

    public int Speichervorgaenge { get; private set; }

    public void Hinzufuegen(Person person) => Personen.Add(person);

    public Task<Person?> LadenAsync(Guid id, CancellationToken abbruch = default)
        => Task.FromResult(Personen.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Person>> LadenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default)
        => Task.FromResult<IReadOnlyList<Person>>(Personen.Where(p => ids.Contains(p.Id)).ToList());

    public List<DublettenAusschluss> Ausschluesse { get; } = [];

    public void AusschlussHinzufuegen(DublettenAusschluss ausschluss) => Ausschluesse.Add(ausschluss);

    public Task<IReadOnlyList<Person>> AlleAsync(CancellationToken abbruch = default)
        => Task.FromResult<IReadOnlyList<Person>>(Personen);

    public Task<int> AnzahlAsync(CancellationToken abbruch = default) => Task.FromResult(Personen.Count);

    public Task<IReadOnlyDictionary<Guid, string>> AnzeigenamenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, string>>(Personen.Where(p => ids.Contains(p.Id)).ToDictionary(p => p.Id, p => p.Anzeigename));

    public Task SpeichernAsync(CancellationToken abbruch = default)
    {
        Speichervorgaenge++;
        return Task.CompletedTask;
    }
}

internal sealed class TestKanal(Kanal kanal, Func<AusgehendeNachricht, string?> verhalten) : IZustellKanal
{
    public Kanal Kanal => kanal;

    public List<AusgehendeNachricht> Zugestellt { get; } = [];

    public Task<string?> ZustellenAsync(AusgehendeNachricht nachricht, CancellationToken abbruch = default)
    {
        var rueckmeldung = verhalten(nachricht);
        Zugestellt.Add(nachricht);
        return Task.FromResult(rueckmeldung);
    }
}
