using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Datenschutz;

public sealed record Loeschpruefung(Guid PersonId, Datenschutzstatus Status, IReadOnlyList<(Aufbewahrungsgrund Grund, Aufbewahrungsfrist? Frist, DateOnly? Fristende)> Gruende,
    Loeschentscheidung Entscheidung);

/// <param name="Antwortbrief">Druckstück des Antwortbriefs (mit Anschrift, nicht gespeichert), wenn keine E-Mail-Adresse vorlag.</param>
public sealed record LoeschverlangenErgebnis(Loeschentscheidung Entscheidung, bool AntwortEingereiht, byte[]? Antwortbrief = null);

/// <summary>
/// Datenschutz: Archivieren, Sperren (Art. 18), Anonymisieren, Löschverlangen (Art. 17), Löschläufe mit Vier-Augen-Freigabe,
/// Auskunft (Art. 15). Es werden nie Zeilen gelöscht, auf die andere Daten zeigen; Protokolle enthalten nur Pseudonyme.
/// </summary>
public sealed class DatenschutzDienst(
    IDatenschutzSpeicher speicher,
    IPersonenSpeicher personen,
    PostausgangDienst postausgang,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    IStammdaten stammdaten,
    TimeProvider uhr,
    Dokumente.DokumentDienst? dokumente = null)
{
    public static readonly Rolle[] FreigabeRollen = [Rolle.Verwaltungsleitung, Rolle.Leitung];

    /// <summary>Rollen, die gesperrte Personen sehen dürfen (Zugriff wird protokolliert).</summary>
    public static readonly Rolle[] SperrzugriffRollen = [Rolle.Buchhaltung, Rolle.IntegrationskursTeam];

    public async Task GrundeinstellungenAsync(CancellationToken abbruch = default)
    {
        if ((await speicher.FristenAsync(abbruch)).Count > 0)
        {
            return;
        }

        foreach (var f in Aufbewahrungsfrist.Vorschlaege())
        {
            speicher.Hinzufuegen(f);
        }

        await speicher.SpeichernAsync(abbruch);
    }

    public async Task<Loeschpruefung> PruefenAsync(Guid personId, CancellationToken abbruch = default)
    {
        var person = await PersonAsync(personId, abbruch);
        var fristen = (await speicher.FristenAsync(abbruch)).ToDictionary(f => f.Schluessel);
        var gruende = await speicher.GruendeAsync(personId, abbruch);
        var heute = Heute;
        return new Loeschpruefung(personId, person.Datenschutz,
            gruende.Select(g => (g, fristen.GetValueOrDefault(g.FristSchluessel), fristen.GetValueOrDefault(g.FristSchluessel)?.Fristende(g.Bezugsdatum))).ToList(),
            Loeschregel.Entscheiden(gruende, fristen, heute));
    }

    public async Task ArchivierenAsync(Guid personId, CancellationToken abbruch = default)
    {
        var person = await PersonAsync(personId, abbruch);
        person.Archivieren(uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Datenschutz.Archiviert", $"{Pseudonym.Person(personId)} archiviert", nameof(Person), personId, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    public async Task ReaktivierenAsync(Guid personId, CancellationToken abbruch = default)
    {
        var person = await PersonAsync(personId, abbruch);
        var vorher = person.Datenschutz;
        person.Reaktivieren();
        await protokoll.ProtokollierenAsync("Datenschutz.Reaktiviert", $"{Pseudonym.Person(personId)} reaktiviert (vorher {DatenschutzAnzeige.Text(vorher)})",
            nameof(Person), personId, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    /// <summary>Löschen nach Regel: Läuft eine Frist, wird gesperrt (und nicht benötigte Kontaktdaten gelöscht), sonst anonymisiert.</summary>
    public async Task<Loeschentscheidung> LoeschenAsync(Guid personId, string anlass, CancellationToken abbruch = default)
    {
        var pruefung = await PruefenAsync(personId, abbruch);
        await AnwendenAsync(await PersonAsync(personId, abbruch), pruefung.Entscheidung, anlass, abbruch);
        await speicher.SpeichernAsync(abbruch);
        return pruefung.Entscheidung;
    }

    /// <summary>Löschverlangen nach Art. 17: Antwortschreiben in den Postausgang, dann löschen bzw. sperren.</summary>
    public async Task<LoeschverlangenErgebnis> LoeschverlangenAsync(Guid personId, CancellationToken abbruch = default)
    {
        var pruefung = await PruefenAsync(personId, abbruch);
        var person = await PersonAsync(personId, abbruch);
        var einrichtung = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Ihre Volkshochschule";
        var antwort = false;
        byte[]? brief = null;
        var text = pruefung.Entscheidung.Aktion == Loeschaktion.Anonymisieren
                ? "wir haben Ihre personenbezogenen Daten wie gewünscht gelöscht. Übrig bleiben nur anonyme statistische Angaben (Altersgruppe, Gebiet)."
                : "wir haben Ihre Daten gelöscht, soweit keine gesetzliche Aufbewahrungspflicht besteht. Die übrigen Daten sind gesperrt und werden "
                  + $"nach Ablauf der Frist am {pruefung.Entscheidung.GesperrtBis:dd.MM.yyyy} gelöscht. Gründe:\n- "
                  + string.Join("\n- ", pruefung.Entscheidung.Gruende);
        if (person.EMail is { } mail)
        {
            // Ohne Personenbezug eingereiht, damit die Antwort nicht mit anonymisiert wird.
            await postausgang.KundenmailEinreihenAsync(mail, null, "Ihr Antrag auf Löschung Ihrer Daten (Art. 17 DSGVO)",
                $"Guten Tag,\n\n{text}\n\nMit freundlichen Grüßen\n{einrichtung}", string.IsNullOrEmpty(person.Sprache) ? "de" : person.Sprache, null, abbruch);
            antwort = true;
        }
        else if (dokumente is not null)
        {
            // Ohne E-Mail-Adresse: Antwortbrief, solange die Anschrift noch da ist (vor dem Anonymisieren).
            var jetzt = uhr.GetLocalNow();
            var stand = person.StandAm(DateOnly.FromDateTime(jetzt.Date), jetzt);
            var anschrift = new List<string> { stand.NameMitTitel };
            if (stand.BriefAn is { } a)
            {
                anschrift.AddRange(new[] { a.Zusatz, a.Strasse, $"{a.Postleitzahl} {a.Ort}" }.Where(z => !string.IsNullOrWhiteSpace(z)).Select(z => z!));
            }

            brief = (await dokumente.DatenschutzantwortAsync(anschrift, person.Sprache, "Ihr Antrag auf Löschung Ihrer Daten (Art. 17 DSGVO)",
                text.Split("\n", StringSplitOptions.RemoveEmptyEntries), abbruch)).Druck;
        }

        await AnwendenAsync(person, pruefung.Entscheidung, "Löschverlangen nach Art. 17 DSGVO", abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new LoeschverlangenErgebnis(pruefung.Entscheidung, antwort, brief);
    }

    /// <summary>Gesperrte Person ansehen: nur berechtigte Rollen, jeder Zugriff wird protokolliert.</summary>
    public async Task<bool> SperrzugriffAsync(Guid personId, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (wer.Rolle is not { } r || !SperrzugriffRollen.Contains(r))
        {
            return false;
        }

        await protokoll.ProtokollierenAsync("Datenschutz.Sperrzugriff", $"Zugriff auf gesperrte Daten von {Pseudonym.Person(personId)}", nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return true;
    }

    // ---------- Kriterien und Archiv ----------

    public Task<IReadOnlyList<PersonAktivitaet>> InaktivePersonenAsync(int monate, CancellationToken abbruch = default)
        => speicher.InaktivePersonenAsync(uhr.GetLocalNow().AddMonths(-monate), abbruch);

    public Task<IReadOnlyList<KursEnde>> KurseEndeVorAsync(DateOnly vor, CancellationToken abbruch = default) => speicher.KurseEndeVorAsync(vor, abbruch);

    public Task<IReadOnlyList<Domain.Programm.Semester>> SemesterAelterAlsAsync(int monate, CancellationToken abbruch = default)
        => speicher.SemesterEndeVorAsync(Heute.AddMonths(-monate), abbruch);

    public async Task<int> KurseArchivierenAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default)
    {
        var kurse = await speicher.KurseLadenAsync(ids, abbruch);
        foreach (var k in kurse)
        {
            k.Archivieren(uhr.GetLocalNow());
        }

        await protokoll.ProtokollierenAsync("Datenschutz.KurseArchiviert", $"{kurse.Count} Kurse archiviert: {string.Join(", ", kurse.Select(k => k.Kursnummer))}", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return kurse.Count;
    }

    public async Task KursWiederherstellenAsync(Guid id, CancellationToken abbruch = default)
    {
        var kurs = (await speicher.KurseLadenAsync([id], abbruch)).Single();
        kurs.Wiederherstellen();
        await protokoll.ProtokollierenAsync("Datenschutz.KursWiederhergestellt", $"Kurs {kurs.Kursnummer} aus dem Archiv geholt", "Kurs", id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task SemesterArchivierenAsync(Guid id, bool archivieren, CancellationToken abbruch = default)
    {
        var semester = await speicher.SemesterLadenAsync(id, abbruch) ?? throw new InvalidOperationException("Semester nicht gefunden.");
        var kurse = await speicher.KurseDesSemestersAsync(id, abbruch);
        foreach (var k in kurse)
        {
            if (archivieren)
            {
                k.Archivieren(uhr.GetLocalNow());
            }
            else
            {
                k.Wiederherstellen();
            }
        }

        if (archivieren)
        {
            semester.Archivieren(uhr.GetLocalNow());
        }
        else
        {
            semester.Wiederherstellen();
        }

        await protokoll.ProtokollierenAsync(archivieren ? "Datenschutz.SemesterArchiviert" : "Datenschutz.SemesterWiederhergestellt",
            $"Semester {semester.Name} mit {kurse.Count} Kursen {(archivieren ? "archiviert" : "wiederhergestellt")}", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task FristAendernAsync(string schluessel, int jahre, bool abJahresende, string rechtsgrundlage, string? hinweis, CancellationToken abbruch = default)
    {
        var frist = await speicher.FristLadenAsync(schluessel, abbruch) ?? throw new InvalidOperationException("Frist nicht gefunden.");
        frist.Aendern(jahre, abJahresende, rechtsgrundlage, hinweis);
        await protokoll.ProtokollierenAsync("Datenschutz.Frist", $"Aufbewahrungsfrist „{frist.Bezeichnung}“: {jahre} Jahre{(abJahresende ? " ab Jahresende" : "")} ({rechtsgrundlage})",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    // ---------- Löschläufe ----------

    /// <summary>Vorschau: inaktive Personen und abgelaufene Sperren; je Treffer wird entschieden, ob gesperrt oder anonymisiert würde.</summary>
    public async Task<Loeschlauf> LoeschlaufVorschauAsync(int inaktivMonate, CancellationToken abbruch = default)
    {
        var treffer = await InaktivePersonenAsync(inaktivMonate, abbruch);
        var fristen = (await speicher.FristenAsync(abbruch)).ToDictionary(f => f.Schluessel);
        int anonym = 0, sperren = 0;
        foreach (var t in treffer)
        {
            var e = Loeschregel.Entscheiden(await speicher.GruendeAsync(t.Id, abbruch), fristen, Heute);
            if (e.Aktion == Loeschaktion.Anonymisieren)
            {
                anonym++;
            }
            else if (t.Status != Datenschutzstatus.Gesperrt)
            {
                sperren++;
            }
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        var lauf = new Loeschlauf($"Personen ohne Aktivität seit {inaktivMonate} Monaten (vor {Heute.AddMonths(-inaktivMonate):dd.MM.yyyy}) und abgelaufene Sperren",
            treffer.Select(t => t.Id), anonym, sperren, uhr.GetLocalNow(), wer.Anzeige);
        speicher.Hinzufuegen(lauf);
        await protokoll.ProtokollierenAsync("Datenschutz.LoeschlaufVorschau",
            $"Löschlauf vorbereitet: {anonym} zu anonymisieren, {sperren} zu sperren – wartet auf zwei Freigaben", "Loeschlauf", lauf.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return lauf;
    }

    /// <summary>Freigabe (Vier-Augen-Prinzip). Mit der zweiten Freigabe wird der Lauf ausgeführt.</summary>
    public async Task<Loeschlauf> FreigebenAsync(Guid laufId, CancellationToken abbruch = default)
    {
        var lauf = await speicher.LoeschlaufLadenAsync(laufId, abbruch) ?? throw new InvalidOperationException("Löschlauf nicht gefunden.");
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var vollstaendig = lauf.Freigeben(wer.Kennung, wer.Anzeige, wer.Rolle is { } r && FreigabeRollen.Contains(r), uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Datenschutz.LoeschlaufFreigabe", $"Löschlauf freigegeben ({(vollstaendig ? "zweite" : "erste")} Freigabe)", "Loeschlauf", lauf.Id,
            abbruch: abbruch);
        if (vollstaendig)
        {
            await AusfuehrenAsync(lauf, abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return lauf;
    }

    public async Task VerwerfenAsync(Guid laufId, CancellationToken abbruch = default)
    {
        var lauf = await speicher.LoeschlaufLadenAsync(laufId, abbruch) ?? throw new InvalidOperationException("Löschlauf nicht gefunden.");
        lauf.Verwerfen();
        await protokoll.ProtokollierenAsync("Datenschutz.LoeschlaufVerworfen", "Löschlauf verworfen", "Loeschlauf", lauf.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    private async Task AusfuehrenAsync(Loeschlauf lauf, CancellationToken abbruch)
    {
        var fristen = (await speicher.FristenAsync(abbruch)).ToDictionary(f => f.Schluessel);
        var grenze = lauf.ErstelltAm;
        var noch = (await speicher.InaktivePersonenAsync(grenze, abbruch)).Select(p => p.Id).ToHashSet();
        int anonym = 0, gesperrt = 0, uebersprungen = 0;
        foreach (var id in lauf.PersonIds)
        {
            var person = await personen.LadenAsync(id, abbruch);
            if (person is null || person.IstAnonymisiert || !noch.Contains(id))
            {
                uebersprungen++; // inzwischen wieder aktiv oder schon gelöscht
                continue;
            }

            var e = Loeschregel.Entscheiden(await speicher.GruendeAsync(id, abbruch), fristen, Heute);
            if (e.Aktion == Loeschaktion.Sperren && person.Datenschutz == Datenschutzstatus.Gesperrt)
            {
                uebersprungen++;
                continue;
            }

            await AnwendenAsync(person, e, "Löschlauf", abbruch, protokollieren: false);
            _ = e.Aktion == Loeschaktion.Anonymisieren ? anonym++ : gesperrt++;
        }

        lauf.AlsAusgefuehrt(anonym, gesperrt, uebersprungen, uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Datenschutz.LoeschlaufAusgefuehrt",
            $"Löschlauf ausgeführt: {anonym} anonymisiert, {gesperrt} gesperrt, {uebersprungen} übersprungen", "Loeschlauf", lauf.Id, abbruch: abbruch);
    }

    private async Task AnwendenAsync(Person person, Loeschentscheidung e, string anlass, CancellationToken abbruch, bool protokollieren = true)
    {
        var jetzt = uhr.GetLocalNow();
        if (e.Aktion == Loeschaktion.Sperren)
        {
            person.KontaktdatenLoeschen(jetzt);
            person.Sperren(e.GesperrtBis!.Value, string.Join(" | ", e.Gruende), jetzt);
            if (protokollieren)
            {
                await protokoll.ProtokollierenAsync("Datenschutz.Gesperrt",
                    $"{Pseudonym.Person(person.Id)} gesperrt bis {e.GesperrtBis:dd.MM.yyyy} ({anlass}); nicht benötigte Kontaktdaten gelöscht",
                    nameof(Person), person.Id, abbruch: abbruch);
            }

            return;
        }

        // Kurse und Termine kopieren keine Namen mehr: der Platzhalter erscheint dort automatisch (Namen werden zur Laufzeit gelesen).
        var platzhalter = person.Anonymisieren(jetzt);

        foreach (var andere in await speicher.PersonenMitVollmachtFuerAsync(person.Id, abbruch))
        {
            andere.BevollmaechtigtenAnonymisieren(person.Id, platzhalter);
        }

        foreach (var n in await speicher.NachrichtenAsync(person.Id, abbruch))
        {
            n.Anonymisieren(platzhalter);
        }

        // Belegungen bleiben (Statistik, Buchhaltung), nur personenbezogene Texte werden ersetzt.
        foreach (var b in await speicher.BelegungenAsync(person.Id, abbruch))
        {
            if (b.PersonId == person.Id)
            {
                b.PersonendatenEntfernen();
            }

            b.BevollmaechtigtenUmbenennen(person.Id, platzhalter);
        }

        foreach (var erm in await speicher.ErmaessigungenAsync(person.Id, abbruch))
        {
            erm.Anonymisieren();
        }

        // Check-in-Zugänge (Link, Geräte, Ausweise) werden entwertet.
        foreach (var token in await speicher.ZugangstokensAsync(person.Id, abbruch))
        {
            token.Widerrufen(jetzt, "Anonymisierung");
        }

        // Dokumente: Inhalt (PDF, Datenstand, Text) wird gelöscht; Art, Datum und Prüfsumme bleiben als Nachweis.
        foreach (var dok in await speicher.DokumenteAsync(person.Id, abbruch))
        {
            if (!dok.IstAnonymisiert)
            {
                dok.Anonymisieren(jetzt);
            }
        }

        // Aufgaben nennen die Person im Titel: Text ersetzen, offene Aufgaben schließen.
        foreach (var aufgabe in await speicher.AufgabenZuPersonAsync(person.Id, abbruch))
        {
            aufgabe.Anonymisieren(platzhalter, jetzt);
        }

        // Finanzdaten: erst nach Ablauf aller Fristen erreichbar (sonst wird gesperrt). Nummer, Betrag und Prüfsumme bleiben.
        var finanz = await speicher.FinanzdatenAsync(person.Id, abbruch);
        foreach (var b in finanz.Belege)
        {
            b.Anonymisieren(platzhalter, jetzt);
        }

        foreach (var m in finanz.Mandate)
        {
            m.Anonymisieren(platzhalter);
        }

        finanz.Konto?.Anonymisieren(platzhalter);

        // Integrationskurs: Lichtbilder löschen, Aktenzeichen und Bemerkungen entfernen.
        var ik = await speicher.IntegrationsdatenAsync(person.Id, abbruch);
        foreach (var b in ik.Berechtigungen)
        {
            b.Anonymisieren();
        }

        foreach (var p in ik.Pruefungen)
        {
            p.Anonymisieren();
        }

        foreach (var l in ik.Lichtbilder)
        {
            l.Loeschen(jetzt);
        }
        foreach (var u in finanz.Umsaetze)
        {
            u.Anonymisieren();
        }

        if (protokollieren)
        {
            await protokoll.ProtokollierenAsync("Datenschutz.Anonymisiert", $"{Pseudonym.Person(person.Id)} anonymisiert ({anlass})", nameof(Person), person.Id,
                abbruch: abbruch);
        }
    }

    // ---------- Auskunft ----------

    public async Task<Auskunft> AuskunftAsync(Guid personId, CancellationToken abbruch = default)
    {
        var person = await PersonAsync(personId, abbruch);
        var jetzt = uhr.GetLocalNow();
        var stand = person.StandAm(Heute, jetzt);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var pruefung = await PruefenAsync(personId, abbruch);
        var kurse = await speicher.KurseMitDozentAsync(personId, abbruch);
        var nachrichten = await speicher.NachrichtenAsync(personId, abbruch);
        var verlauf = await speicher.ProtokollZuAsync(personId, abbruch);
        var belegungen = (await speicher.BelegungenAsync(personId, abbruch)).Where(b => b.PersonId == personId).ToList();
        var titel = await speicher.KurstitelAsync(belegungen.Select(b => b.KursId).Distinct(), abbruch);
        var ermaessigungen = await speicher.ErmaessigungenAsync(personId, abbruch);
        var dokumente = await speicher.DokumenteAsync(personId, abbruch);

        var auskunft = new Auskunft(
            (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Volkshochschule",
            jetzt,
            wer.Anzeige,
            personId,
            stand.NameMitTitel,
            DatenschutzAnzeige.Text(person.Datenschutz),
            new AuskunftStammdaten(
                stand.Titel,
                stand.Geburtsdatum?.ToString("dd.MM.yyyy"),
                PersonMerkmal.RollenAnzeige(stand.Rollen),
                stand.Sprache,
                stand.Adressen.Select(a => a.Anzeige + (a.AdressId == stand.AufenthaltsortId ? " (üblicher Aufenthaltsort)" : "")
                                           + (a.AdressId == stand.KorrespondenzadresseId ? " (Korrespondenz)" : "")).ToList(),
                stand.Rufnummern.Select(r => r.Wert).ToList(),
                stand.EMails.Select(m => m.Wert).ToList(),
                stand.Organisationen.Select(o => o.Wert).ToList(),
                stand.Vollmachten.Select(v => v.Wert).ToList(),
                stand.Einwilligungen.Select(w => $"{w.Feld}: {w.Wert}").ToList()),
            Datenhistorie.Erstellen(person),
            kurse.Select(k => new AuskunftKurs(k.Kursnummer, k.Titel, k.Veranstaltungen.Count(v => v.DozentId == personId),
                k.Veranstaltungen.Count == 0 ? null : $"{k.Veranstaltungen.Min(v => v.Beginn):dd.MM.yyyy}–{k.Veranstaltungen.Max(v => v.Beginn):dd.MM.yyyy}")).ToList(),
            belegungen.OrderBy(b => b.GebuchtAm).Select(b => new AuskunftBelegung(titel.GetValueOrDefault(b.KursId) ?? b.Kursnummer,
                Domain.Anmeldung.BelegungAnzeige.Status(b.Status), b.GebuchtAm,
                b.GebuchtVon + (b.BevollmaechtigterName is null ? "" : $" im Auftrag von {b.BevollmaechtigterName}"),
                Domain.Anmeldung.BelegungAnzeige.Weg(b.Weg), b.Entgelt + (b.Ermaessigt ? " (ermäßigt)" : ""),
                b.IstKontingent ? Anmeldung.AnmeldungDienst.Text(b.Kontingent) : null)).ToList(),
            ermaessigungen.Select(e => $"{e.Art}, gültig {e.GueltigVon:dd.MM.yyyy}–{e.GueltigBis:dd.MM.yyyy}, Nachweis: {e.Nachweis}").ToList(),
            nachrichten.OrderBy(n => n.ErstelltAm).Select(n => new AuskunftNachricht(n.ErstelltAm, n.Kanal.ToString(), n.Betreff, n.Status.ToString())).ToList(),
            [
                "Mitarbeitende der Volkshochschule im Rahmen ihrer Aufgaben",
                .. nachrichten.Select(n => n.Kanal == Kanal.EMail ? "E-Mail-Versand (Auftragsverarbeitung)" : $"Schnittstelle: {n.Empfaenger}").Distinct(),
            ],
            pruefung.Gruende.Select(g => new AuskunftFrist(g.Frist?.Bezeichnung ?? g.Grund.FristSchluessel, g.Grund.Beschreibung,
                g.Fristende?.ToString("dd.MM.yyyy") ?? "–", g.Frist?.Rechtsgrundlage ?? "–")).ToList(),
            verlauf.OrderBy(v => v.Zeitpunkt).Select(v => new AuskunftProtokoll(v.Zeitpunkt, v.Aktion, v.Benutzer)).ToList(),
            dokumente.Count == 0
                ? ["Es sind keine Dokumente zu dieser Person gespeichert."]
                : dokumente.OrderBy(d => d.ErstelltAm).Select(d => $"{d.Titel}, erstellt am {d.ErstelltAm.ToLocalTime():dd.MM.yyyy} von {d.ErstelltVon}"
                                                                    + (d.IstAnonymisiert ? " (Inhalt gelöscht)" : $" · SHA-256 {d.Sha256}")).ToList(),
            [
                "Ohne Aufbewahrungspflicht werden Daten nach drei Jahren ohne Aktivität anonymisiert (Vorschlag, mit dem Datenschutzbeauftragten abgestimmt).",
                "Sie können Berichtigung (Art. 16), Löschung (Art. 17), Einschränkung (Art. 18) und Widerspruch (Art. 21) verlangen.",
            ]);

        await protokoll.ProtokollierenAsync("Datenschutz.Auskunft", $"Auskunft nach Art. 15 für {Pseudonym.Person(personId)} erstellt", nameof(Person), personId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return auskunft;
    }

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    private async Task<Person> PersonAsync(Guid id, CancellationToken abbruch)
        => await personen.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
}
