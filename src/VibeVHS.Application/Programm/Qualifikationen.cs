using VibeVHS.Application.Personen;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

public interface IQualifikationsSpeicher
{
    Task<IReadOnlyDictionary<string, Merkmal>> KatalogAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Qualifikationsausnahme>> AusnahmenAsync(Guid kursId, CancellationToken abbruch = default);

    /// <summary>Kursart verfolgt (für Änderungen).</summary>
    Task<Kursart?> KursartLadenAsync(Guid id, CancellationToken abbruch = default);

    /// <summary>Nicht archivierte Kurse einer Kursart, verfolgt.</summary>
    Task<IReadOnlyList<Kurs>> KurseDerKursartAsync(Guid kursartId, CancellationToken abbruch = default);

    /// <summary>Nicht abgesagte, nicht archivierte Kurse mit Terminen im Zeitraum (mit Veranstaltungen).</summary>
    Task<IReadOnlyList<Kurs>> KurseMitTerminenZwischenAsync(DateTime von, DateTime bis, CancellationToken abbruch = default);

    /// <summary>Personen mit Qualifikationen, die vor <paramref name="bis"/> ablaufen.</summary>
    Task<IReadOnlyList<Person>> PersonenMitAblaufendenQualifikationenAsync(DateOnly bis, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Prüft den Einsatz von Dozierenden (für die Programmplanung).</summary>
public interface IDozenteneinsatz
{
    /// <summary>Eignung einer Person für die Tage; <paramref name="kursId"/> berücksichtigt erteilte Ausnahmen.</summary>
    Task<Dozenteneignung> PruefenAsync(Guid personId, IEnumerable<string> benoetigt, IEnumerable<DateOnly> tage, Guid? kursId,
        CancellationToken abbruch = default);

    Task AusnahmeErteilenAsync(Guid kursId, Guid personId, IEnumerable<string> schluessel, string begruendung, CancellationToken abbruch = default);
}

/// <summary>Eine Person in der Dozierendenauswahl mit ihrer Eignung für die gewählten Tage.</summary>
public sealed record DozentOption(PersonZeile Person, Dozenteneignung Eignung);

/// <summary>
/// Beschäftigungsart und Qualifikationen von Dozierenden, Anforderungen der Kursarten, Ausnahmen der Fachbereichsleitung
/// und Aufgaben vor Ablauf. Jede Änderung wird protokolliert.
/// </summary>
public sealed class QualifikationsDienst(
    IQualifikationsSpeicher speicher,
    IProgrammSpeicher programm,
    IPersonenSpeicher personen,
    IPersonenAbfrage abfrage,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr,
    Aufgaben.AufgabenDienst aufgaben) : IDozenteneinsatz
{
    /// <summary>Wer Qualifikationen und Beschäftigungsart pflegt.</summary>
    public static readonly Rolle[] PflegeRollen = [Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    /// <summary>Wer Ausnahmen erteilt.</summary>
    public static readonly Rolle[] AusnahmeRollen = [Rolle.Fachbereichsleitung, Rolle.Leitung];

    /// <summary>Mitgelieferte Qualifikationsmerkmale (Vorschläge; im Katalog änderbar).</summary>
    public static IReadOnlyList<Merkmal> Vorschlaege { get; } =
    [
        new("bamf-lk", "BAMF-Lehrkraftzulassung Integrationskurs", "Qualifikation", nachweisPflicht: true, bezug: MerkmalBezug.Person, gesetzlich: true,
            werte: ["direkt", "mit Auflagen", "nach Zusatzqualifizierung"], vorlaufTage: 90),
        new("alpha", "Zusatzqualifikation Alphabetisierung", "Qualifikation", nachweisPflicht: true, bezug: MerkmalBezug.Person, gesetzlich: true, vorlaufTage: 90),
        new("telc-pruefer", "Prüferlizenz telc", "Prüfung", nachweisPflicht: true, bezug: MerkmalBezug.Person),
        new("goethe-pruefer", "Prüferlizenz Goethe-Institut", "Prüfung", nachweisPflicht: true, bezug: MerkmalBezug.Person),
        new("uebungsleiter", "Übungsleiterlizenz", "Qualifikation", nachweisPflicht: true, bezug: MerkmalBezug.Person),
        new("erste-hilfe", "Erste-Hilfe-Nachweis", "Sicherheit", nachweisPflicht: true, bezug: MerkmalBezug.Person),
        new("fuehrungszeugnis", "Erweitertes Führungszeugnis", "Sicherheit", nachweisPflicht: true, bezug: MerkmalBezug.Person, gesetzlich: true),
        // Interne Freigabe der vhs (Eignungsprüfung, Hospitation, Gespräch mit der Fachbereichsleitung) je Sprache; Ausnahme möglich.
        new(Sprachfreigabe.Schluessel, "Lehrfreigabe Sprachkurs (Eignungsprüfung der vhs)", "Freigabe", nachweisPflicht: true, bezug: MerkmalBezug.Person,
            werte: Sprachfreigabe.Sprachen, vorlaufTage: 60, mehrfach: true),
        new("daf-daz", "Qualifikation Deutsch als Fremd-/Zweitsprache (Studium oder Zusatzqualifikation)", "Qualifikation", nachweisPflicht: true,
            bezug: MerkmalBezug.Person),
    ];

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    /// <summary>
    /// Legt fehlende Qualifikationsmerkmale an. Beim ersten Mal verlangt die Kursart „Integrationskurs“ die BAMF-Lehrkraftzulassung
    /// und Alphabetisierungskurse zusätzlich die Alpha-Zusatzqualifikation.
    /// </summary>
    public async Task GrundeinstellungenAsync(CancellationToken abbruch = default)
    {
        var katalog = await speicher.KatalogAsync(abbruch);
        var ersteMal = !katalog.Values.Any(m => m.Bezug == MerkmalBezug.Person);
        var neu = Vorschlaege.Where(v => !katalog.ContainsKey(v.Schluessel)).ToList();
        foreach (var m in neu)
        {
            speicher.Hinzufuegen(new Merkmal(m.Schluessel, m.Name, m.Gruppe, false, m.NachweisPflicht, null, m.Bezug, m.Gesetzlich, m.Werte, m.VorlaufTage, m.Mehrfach));
        }

        // Sprachfreigabe neu im Katalog: Sprachkurs-Kursarten verlangen sie, jeder Kurs für seine Sprache (aus dem Titel).
        var sprachkurse = 0;
        if (neu.Any(m => m.Schluessel == Sprachfreigabe.Schluessel))
        {
            foreach (var art in (await programm.KursartenAsync(abbruch))
                         .Where(a => a.Nutzungsart != Nutzungsart.Integrationskurs && a.Name.Contains("Sprach", StringComparison.OrdinalIgnoreCase)))
            {
                var verfolgt = await speicher.KursartLadenAsync(art.Id, abbruch);
                verfolgt?.MitDozierendenmerkmalen(art.BenoetigteDozierendenmerkmale.Append(Sprachfreigabe.Schluessel));
                foreach (var kurs in await speicher.KurseDerKursartAsync(art.Id, abbruch))
                {
                    kurs.MitDozierendenmerkmalen(Sprachfreigabe.Konkretisieren(kurs.BenoetigteDozierendenmerkmale.Append(Sprachfreigabe.Schluessel), kurs.Titel));
                    sprachkurse++;
                }
            }
        }

        if (ersteMal)
        {
            foreach (var art in (await programm.KursartenAsync(abbruch)).Where(a => a.Nutzungsart == Nutzungsart.Integrationskurs))
            {
                var verfolgt = await speicher.KursartLadenAsync(art.Id, abbruch);
                verfolgt?.MitDozierendenmerkmalen(["bamf-lk"]);
                foreach (var kurs in await speicher.KurseDerKursartAsync(art.Id, abbruch))
                {
                    kurs.MitDozierendenmerkmalen(kurs.Titel.Contains("Alphabetisierung", StringComparison.OrdinalIgnoreCase) ? ["bamf-lk", "alpha"] : ["bamf-lk"]);
                }
            }
        }

        if (neu.Count > 0)
        {
            await protokoll.ProtokollierenAsync("Qualifikation.Katalog", $"{neu.Count} Qualifikationsmerkmale für Dozierende in den Merkmalskatalog aufgenommen (Vorschläge)"
                + (sprachkurse > 0 ? $"; {sprachkurse} Sprachkurse verlangen ab jetzt die Lehrfreigabe für ihre Sprache" : ""),
                abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
        }
    }

    public async Task<IReadOnlyList<Merkmal>> PersonenmerkmaleAsync(CancellationToken abbruch = default)
        => (await speicher.KatalogAsync(abbruch)).Values.Where(m => m.Bezug == MerkmalBezug.Person).OrderBy(m => m.Gruppe).ThenBy(m => m.Name).ToList();

    public Task<IReadOnlyDictionary<string, Merkmal>> KatalogAsync(CancellationToken abbruch = default) => speicher.KatalogAsync(abbruch);

    public async Task MerkmalAufnehmenAsync(Merkmal merkmal, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(merkmal);
        await RolleAsync([Rolle.Verwaltungsleitung, Rolle.Leitung, Rolle.Systemverwaltung], abbruch);
        if ((await speicher.KatalogAsync(abbruch)).ContainsKey(merkmal.Schluessel))
        {
            throw new InvalidOperationException("Diesen Schlüssel gibt es schon.");
        }

        speicher.Hinzufuegen(merkmal);
        await protokoll.ProtokollierenAsync("Qualifikation.Katalog", $"Merkmal „{merkmal.Name}“ ({(merkmal.Bezug == MerkmalBezug.Person ? "Person" : "Ort")}{(merkmal.Gesetzlich ? ", gesetzlich" : "")}) aufgenommen",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Anforderungen einer Kursart an Dozierende; gilt auch für alle nicht archivierten Kurse dieser Art (kursbezogene Ergänzungen bleiben).</summary>
    public async Task KursartAnforderungenSetzenAsync(Guid kursartId, IReadOnlyList<string> schluessel, CancellationToken abbruch = default)
    {
        await RolleAsync([Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung], abbruch);
        var art = await speicher.KursartLadenAsync(kursartId, abbruch) ?? throw new InvalidOperationException("Kursart nicht gefunden.");
        var alt = art.BenoetigteDozierendenmerkmale.ToList();
        art.MitDozierendenmerkmalen(schluessel);
        foreach (var kurs in await speicher.KurseDerKursartAsync(kursartId, abbruch))
        {
            // Kurseigene Ergänzungen bleiben; was von der Kursart kam (auch als konkretisierte Sprachfreigabe), wird ersetzt.
            var eigene = kurs.BenoetigteDozierendenmerkmale.Where(a => !alt.Contains(a) && !alt.Contains(Anforderung.Basis(a))).ToList();
            kurs.MitDozierendenmerkmalen(Sprachfreigabe.Konkretisieren(eigene.Concat(schluessel), kurs.Titel));
        }

        var katalog = await speicher.KatalogAsync(abbruch);
        await protokoll.ProtokollierenAsync("Qualifikation.Kursart",
            $"Kursart „{art.Name}“ verlangt von Dozierenden: {(schluessel.Count == 0 ? "nichts" : string.Join(", ", schluessel.Select(s => Anforderung.Anzeige(s, katalog))))}",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    // ---------- Person ----------

    public async Task BeschaeftigungAendernAsync(Guid personId, Beschaeftigungsart art, DateOnly ab, string grund, CancellationToken abbruch = default)
    {
        var wer = await RolleAsync(PflegeRollen, abbruch);
        var person = await PersonAsync(personId, abbruch);
        person.BeschaeftigungAendernAb(art, ab, Erfassung.Aenderung(uhr.GetLocalNow(), grund, wer.Anzeige));
        await protokoll.ProtokollierenAsync("Person.Beschaeftigung", $"Beschäftigungsart von {Pseudonym.Person(personId)} ab {ab:dd.MM.yyyy}: {BeschaeftigungAnzeige.Text(art)}",
            nameof(Person), personId, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    public async Task QualifikationSetzenAsync(Guid personId, string schluessel, string? wert, string? nachweis, string? stelle, DateOnly ab, DateOnly? bis,
        CancellationToken abbruch = default)
    {
        var wer = await RolleAsync(PflegeRollen, abbruch);
        var person = await PersonAsync(personId, abbruch);
        var merkmal = (await speicher.KatalogAsync(abbruch)).GetValueOrDefault(schluessel) ?? throw new InvalidOperationException("Merkmal nicht im Katalog.");
        person.QualifikationSetzenAb(merkmal, wert, nachweis, stelle, ab, bis, Erfassung.Aenderung(uhr.GetLocalNow(), nachweis ?? "Qualifikation eingetragen", wer.Anzeige));
        await protokoll.ProtokollierenAsync("Person.Qualifikation",
            $"{Pseudonym.Person(personId)}: „{merkmal.Name}“ ab {ab:dd.MM.yyyy}{(bis is { } b ? $" bis {b:dd.MM.yyyy}" : "")} eingetragen (Nachweis in der Datenhistorie)",
            nameof(Person), personId, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    public async Task QualifikationBeendenAsync(Guid personId, string schluessel, DateOnly ab, string grund, CancellationToken abbruch = default)
    {
        var wer = await RolleAsync(PflegeRollen, abbruch);
        var person = await PersonAsync(personId, abbruch);
        person.QualifikationBeendenAb(schluessel, ab, Erfassung.Ende(uhr.GetLocalNow(), grund, wer.Anzeige));
        await protokoll.ProtokollierenAsync("Person.QualifikationBeendet", $"{Pseudonym.Person(personId)}: Qualifikation „{schluessel}“ beendet ab {ab:dd.MM.yyyy}",
            nameof(Person), personId, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    // ---------- Prüfung ----------

    public async Task<Dozenteneignung> PruefenAsync(Guid personId, IEnumerable<string> benoetigt, IEnumerable<DateOnly> tage, Guid? kursId,
        CancellationToken abbruch = default)
    {
        var person = await PersonAsync(personId, abbruch);
        var ausnahmen = kursId is { } k ? await speicher.AusnahmenAsync(k, abbruch) : [];
        return Dozentenpruefung.Pruefen(person, benoetigt, await speicher.KatalogAsync(abbruch), tage, uhr.GetLocalNow(), ausnahmen);
    }

    /// <summary>Dozierende suchen und je Treffer die Eignung für die Tage prüfen (gesperrte Auswahl mit Tooltip).</summary>
    public async Task<IReadOnlyList<DozentOption>> DozentenauswahlAsync(string? suche, IReadOnlyList<string> benoetigt, IReadOnlyList<DateOnly> tage, Guid? kursId,
        CancellationToken abbruch = default)
    {
        if (string.IsNullOrWhiteSpace(suche))
        {
            return [];
        }

        var treffer = await abfrage.SeiteAsync(new PersonenFilter(suche, Personenrolle.Dozierende), null, 8, abbruch);
        var katalog = await speicher.KatalogAsync(abbruch);
        var ausnahmen = kursId is { } k ? await speicher.AusnahmenAsync(k, abbruch) : [];
        var liste = new List<DozentOption>();
        foreach (var z in treffer)
        {
            if (await personen.LadenAsync(z.Id, abbruch) is { } p)
            {
                liste.Add(new DozentOption(z, Dozentenpruefung.Pruefen(p, benoetigt, katalog, tage.Count > 0 ? tage : [Heute], uhr.GetLocalNow(), ausnahmen)));
            }
        }

        return liste;
    }

    /// <summary>Eignung des Teams für die künftigen Veranstaltungen des Kurses.</summary>
    public async Task<IReadOnlyList<Dozenteneignung>> TeamEignungAsync(Kurs kurs, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(kurs);
        var tage = KuenftigeTage(kurs);
        var katalog = await speicher.KatalogAsync(abbruch);
        var ausnahmen = await speicher.AusnahmenAsync(kurs.Id, abbruch);
        var ergebnis = new List<Dozenteneignung>();
        foreach (var d in kurs.Team)
        {
            if (await personen.LadenAsync(d.PersonId, abbruch) is { } p)
            {
                ergebnis.Add(Dozentenpruefung.Pruefen(p, kurs.BenoetigteDozierendenmerkmale, katalog, tage.Count > 0 ? tage : [Heute], uhr.GetLocalNow(), ausnahmen));
            }
        }

        return ergebnis;
    }

    /// <summary>Markierung: künftige Veranstaltungen, deren Dozent/in am Tag nicht geeignet ist (mit Grund).</summary>
    public async Task<IReadOnlyDictionary<Guid, Tageseignung>> BetroffeneVeranstaltungenAsync(Kurs kurs, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(kurs);
        var ergebnis = new Dictionary<Guid, Tageseignung>();
        if (kurs.BenoetigteDozierendenmerkmale.Count == 0)
        {
            return ergebnis;
        }

        var katalog = await speicher.KatalogAsync(abbruch);
        var ausnahmen = await speicher.AusnahmenAsync(kurs.Id, abbruch);
        var jetzt = uhr.GetLocalNow();
        foreach (var gruppe in kurs.Veranstaltungen.Where(v => !v.Ausgefallen && v.Datum >= Heute && v.DozentId is not null).GroupBy(v => v.DozentId!.Value))
        {
            var p = await personen.LadenAsync(gruppe.Key, abbruch);
            foreach (var v in gruppe)
            {
                var e = p is null
                    ? new Tageseignung(v.Datum, Eignungscode.MerkmalFehlt, "Dozent/in nicht gefunden.", kurs.BenoetigteDozierendenmerkmale, false)
                    : Dozentenpruefung.AmTag(p, kurs.BenoetigteDozierendenmerkmale, katalog, v.Datum, jetzt, ausnahmen);
                if (!e.Geeignet || e.Code == Eignungscode.Ausnahme)
                {
                    ergebnis[v.Id] = e;
                }
            }
        }

        return ergebnis;
    }

    public async Task<IReadOnlyList<Qualifikationsausnahme>> AusnahmenAsync(Guid kursId, CancellationToken abbruch = default)
        => await speicher.AusnahmenAsync(kursId, abbruch);

    /// <summary>Ausnahme der Fachbereichsleitung für fehlende, nicht gesetzliche Merkmale (mit Begründung, protokolliert).</summary>
    public async Task AusnahmeErteilenAsync(Guid kursId, Guid personId, IEnumerable<string> schluessel, string begruendung, CancellationToken abbruch = default)
    {
        var wer = await RolleAsync(AusnahmeRollen, abbruch);
        if (string.IsNullOrWhiteSpace(begruendung))
        {
            throw new InvalidOperationException("Eine Ausnahme braucht eine Begründung.");
        }

        var katalog = await speicher.KatalogAsync(abbruch);
        var vorhanden = await speicher.AusnahmenAsync(kursId, abbruch);
        var jetzt = uhr.GetUtcNow();
        // Ausnahmen gelten je Merkmal (auch für „Lehrfreigabe Sprachkurs: Englisch“ die Lehrfreigabe insgesamt in diesem Kurs).
        foreach (var s in schluessel.Select(Anforderung.Basis).Distinct().Where(s => !vorhanden.Any(a => a.PersonId == personId && a.Schluessel == s)))
        {
            var merkmal = katalog.GetValueOrDefault(s) ?? throw new InvalidOperationException("Merkmal nicht im Katalog.");
            speicher.Hinzufuegen(new Qualifikationsausnahme(kursId, personId, merkmal, begruendung, wer.Anzeige, jetzt));
            await protokoll.ProtokollierenAsync("Qualifikation.Ausnahme",
                $"Ausnahme für {Pseudonym.Person(personId)} im Kurs {kursId.ToString()[..8]}: „{merkmal.Name}“ fehlt; Begründung: {begruendung.Trim()}", nameof(Person), personId,
                abbruch: abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Team ändern: Ungeeignete Dozierende werden abgelehnt (außer mit Ausnahme).</summary>
    public async Task TeamHinzufuegenAsync(Guid kursId, Guid personId, bool leitung, string? ausnahmebegruendung = null, CancellationToken abbruch = default)
    {
        var wer = await RolleAsync([Rolle.Fachbereichsleitung, Rolle.Leitung], abbruch);
        var kurs = await programm.KursLadenAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        if (kurs.Archiviert)
        {
            throw new InvalidOperationException("Der Kurs ist archiviert und schreibgeschützt.");
        }

        var person = await PersonAsync(personId, abbruch);
        var tage = KuenftigeTage(kurs);
        var eignung = await PruefenAsync(personId, kurs.BenoetigteDozierendenmerkmale, tage.Count > 0 ? tage : [Heute], kursId, abbruch);
        if (!eignung.Geeignet)
        {
            if (string.IsNullOrWhiteSpace(ausnahmebegruendung) || !eignung.AusnahmeMoeglich)
            {
                throw new InvalidOperationException(eignung.Begruendung);
            }

            await AusnahmeErteilenAsync(kursId, personId, eignung.Tage.SelectMany(t => t.Fehlend), ausnahmebegruendung, abbruch);
        }

        var team = kurs.Team.Where(t => t.PersonId != personId).Select(t => leitung ? t with { Leitung = false } : t).ToList();
        team.Add(new KursDozent(personId, person.Anzeigename, leitung || team.Count == 0));
        kurs.MitTeam(team);
        await protokoll.ProtokollierenAsync("Kurs.Team", $"{kurs.Kursnummer}: {Pseudonym.Person(personId)} ins Dozierendenteam aufgenommen{(leitung ? " (Leitung)" : "")} von {wer.Anzeige}",
            nameof(Kurs), kursId, abbruch: abbruch);
        await programm.SpeichernAsync(abbruch);
    }

    public async Task TeamEntfernenAsync(Guid kursId, Guid personId, CancellationToken abbruch = default)
    {
        await RolleAsync([Rolle.Fachbereichsleitung, Rolle.Leitung], abbruch);
        var kurs = await programm.KursLadenAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        if (kurs.Veranstaltungen.Any(v => v.DozentId == personId && v.Datum >= Heute && !v.Ausgefallen))
        {
            throw new InvalidOperationException("Die Person leitet noch künftige Veranstaltungen. Bitte diese zuerst einer anderen Person zuordnen.");
        }

        kurs.MitTeam(kurs.Team.Where(t => t.PersonId != personId));
        await protokoll.ProtokollierenAsync("Kurs.Team", $"{kurs.Kursnummer}: {Pseudonym.Person(personId)} aus dem Dozierendenteam entfernt", nameof(Kurs), kursId, abbruch: abbruch);
        await programm.SpeichernAsync(abbruch);
    }

    /// <summary>
    /// Sprache eines Sprachkurses festlegen (wenn der Titel sie nicht verrät oder sie abweicht): Dozierende brauchen dann die
    /// Lehrfreigabe für genau diese Sprache. Null = Lehrfreigabe für irgendeine Sprache genügt.
    /// </summary>
    public async Task KursSpracheSetzenAsync(Guid kursId, string? sprache, CancellationToken abbruch = default)
    {
        await RolleAsync([Rolle.Fachbereichsleitung, Rolle.Leitung], abbruch);
        if (sprache is not null && !Sprachfreigabe.Sprachen.Contains(sprache))
        {
            throw new InvalidOperationException("Unbekannte Sprache.");
        }

        var kurs = await programm.KursLadenAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        kurs.MitDozierendenmerkmalen(kurs.BenoetigteDozierendenmerkmale.Where(a => Anforderung.Basis(a) != Sprachfreigabe.Schluessel)
            .Append(Anforderung.Bilden(Sprachfreigabe.Schluessel, sprache)));
        await protokoll.ProtokollierenAsync("Qualifikation.Kurs",
            $"{kurs.Kursnummer}: Dozierende brauchen die Lehrfreigabe {(sprache is null ? "für eine beliebige Sprache" : $"für {sprache}")}", nameof(Kurs), kursId,
            abbruch: abbruch);
        await programm.SpeichernAsync(abbruch);
    }

    // ---------- Automatik ----------

    /// <summary>
    /// Aufgaben für die Fachbereichsleitung: Qualifikation läuft bald ab, Nachweis fehlt, künftige Veranstaltungen mit ungeeigneter
    /// Dozentin / ungeeignetem Dozenten (je Kurs und Person einmal).
    /// </summary>
    public async Task<Aufgaben.Abgleichergebnis> AufgabenErzeugenAsync(int tageVoraus = 180, CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetLocalNow();
        var heute = Heute;
        var katalog = await speicher.KatalogAsync(abbruch);
        var ablauf = new List<Aufgaben.AufgabenAnlass>();
        var nachweis = new List<Aufgaben.AufgabenAnlass>();
        var einsatz = new List<Aufgaben.AufgabenAnlass>();

        foreach (var p in await speicher.PersonenMitAblaufendenQualifikationenAsync(heute.AddDays(katalog.Values.Select(m => m.VorlaufTage).DefaultIfEmpty(60).Max()), abbruch))
        {
            foreach (var q in p.Qualifikationen.AlleAmStichtag(heute, jetzt).Where(q => q.GueltigBisEinschliesslich is { } b
                         && b <= heute.AddDays(katalog.GetValueOrDefault(q.Schluessel)?.VorlaufTage ?? 60)))
            {
                var bis = q.GueltigBisEinschliesslich!.Value;
                ablauf.Add(new($"qualifikation-ablauf:{p.Id:N}:{q.Schluessel}:{bis:yyyyMMdd}", $"{p.Anzeigename}: „{q.Bezeichnung}“ läuft am {bis:dd.MM.yyyy} ab",
                    "Neuen Nachweis anfordern und eintragen; sonst sind die betroffenen Veranstaltungen umzuplanen.", bis.AddDays(-14), $"personen/{p.Id}"));
            }

            foreach (var q in p.Qualifikationen.AlleAmStichtag(heute, jetzt).Where(q => q.Nachweis is null && katalog.GetValueOrDefault(q.Schluessel) is { NachweisPflicht: true }))
            {
                nachweis.Add(new($"qualifikation-nachweis:{p.Id:N}:{q.Schluessel}", $"{p.Anzeigename}: Nachweis für „{q.Bezeichnung}“ fehlt",
                    "Nachweis (Dokument oder Aktenzeichen) eintragen.", DateOnly.FromDateTime(q.ErfasstAm.LocalDateTime.Date).AddDays(14), $"personen/{p.Id}"));
            }
        }

        foreach (var kurs in await speicher.KurseMitTerminenZwischenAsync(heute.ToDateTime(TimeOnly.MinValue), heute.AddDays(tageVoraus).ToDateTime(TimeOnly.MinValue), abbruch))
        {
            if (kurs.BenoetigteDozierendenmerkmale.Count == 0)
            {
                continue;
            }

            var betroffen = await BetroffeneVeranstaltungenAsync(kurs, abbruch);
            foreach (var gruppe in betroffen.Where(b => !b.Value.Geeignet)
                         .Select(b => (V: kurs.Veranstaltungen.First(v => v.Id == b.Key), E: b.Value))
                         .GroupBy(x => x.V.DozentId!.Value))
            {
                var erster = gruppe.OrderBy(x => x.V.Beginn).First();
                einsatz.Add(new($"qualifikation-einsatz:{kurs.Id:N}:{gruppe.Key:N}",
                    $"{kurs.Kursnummer}: {gruppe.Count()} Veranstaltung(en) ab {erster.V.Datum:dd.MM.yyyy} mit {erster.V.DozentName} – {erster.E.Begruendung}",
                    erster.E.AusnahmeMoeglich ? "Nachweis eintragen, andere Person einplanen oder Ausnahme mit Begründung erteilen."
                        : "Nachweis eintragen oder eine andere, zugelassene Person einplanen (keine Ausnahme möglich).",
                    erster.V.Datum.AddDays(-14), $"kurse/{kurs.Id}", kurs.Fachbereich,
                    erster.V.Datum <= heute.AddDays(14) ? Dringlichkeit.Hoch : Dringlichkeit.Normal));
            }
        }

        // Abgelaufen, aber nicht erneuert: Die Aufgabe bleibt offen.
        async Task<bool> NochAbgelaufen(Aufgabe a)
        {
            var teile = a.Schluessel.Split(':');
            if (teile.Length != 4 || !DateOnly.TryParseExact(teile[3], "yyyyMMdd", out var bis) || bis >= heute
                || !Guid.TryParseExact(teile[1], "N", out var personId) || await personen.LadenAsync(personId, abbruch) is not { } person)
            {
                return false;
            }

            return !person.Qualifikationen.AlleAmStichtag(heute, jetzt).Any(q => q.Schluessel == teile[2]);
        }

        return await aufgaben.AbgleichenAsync(Aufgaben.Aufgabentypen.QualifikationAblauf, ablauf, NochAbgelaufen, _ => "Qualifikation erneuert oder geändert", abbruch)
               + await aufgaben.AbgleichenAsync(Aufgaben.Aufgabentypen.QualifikationNachweis, nachweis, grund: _ => "Nachweis eingetragen", abbruch: abbruch)
               + await aufgaben.AbgleichenAsync(Aufgaben.Aufgabentypen.QualifikationEinsatz, einsatz,
                   grund: _ => "Einsatz geklärt (Nachweis, andere Kursleitung, Ausnahme oder Termine vorbei)", abbruch: abbruch);
    }

    // ---------- Hilfen ----------

    private List<DateOnly> KuenftigeTage(Kurs kurs)
        => kurs.Veranstaltungen.Where(v => !v.Ausgefallen && v.Datum >= Heute).Select(v => v.Datum).Distinct().Order().ToList();

    private async Task<Benutzer> RolleAsync(Rolle[] rollen, CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !rollen.Contains(r)))
        {
            throw new InvalidOperationException(Oberflaeche.Freigabe.NurFuer(wer.Rolle, rollen).Begruendung);
        }

        return wer;
    }

    private async Task<Person> PersonAsync(Guid id, CancellationToken abbruch)
        => await personen.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
}
