using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

/// <summary>Eingaben des Schnellwegs "regelmäßig, gleicher Ort, gleiche Dozentin".</summary>
public sealed record KursEntwurf(
    string Titel,
    Guid SemesterId,
    int Fachbereich,
    Guid KursartId,
    Serienregel Serie,
    IReadOnlyList<KursDozent> Team,
    int MinTeilnehmende,
    int MaxTeilnehmende,
    Geld Entgelt,
    Geld? Ermaessigt,
    Sichtbarkeit? Sichtbarkeit = null,
    Guid? AuftraggeberId = null,
    string? AuftraggeberName = null,
    string? Beschreibung = null,
    string? Kursnummer = null,
    string? Ausnahmebegruendung = null);

/// <summary>Vorschau vor dem Speichern: Termine (mit ausgelassenen), Ortseignung und Konflikte.</summary>
public sealed record KursVorschau(
    IReadOnlyList<Serientermin> Termine,
    IReadOnlyList<Ortseignung> Orte,
    IReadOnlyList<string> Konflikte,
    string Kursnummer,
    Dozenteneignung? Dozent = null)
{
    public bool Speicherbar => Orte.All(o => o.Geeignet) && Termine.Any(t => t.Findet);
}

/// <summary>Anwendungsfälle der Programmplanung. Jede Änderung wird protokolliert.</summary>
public sealed class ProgrammDienst(IProgrammSpeicher speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr,
    IDozenteneinsatz einsatz)
{
    public const int HorizontWochen = 12;

    private DateOnly Horizont => DateOnly.FromDateTime(uhr.GetLocalNow().Date).AddDays(HorizontWochen * 7);

    public async Task<KursVorschau> VorschauAsync(KursEntwurf entwurf, CancellationToken abbruch = default)
    {
        var (kursart, _) = await GrundlagenAsync(entwurf, abbruch);
        var kalender = await speicher.KalenderAsync(abbruch);
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var termine = Seriengenerator.Erzeugen(entwurf.Serie, kalender, entwurf.Serie.Unbefristet ? Horizont : null);
        var tage = termine.Where(t => t.Findet).Select(t => t.Datum).ToList();
        var jetzt = uhr.GetLocalNow();
        var orte = entwurf.Serie.OrtIds.Select(id => verzeichnis.Finden(id))
            .OfType<Ort>()
            .Select(o => o.Art == Ortsart.VirtuellerRaum
                ? Ortsplanung.Pruefen(verzeichnis, o, Nutzungsart.Online, [], tage, jetzt)
                : Ortsplanung.Pruefen(verzeichnis, o, kursart.Nutzungsart, kursart.BenoetigteMerkmale, tage, jetzt))
            .ToList();

        var konflikte = new List<string>();
        if (termine.Any(t => t.Findet))
        {
            var bestehende = await speicher.TermineAsync(termine.Where(t => t.Findet).Min(t => t.Beginn), termine.Max(t => t.Ende), abbruch);
            var neueId = Guid.NewGuid();
            var neu = termine.Where(t => t.Findet).Select(t => new Termin(Guid.NewGuid(), neueId, entwurf.Titel, t.Beginn, t.Ende,
                entwurf.Serie.OrtIds, entwurf.Serie.DozentId ?? entwurf.Team.FirstOrDefault()?.PersonId, false));
            konflikte = Konfliktpruefung.Finden(bestehende.Select(b => b.Termin).Concat(neu), id => verzeichnis.Finden(id)?.Exklusiv ?? false)
                .Where(k => k.A.KursId == neueId || k.B.KursId == neueId)
                .Select(k => k.Beschreibung(id => verzeichnis.Finden(id)?.Anzeige ?? "?"))
                .ToList();
        }

        Dozenteneignung? dozent = null;
        if ((entwurf.Serie.DozentId ?? entwurf.Team.FirstOrDefault()?.PersonId) is { } dozentId)
        {
            dozent = await einsatz.PruefenAsync(dozentId, kursart.BenoetigteDozierendenmerkmale, tage.Count > 0 ? tage : [DateOnly.FromDateTime(jetzt.Date)], null, abbruch);
        }

        return new KursVorschau(termine, orte, konflikte, entwurf.Kursnummer ?? await NaechsteNummerAsync(entwurf, abbruch), dozent);
    }

    /// <summary>Wirft mit Begründung, wenn die Person an einem der Tage nicht eingesetzt werden darf (Ausnahmen des Kurses zählen).</summary>
    private async Task EinsatzPruefenAsync(Kurs kurs, Guid? dozentId, IEnumerable<DateOnly> tage, CancellationToken abbruch)
    {
        var liste = tage.ToList();
        if (dozentId is not { } id || kurs.BenoetigteDozierendenmerkmale.Count == 0 || liste.Count == 0)
        {
            return;
        }

        var eignung = await einsatz.PruefenAsync(id, kurs.BenoetigteDozierendenmerkmale, liste, kurs.Id, abbruch);
        if (!eignung.Geeignet)
        {
            throw new InvalidOperationException(eignung.Begruendung);
        }
    }

    /// <summary>Tage, an denen eine Serie stattfindet (für die Dozierendenprüfung in der Auswahl).</summary>
    public async Task<IReadOnlyList<DateOnly>> SerientageAsync(Serienregel serie, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(serie);
        if (serie.Wochentage.Count == 0 || serie.DauerMinuten < 5)
        {
            return [];
        }

        var kalender = await speicher.KalenderAsync(abbruch);
        return Seriengenerator.Erzeugen(serie, kalender, serie.Unbefristet ? Horizont : null).Where(t => t.Findet).Select(t => t.Datum).ToList();
    }

    /// <summary>Eignung aller buchbaren Orte für Kursart und Termine der Serie (für die Ortsauswahl mit Begründung).</summary>
    public async Task<IReadOnlyList<Ortseignung>> OrtsauswahlAsync(Guid kursartId, Serienregel? serie, CancellationToken abbruch = default)
    {
        var kursart = (await speicher.KursartenAsync(abbruch)).First(k => k.Id == kursartId);
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var tage = new List<DateOnly>();
        if (serie is { Wochentage.Count: > 0, DauerMinuten: >= 5 })
        {
            var kalender = await speicher.KalenderAsync(abbruch);
            tage = Seriengenerator.Erzeugen(serie, kalender, serie.Unbefristet ? Horizont : null).Where(t => t.Findet).Select(t => t.Datum).ToList();
        }

        var jetzt = uhr.GetLocalNow();
        return verzeichnis.Alle.Where(o => o.Buchbar && o.Aktiv)
            .Select(o => Ortsplanung.Pruefen(verzeichnis, o, o.Art == Ortsart.VirtuellerRaum ? Nutzungsart.Online : kursart.Nutzungsart,
                o.Art == Ortsart.VirtuellerRaum ? [] : kursart.BenoetigteMerkmale, tage.Count > 0 ? tage : [DateOnly.FromDateTime(jetzt.Date)], jetzt))
            .OrderByDescending(e => e.Geeignet)
            .ThenBy(e => verzeichnis.PfadAnzeige(e.Ort), StringComparer.Ordinal)
            .ToList();
    }

    public async Task<Kurs> AnlegenAsync(KursEntwurf entwurf, CancellationToken abbruch = default)
    {
        var vorschau = await VorschauAsync(entwurf, abbruch);
        if (!vorschau.Speicherbar)
        {
            throw new InvalidOperationException(vorschau.Orte.FirstOrDefault(o => !o.Geeignet)?.Begruendung ?? "Die Serie ergibt keine Termine.");
        }

        var (kursart, semester) = await GrundlagenAsync(entwurf, abbruch);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var ausnahmeNoetig = false;
        if (vorschau.Dozent is { Geeignet: false } d)
        {
            if (string.IsNullOrWhiteSpace(entwurf.Ausnahmebegruendung) || !d.AusnahmeMoeglich)
            {
                throw new InvalidOperationException(d.Begruendung);
            }

            if (wer.Rolle is not (Rolle.Fachbereichsleitung or Rolle.Leitung))
            {
                throw new InvalidOperationException("Ausnahmen erteilt nur die Fachbereichsleitung.");
            }

            ausnahmeNoetig = true;
        }

        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var kurs = new Kurs(vorschau.Kursnummer, entwurf.Titel, semester, entwurf.Fachbereich, kursart, entwurf.MinTeilnehmende, entwurf.MaxTeilnehmende)
            .MitTeam(entwurf.Team)
            .MitBeschreibung(entwurf.Beschreibung);
        if (entwurf.Sichtbarkeit is { } s)
        {
            kurs.MitSichtbarkeit(s, entwurf.AuftraggeberId, entwurf.AuftraggeberName);
        }

        kurs.EntgeltSetzenAb(entwurf.Entgelt, entwurf.Ermaessigt, semester.Beginn, Erfassung.Erst(uhr.GetLocalNow(), wer.Anzeige));
        kurs.SerieFestlegen(entwurf.Serie, await speicher.KalenderAsync(abbruch), id => verzeichnis.Finden(id)?.Anzeige ?? "?", Horizont);
        speicher.Hinzufuegen(kurs);
        await protokoll.ProtokollierenAsync("Kurs.Angelegt",
            $"Kurs {kurs.Kursnummer} „{kurs.Titel}“ angelegt: {kurs.Veranstaltungen.Count} Veranstaltungen{(vorschau.Konflikte.Count > 0 ? $", {vorschau.Konflikte.Count} Konflikte" : "")}",
            nameof(Kurs), kurs.Id, erfolgreich: vorschau.Konflikte.Count == 0, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        if (ausnahmeNoetig && vorschau.Dozent is { } dz)
        {
            await einsatz.AusnahmeErteilenAsync(kurs.Id, dz.PersonId, dz.Tage.SelectMany(t => t.Fehlend), entwurf.Ausnahmebegruendung!, abbruch);
        }

        return kurs;
    }

    public async Task SerieAendernAbAsync(Guid kursId, DateOnly ab, Serienregel neueRegel, CancellationToken abbruch = default)
    {
        var kurs = await KursAsync(kursId, abbruch);
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var kalender = await speicher.KalenderAsync(abbruch);
        var neueTage = Seriengenerator.Erzeugen(neueRegel with { Start = ab }, kalender, neueRegel.Unbefristet ? Horizont : null).Where(t => t.Findet).Select(t => t.Datum);
        await EinsatzPruefenAsync(kurs, neueRegel.DozentId ?? kurs.Leitung?.PersonId, neueTage, abbruch);
        kurs.SerieAendernAb(ab, neueRegel, kalender, id => verzeichnis.Finden(id)?.Anzeige ?? "?", Horizont);
        await protokoll.ProtokollierenAsync("Kurs.SerieGeaendert",
            $"{kurs.Kursnummer}: alle Termine ab {ab:dd.MM.yyyy} geändert ({string.Join("/", neueRegel.Wochentage.Select(KursAnzeige.Wochentag))} {neueRegel.Beginn:HH\\:mm}, {neueRegel.DauerMinuten} Min.); einzeln geänderte Termine bleiben",
            nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task NurDieseAendernAsync(Guid kursId, Guid veranstaltungId, DateTime beginn, DateTime ende, IReadOnlyList<Guid> ortIds,
        string? bemerkung, CancellationToken abbruch = default, Guid? dozentId = null)
    {
        var kurs = await KursAsync(kursId, abbruch);
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var v = kurs.Veranstaltungen.Single(x => x.Id == veranstaltungId);
        var gewuenscht = dozentId ?? v.DozentId;
        var dozent = gewuenscht is { } d ? kurs.Team.FirstOrDefault(t => t.PersonId == d) : null;
        if (dozentId is not null && dozent is null)
        {
            throw new InvalidOperationException("Die Person gehört nicht zum Dozierendenteam des Kurses. Bitte zuerst ins Team aufnehmen.");
        }

        await EinsatzPruefenAsync(kurs, dozent?.PersonId, [DateOnly.FromDateTime(beginn)], abbruch);
        v.NurDieseAendern(beginn, ende, ortIds.Select(id => new VeranstaltungsOrt(id, verzeichnis.Finden(id)?.Anzeige ?? "?")), dozent, bemerkung);
        await protokoll.ProtokollierenAsync("Kurs.TerminGeaendert",
            $"{kurs.Kursnummer}: Termin {v.Nummer} geändert auf {beginn:dd.MM.yyyy HH:mm}–{ende:HH:mm} ({string.Join(", ", v.Orte.Select(o => o.OrtName))})",
            nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task AusfallenLassenAsync(Guid kursId, Guid veranstaltungId, string grund, CancellationToken abbruch = default)
    {
        var kurs = await KursAsync(kursId, abbruch);
        var v = kurs.Veranstaltungen.Single(x => x.Id == veranstaltungId);
        v.Ausfallen(grund);
        await protokoll.ProtokollierenAsync("Kurs.TerminFaelltAus", $"{kurs.Kursnummer}: Termin {v.Nummer} am {v.Beginn:dd.MM.yyyy} fällt aus. Grund: {grund}",
            nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task EinzelveranstaltungAsync(Guid kursId, DateTime beginn, DateTime ende, IReadOnlyList<Guid> ortIds, CancellationToken abbruch = default,
        Guid? dozentId = null)
    {
        var kurs = await KursAsync(kursId, abbruch);
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var dozent = dozentId is { } d ? kurs.Team.FirstOrDefault(t => t.PersonId == d) : kurs.Leitung;
        await EinsatzPruefenAsync(kurs, dozent?.PersonId, [DateOnly.FromDateTime(beginn)], abbruch);
        var v = kurs.EinzelveranstaltungHinzufuegen(beginn, ende, ortIds.Select(id => new VeranstaltungsOrt(id, verzeichnis.Finden(id)?.Anzeige ?? "?")), dozent);
        speicher.Hinzufuegen(v);
        await protokoll.ProtokollierenAsync("Kurs.Einzeltermin", $"{kurs.Kursnummer}: Einzelveranstaltung am {beginn:dd.MM.yyyy HH:mm} hinzugefügt",
            nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task StatusSetzenAsync(Guid kursId, Kursstatus status, CancellationToken abbruch = default)
    {
        var kurs = await KursAsync(kursId, abbruch);
        kurs.StatusSetzen(status);
        await protokoll.ProtokollierenAsync("Kurs.Status", $"{kurs.Kursnummer}: Status {KursAnzeige.Status(status)}", nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Kopiert alle Kurse eines Semesters (ohne abgesagte) in ein anderes, Termine um ganze Wochen verschoben.</summary>
    public async Task<int> SemesterKopierenAsync(Guid vonId, Guid nachId, CancellationToken abbruch = default)
    {
        var semester = await speicher.SemesterAsync(abbruch);
        var von = semester.Single(s => s.Id == vonId);
        var nach = semester.Single(s => s.Id == nachId);
        var tage = Kurs.Verschiebung(von, nach);
        var kalender = await speicher.KalenderAsync(abbruch);
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var anzahl = 0;
        foreach (var kurs in await speicher.KurseMitTerminenAsync(vonId, abbruch))
        {
            if (kurs.Status == Kursstatus.Abgesagt)
            {
                continue;
            }

            var nummer = Kurs.NummerFuer(kurs.Kursnummer, nach);
            if (await speicher.KursnummerVergebenAsync(nummer, abbruch))
            {
                continue;
            }

            var kopie = kurs.KopieFuer(nach, nummer, tage, kalender, id => verzeichnis.Finden(id)?.Anzeige ?? "?",
                nach.Ende, Erfassung.Erst(uhr.GetLocalNow(), wer.Anzeige, $"Semesterkopie aus {von.Name}"));
            speicher.Hinzufuegen(kopie);
            anzahl++;
        }

        await protokoll.ProtokollierenAsync("Programm.Semesterkopie",
            $"Semesterkopie {von.Name} → {nach.Name}: {anzahl} Kurse als Entwurf angelegt, Termine um {tage / 7} Wochen verschoben",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return anzahl;
    }

    public async Task MerkmalSetzenAsync(Guid ortId, string schluessel, string? wert, string? nachweis, DateOnly ab, DateOnly? bis, CancellationToken abbruch = default)
    {
        var ort = await speicher.OrtLadenAsync(ortId, abbruch) ?? throw new InvalidOperationException("Ort nicht gefunden.");
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var merkmal = verzeichnis.Katalog.GetValueOrDefault(schluessel) ?? throw new InvalidOperationException("Merkmal nicht im Katalog.");
        var wer = await benutzer.ErmittelnAsync(abbruch);
        ort.MerkmalSetzenAb(merkmal, wert, nachweis, ab, bis, Erfassung.Aenderung(uhr.GetLocalNow(), nachweis ?? "Merkmal eingetragen", wer.Anzeige));
        await protokoll.ProtokollierenAsync("Ort.Merkmal",
            $"{ort.Anzeige}: „{merkmal.Name}“ ab {ab:dd.MM.yyyy}{(bis is { } b ? $" bis {b:dd.MM.yyyy}" : "")}{(nachweis is null ? "" : $", Nachweis: {nachweis}")}",
            nameof(Ort), ortId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task MerkmalKatalogErgaenzenAsync(Merkmal merkmal, CancellationToken abbruch = default)
    {
        speicher.Hinzufuegen(merkmal);
        await protokoll.ProtokollierenAsync("Ort.Merkmalskatalog", $"Merkmal „{merkmal.Name}“ ({merkmal.Gruppe}) in den Katalog aufgenommen", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task FerienHinzufuegenAsync(Ferienzeitraum ferien, CancellationToken abbruch = default)
    {
        speicher.Hinzufuegen(ferien);
        await protokoll.ProtokollierenAsync("Programm.Ferien", $"Ferien „{ferien.Name}“ {ferien.Von:dd.MM.yyyy}–{ferien.Bis:dd.MM.yyyy} eingetragen", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Automatik: unbefristete Kurse bis zum Horizont (12 Wochen) fortschreiben.</summary>
    public async Task<int> UnbefristeteFortschreibenAsync(CancellationToken abbruch = default)
    {
        var kalender = await speicher.KalenderAsync(abbruch);
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var neu = 0;
        foreach (var kurs in await speicher.UnbefristeteKurseAsync(abbruch))
        {
            var vorher = kurs.Veranstaltungen.Select(v => v.Id).ToHashSet();
            var anzahl = kurs.Fortschreiben(kalender, id => verzeichnis.Finden(id)?.Anzeige ?? "?", Horizont);
            foreach (var v in kurs.Veranstaltungen.Where(v => !vorher.Contains(v.Id)))
            {
                speicher.Hinzufuegen(v);
            }

            if (anzahl > 0)
            {
                neu += anzahl;
                await protokoll.ProtokollierenAsync("Kurs.Fortgeschrieben", $"{kurs.Kursnummer}: {anzahl} weitere Termine bis {Horizont:dd.MM.yyyy} erzeugt",
                    nameof(Kurs), kurs.Id, abbruch: abbruch);
            }
        }

        await speicher.SpeichernAsync(abbruch);
        return neu;
    }

    /// <summary>Automatik: Aufgabe anlegen, wenn eine befristete Zulassung (Merkmal mit Ende) in den nächsten 60 Tagen abläuft.</summary>
    public async Task<int> AblaufendeMerkmalePruefenAsync(int tageVorlauf = 60, CancellationToken abbruch = default)
    {
        var verzeichnis = await speicher.OrtsverzeichnisAsync(abbruch);
        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var angelegt = 0;
        foreach (var ort in verzeichnis.Alle)
        {
            foreach (var eintrag in ort.Merkmale.AlleAmStichtag(heute, jetzt).Where(m => m.GueltigBis is { } bis && bis.AddDays(-1) <= heute.AddDays(tageVorlauf)))
            {
                var bis = eintrag.GueltigBis!.Value.AddDays(-1);
                var schluessel = $"merkmal-ablauf:{ort.Id:N}:{eintrag.Schluessel}:{bis:yyyyMMdd}";
                if (await speicher.AufgabeExistiertAsync(schluessel, abbruch))
                {
                    continue;
                }

                var name = verzeichnis.Katalog.GetValueOrDefault(eintrag.Schluessel)?.Name ?? eintrag.Schluessel;
                var gruppe = eintrag.Schluessel == "bamf" ? nameof(Rolle.IntegrationskursTeam) : nameof(Rolle.Verwaltungsleitung);
                speicher.Hinzufuegen(new Aufgabe(schluessel, $"{ort.Anzeige}: „{name}“ läuft am {bis:dd.MM.yyyy} ab",
                    "Zulassung verlängern oder den Ort für betroffene Kurse ersetzen.", bis.AddDays(-14), gruppe, $"orte?id={ort.Id}", jetzt));
                await protokoll.ProtokollierenAsync("Ort.MerkmalLaeuftAb", $"Aufgabe angelegt: {ort.Anzeige} – „{name}“ läuft am {bis:dd.MM.yyyy} ab",
                    nameof(Ort), ort.Id, erfolgreich: false, abbruch: abbruch);
                angelegt++;
            }
        }

        await speicher.SpeichernAsync(abbruch);
        return angelegt;
    }

    private async Task<(Kursart, Semester)> GrundlagenAsync(KursEntwurf entwurf, CancellationToken abbruch)
    {
        var kursart = (await speicher.KursartenAsync(abbruch)).FirstOrDefault(k => k.Id == entwurf.KursartId)
            ?? throw new InvalidOperationException("Kursart nicht gefunden.");
        var semester = (await speicher.SemesterAsync(abbruch)).FirstOrDefault(s => s.Id == entwurf.SemesterId)
            ?? throw new InvalidOperationException("Semester nicht gefunden.");
        return (kursart, semester);
    }

    private async Task<string> NaechsteNummerAsync(KursEntwurf entwurf, CancellationToken abbruch)
    {
        var semester = (await speicher.SemesterAsync(abbruch)).First(s => s.Id == entwurf.SemesterId);
        for (var n = (entwurf.Fachbereich * 100) + 1; n < (entwurf.Fachbereich * 100) + 99; n++)
        {
            var nummer = $"{semester.Kuerzel}-{n}";
            if (!await speicher.KursnummerVergebenAsync(nummer, abbruch))
            {
                return nummer;
            }
        }

        return $"{semester.Kuerzel}-{entwurf.Fachbereich}{Ids.Neu().ToString()[..4]}";
    }

    private async Task<Kurs> KursAsync(Guid id, CancellationToken abbruch)
    {
        var kurs = await speicher.KursLadenAsync(id, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        return kurs.Archiviert ? throw new InvalidOperationException("Der Kurs ist archiviert und schreibgeschützt. Bitte zuerst wiederherstellen.") : kurs;
    }
}
