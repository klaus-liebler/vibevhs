using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

public interface ISchluesselSpeicher
{
    Task<IReadOnlyList<Schluessel>> AlleAsync(CancellationToken abbruch = default);

    Task<Schluessel?> LadenAsync(Guid id, CancellationToken abbruch = default);

    Task<bool> NummerVergebenAsync(string nummer, CancellationToken abbruch = default);

    Task<IReadOnlyList<Schluesselausgabe>> AusgabenAsync(bool nurOffene, CancellationToken abbruch = default);

    Task<Schluesselausgabe?> AusgabeAsync(Guid id, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Ein Schlüssel mit Ort und – falls ausgegeben – wer ihn seit wann hat.</summary>
public sealed record Schluesselzeile(Schluessel Schluessel, string Ort, Schluesselausgabe? Ausgabe, string? Inhaber);

/// <summary>
/// Schlüsselverwaltung (QA-67, aus SchoolCraft): Schlüssel je Kursort anlegen, an Kursleitungen oder Mitarbeitende ausgeben (nur an diesen
/// Kreis), Rückgabe festhalten, verlorene Schlüssel außer Betrieb nehmen. Nachts entsteht eine Aufgabe „Schlüssel zurückfordern“, wenn
/// jemand einen Schlüssel hat, aber am Ort in den nächsten Wochen keine Veranstaltung mehr leitet.
/// </summary>
public sealed class SchluesselDienst(ISchluesselSpeicher speicher, IProgrammSpeicher programm, IPersonenSpeicher personen, AufgabenDienst aufgaben,
    IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    public static readonly Rolle[] Rollen = [Rolle.Teilnehmerservice, Rolle.Verwaltungsleitung, Rolle.Leitung, Rolle.Systemverwaltung];

    /// <summary>So weit voraus zählen künftige Veranstaltungen als Grund, den Schlüssel zu behalten.</summary>
    public static readonly TimeSpan Vorschau = TimeSpan.FromDays(42);

    private const string Objekt = nameof(Schluessel);

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    private async Task<Benutzer> RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var f = Freigabe.NurFuer(wer.Rolle, Rollen);
        if (!wer.IstAutomatik && !f.Erlaubt)
        {
            throw new InvalidOperationException(f.Begruendung);
        }

        return wer;
    }

    public async Task<IReadOnlyList<Schluesselzeile>> ListeAsync(CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var schluessel = await speicher.AlleAsync(abbruch);
        var offen = (await speicher.AusgabenAsync(true, abbruch)).ToDictionary(a => a.SchluesselId);
        var namen = await programm.LadenAsync(offen.Values.Select(a => a.PersonId), schluessel.Select(s => s.OrtId), [], abbruch);
        return schluessel
            .Select(s => new Schluesselzeile(s, namen.Ort(s.OrtId), offen.GetValueOrDefault(s.Id),
                offen.TryGetValue(s.Id, out var a) ? namen.Person(a.PersonId, Heute) : null))
            .OrderBy(z => z.Ort, StringComparer.CurrentCulture).ThenBy(z => z.Schluessel.Nummer, StringComparer.CurrentCulture).ToList();
    }

    public async Task<Schluessel> AnlegenAsync(string nummer, string bezeichnung, Guid ortId, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        if (string.IsNullOrWhiteSpace(nummer) || string.IsNullOrWhiteSpace(bezeichnung))
        {
            throw new ArgumentException("Bitte Nummer und Bezeichnung angeben.");
        }

        if (await speicher.NummerVergebenAsync(nummer.Trim(), abbruch))
        {
            throw new InvalidOperationException($"Die Nummer „{nummer.Trim()}“ ist schon vergeben.");
        }

        var s = new Schluessel(nummer, bezeichnung, ortId, uhr.GetLocalNow());
        speicher.Hinzufuegen(s);
        await protokoll.ProtokollierenAsync("Schluessel.Angelegt", $"Schlüssel {s.Nummer} ({s.Bezeichnung}) angelegt", Objekt, s.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return s;
    }

    /// <summary>Ausgabe an eine Kursleitung oder Mitarbeitende (QA-60/FA-3228: nur im passenden Kreis, sonst erklärt abgelehnt).</summary>
    public async Task AusgebenAsync(Guid schluesselId, Guid personId, DateOnly am, string? bemerkung, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var s = await speicher.LadenAsync(schluesselId, abbruch) ?? throw new InvalidOperationException("Schlüssel nicht gefunden.");
        if (!s.InBetrieb)
        {
            throw new InvalidOperationException($"Schlüssel {s.Nummer} ist außer Betrieb ({s.AusserBetriebGrund}).");
        }

        if ((await speicher.AusgabenAsync(true, abbruch)).Any(a => a.SchluesselId == s.Id))
        {
            throw new InvalidOperationException($"Schlüssel {s.Nummer} ist schon ausgegeben – erst die Rückgabe eintragen.");
        }

        var p = await personen.LadenAsync(personId, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
        if ((p.Rollen & (Personenrolle.Dozierende | Personenrolle.Mitarbeitende)) == 0)
        {
            throw new InvalidOperationException("Schlüssel gehen nur an Kursleitungen (Rolle Dozierende) oder Mitarbeitende.");
        }

        speicher.Hinzufuegen(new Schluesselausgabe(s.Id, personId, am, wer.Anzeige, bemerkung));
        await protokoll.ProtokollierenAsync("Schluessel.Ausgegeben", $"Schlüssel {s.Nummer} an {Pseudonym.Person(personId)} ausgegeben ({am:dd.MM.yyyy})", Objekt, s.Id,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task ZurueckAsync(Guid ausgabeId, DateOnly am, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var a = await speicher.AusgabeAsync(ausgabeId, abbruch) ?? throw new InvalidOperationException("Ausgabe nicht gefunden.");
        a.Zurueck(am, wer.Anzeige);
        await protokoll.ProtokollierenAsync("Schluessel.Zurueck", $"Schlüssel zurück von {Pseudonym.Person(a.PersonId)} ({am:dd.MM.yyyy})", Objekt, a.SchluesselId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Verloren oder ausgesondert; eine offene Ausgabe wird mit diesem Tag beendet.</summary>
    public async Task AusserBetriebAsync(Guid schluesselId, string grund, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var s = await speicher.LadenAsync(schluesselId, abbruch) ?? throw new InvalidOperationException("Schlüssel nicht gefunden.");
        s.AusserBetrieb(Heute, grund);
        foreach (var a in (await speicher.AusgabenAsync(true, abbruch)).Where(a => a.SchluesselId == s.Id))
        {
            (await speicher.AusgabeAsync(a.Id, abbruch))!.Zurueck(Heute, wer.Anzeige);
        }

        await protokoll.ProtokollierenAsync("Schluessel.AusserBetrieb", $"Schlüssel {s.Nummer} außer Betrieb: {grund}", Objekt, s.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>
    /// Nächtlich: Aufgabe „Schlüssel zurückfordern“ für jede offene Ausgabe, deren Inhaber am Ort (samt Räumen) in den nächsten
    /// <see cref="Vorschau"/> keine Veranstaltung leitet; erledigt sich selbst mit der Rückgabe oder einem neuen Einsatz.
    /// </summary>
    public async Task<Abgleichergebnis> RueckgabeAufgabenAsync(CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var offen = await speicher.AusgabenAsync(true, abbruch);
        var schluessel = (await speicher.AlleAsync(abbruch)).ToDictionary(s => s.Id);
        var jetzt = uhr.GetLocalNow().LocalDateTime;
        var termine = offen.Count == 0 ? [] : await programm.TermineAsync(jetzt, jetzt + Vorschau, abbruch);
        var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
        var namen = await programm.LadenAsync(offen.Select(a => a.PersonId), schluessel.Values.Select(s => s.OrtId), [], abbruch);
        var anlaesse = new List<AufgabenAnlass>();
        foreach (var a in offen)
        {
            var s = schluessel[a.SchluesselId];
            var orte = Unterorte(verzeichnis, s.OrtId);
            var nochEinsatz = termine.Any(t => !t.Termin.Ausgefallen && t.Termin.DozentId == a.PersonId && t.Termin.OrtIds.Any(orte.Contains));
            if (!nochEinsatz)
            {
                anlaesse.Add(new AufgabenAnlass($"schluessel:{a.Id}", $"Schlüssel {s.Nummer} zurückfordern",
                    $"{namen.Person(a.PersonId, Heute)} hat seit {a.AusgegebenAm:dd.MM.yyyy} den Schlüssel {s.Nummer} ({s.Bezeichnung}, {namen.Ort(s.OrtId)}), " +
                    $"leitet dort aber in den nächsten {Vorschau.TotalDays:0} Tagen keine Veranstaltung mehr.", Heute.AddDays(7), "schluessel"));
            }
        }

        return await aufgaben.AbgleichenAsync(Aufgabentypen.SchluesselRueckgabe, anlaesse,
            grund: _ => "Schlüssel zurückgegeben oder neuer Einsatz am Ort", abbruch: abbruch);
    }

    private static HashSet<Guid> Unterorte(Ortsverzeichnis verzeichnis, Guid wurzel)
    {
        var ergebnis = new HashSet<Guid> { wurzel };
        var offen = new Queue<Guid>([wurzel]);
        while (offen.TryDequeue(out var id))
        {
            foreach (var kind in verzeichnis.Kinder(id).Where(k => ergebnis.Add(k.Id)))
            {
                offen.Enqueue(kind.Id);
            }
        }

        return ergebnis;
    }
}
