using System.Globalization;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Vorlagen;

namespace VibeVHS.Application.Anmeldung;

/// <summary>Auftrag des Tresen-Assistenten (oder der Online-Anmeldung mit <see cref="Weg"/> = Online).</summary>
public sealed record AnmeldeAuftrag(Guid PersonId, Guid KursId, Zahlart Zahlart, Guid? BevollmaechtigterId = null, bool Ueberbuchen = false,
    Buchungsweg? Weg = null);

public sealed record AnmeldeErgebnis(Belegung Belegung, Buchungsergebnis Pruefung, Guid? NachrichtId);

/// <summary>Kurs in der Auswahl mit dem Ergebnis der Buchungsprüfung für eine bestimmte Person.</summary>
public sealed record KursOption(Kurs Kurs, Buchungsergebnis Pruefung, int Belegt, int Wartend, Geld? Entgelt, Geld? Ermaessigt);

/// <summary>Belegung mit aktuellem Namen der Person (für die Teilnehmerliste eines Kurses).</summary>
public sealed record TeilnehmerZeile(Belegung Belegung, string Name, int? Warteplatz);

/// <summary>Reservierte Plätze und Wartende je Veranstaltung und Ort.</summary>
public sealed record Einbuchungslage(IReadOnlyDictionary<(Guid Veranstaltung, Guid? Ort), int> Eingebucht, IReadOnlyDictionary<Guid, int> Wartend)
{
    public int Belegt(Guid veranstaltungId, Guid ortId) => Eingebucht.GetValueOrDefault((veranstaltungId, ortId));

    public static Einbuchungslage Berechnen(IEnumerable<Belegung> belegungen)
    {
        var eingebucht = new Dictionary<(Guid, Guid?), int>();
        var wartend = new Dictionary<Guid, int>();
        foreach (var b in belegungen.Where(b => b.Status == Belegungsstatus.Angemeldet))
        {
            foreach (var (vid, ort) in b.Einbuchungen)
            {
                eingebucht[(vid, ort)] = eingebucht.GetValueOrDefault((vid, ort)) + 1;
            }

            foreach (var vid in b.WartetAuf.Keys)
            {
                wartend[vid] = wartend.GetValueOrDefault(vid) + 1;
            }
        }

        return new Einbuchungslage(eingebucht, wartend);
    }
}

/// <summary>
/// Anmeldung, Warteliste, Storno, Umbuchung und Vorab-Einbuchung. Jede Änderung wird protokolliert (nur mit Pseudonymen),
/// Bestätigungen gehen mit Übersetzungslink in den Postausgang.
/// </summary>
public sealed class AnmeldungDienst(
    IAnmeldungSpeicher speicher,
    IProgrammSpeicher programm,
    IPersonenSpeicher personen,
    IVorlagenSpeicher vorlagen,
    IStammdaten stammdaten,
    PostausgangDienst post,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr,
    Durchfuehrung.CheckinZugang zugang,
    Dokumente.IQrCode qr)
{
    /// <summary>Rollen, die am Tresen buchen, stornieren und umbuchen dürfen.</summary>
    public static readonly Rolle[] BuchungsRollen = [Rolle.Teilnehmerservice, Rolle.IntegrationskursTeam, Rolle.Verwaltungsleitung, Rolle.Leitung];

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static Buchungsweg WegFuer(Rolle? rolle) => rolle == Rolle.IntegrationskursTeam ? Buchungsweg.IntegrationskursTeam : Buchungsweg.Tresen;

    // ---------- Auswahl und Prüfung ----------

    /// <summary>Kurse der Semester mit Buchungsprüfung für die Person (Schritt 2 des Tresen-Assistenten).</summary>
    public async Task<IReadOnlyList<KursOption>> KursauswahlAsync(Guid personId, IEnumerable<Guid> semesterIds, Guid? bevollmaechtigterId = null,
        Buchungsweg? weg = null, CancellationToken abbruch = default)
    {
        var person = await PersonAsync(personId, abbruch);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var buchungsweg = weg ?? WegFuer(wer.Rolle);
        var kurse = await speicher.KurseZurAuswahlAsync(semesterIds, abbruch);
        var zahlen = await speicher.BelegungszahlenAsync(kurse.Select(k => k.Id), abbruch);
        var offen = await speicher.OffeneBelegungenAsync(personId, abbruch);
        var jetzt = uhr.GetLocalNow();
        return kurse.Select(k =>
            {
                var (belegt, wartend) = zahlen.GetValueOrDefault(k.Id);
                var pruefung = Buchungspruefung.Pruefen(k, person, buchungsweg, bevollmaechtigterId,
                    new Buchungslage(belegt, wartend, offen.TryGetValue(k.Id, out var s) ? s : null), jetzt);
                var entgelt = k.EntgeltFuerBuchung(jetzt);
                return new KursOption(k, pruefung, belegt, wartend, entgelt?.Betrag, entgelt?.ErmaessigtCent is { } e ? new Geld(e) : null);
            })
            .OrderBy(o => o.Pruefung.Moeglich ? 0 : 1)
            .ThenBy(o => o.Kurs.Kursnummer, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Gültige Ermäßigung der Person heute (falls vorhanden).</summary>
    public async Task<Ermaessigung?> ErmaessigungHeuteAsync(Guid personId, CancellationToken abbruch = default)
    {
        var heute = Heute;
        return (await speicher.ErmaessigungenAsync(personId, abbruch)).Where(e => e.GiltAm(heute)).OrderByDescending(e => e.GueltigBis).FirstOrDefault();
    }

    public Task<IReadOnlyList<Ermaessigung>> ErmaessigungenAsync(Guid personId, CancellationToken abbruch = default)
        => speicher.ErmaessigungenAsync(personId, abbruch);

    public async Task<Ermaessigung> ErmaessigungErfassenAsync(Guid personId, string art, string nachweis, DateOnly von, DateOnly bis,
        CancellationToken abbruch = default)
    {
        var wer = await BuchendeRolleAsync(abbruch);
        await PersonAsync(personId, abbruch);
        var e = new Ermaessigung(personId, art, nachweis, von, bis, uhr.GetUtcNow(), wer.Anzeige);
        speicher.Hinzufuegen(e);
        await protokoll.ProtokollierenAsync("Person.Ermaessigung",
            $"Ermäßigung „{art}“ für {Pseudonym.Person(personId)} erfasst, gültig {von:dd.MM.yyyy}–{bis:dd.MM.yyyy} (Nachweis vermerkt)", nameof(Person), personId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return e;
    }

    // ---------- Anmelden ----------

    public async Task<AnmeldeErgebnis> AnmeldenAsync(AnmeldeAuftrag auftrag, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var weg = auftrag.Weg ?? WegFuer(wer.Rolle);
        if (weg != Buchungsweg.Online && !wer.IstAutomatik && (wer.Rolle is not { } rolle || !BuchungsRollen.Contains(rolle)))
        {
            throw new InvalidOperationException(Oberflaeche.Freigabe.NurFuer(wer.Rolle, BuchungsRollen).Begruendung);
        }

        var kurs = await KursAsync(auftrag.KursId, abbruch);
        var person = await PersonAsync(auftrag.PersonId, abbruch);
        var belegungen = await speicher.BelegungenDesKursesAsync(kurs.Id, abbruch);
        var jetzt = uhr.GetLocalNow();
        var bestehend = belegungen.Where(b => b.PersonId == person.Id && b.Offen).Select(b => (Belegungsstatus?)b.Status).FirstOrDefault();
        var lage = new Buchungslage(belegungen.Count(b => b.HaeltPlatz), belegungen.Count(b => b.Status == Belegungsstatus.Warteliste), bestehend);
        var pruefung = Buchungspruefung.Pruefen(kurs, person, weg, auftrag.BevollmaechtigterId, lage, jetzt);
        var ueberbuchen = auftrag.Ueberbuchen && pruefung.Ueberbuchbar;
        if (!pruefung.Moeglich && !(pruefung.Code == Buchungscode.Ausgebucht && ueberbuchen))
        {
            throw new InvalidOperationException(pruefung.Erklaerung);
        }

        var buchender = weg == Buchungsweg.Online
            ? new Buchender(person.Anzeigename, null, null)
            : new Buchender(wer.Name, wer.Kennung, wer.Rolle?.Anzeigename(), auftrag.BevollmaechtigterId,
                await BevollmaechtigterNameAsync(person, auftrag.BevollmaechtigterId, abbruch), pruefung.VollmachtId);
        var ermaessigung = await ErmaessigungHeuteAsync(person.Id, abbruch);
        var belegung = Belegung.Anlegen(kurs, person.Id, pruefung.AufWarteliste && !ueberbuchen, weg, buchender, ermaessigung, auftrag.Zahlart, jetzt);
        speicher.Hinzufuegen(belegung);

        var warteplatz = belegung.Status == Belegungsstatus.Warteliste ? lage.Wartend + 1 : (int?)null;
        await protokoll.ProtokollierenAsync(warteplatz is null ? "Anmeldung.Angemeldet" : "Anmeldung.Warteliste",
            $"{Pseudonym.Person(person.Id)} {(warteplatz is null ? "angemeldet" : $"auf die Warteliste gesetzt (Platz {warteplatz})")} für {kurs.Kursnummer} · Weg: {BelegungAnzeige.Weg(weg)}"
            + $" · gebucht von {buchender.Name}{(buchender.Rolle is null ? "" : $" ({buchender.Rolle})")}"
            + (auftrag.BevollmaechtigterId is { } bev && bev != person.Id ? $" im Auftrag von {(belegung.BevollmaechtigterName is null ? "?" : BevollmaechtigterProtokoll(bev, person))} (Vollmacht {belegung.VollmachtId?.ToString()[..8] ?? "über Organisation"})" : "")
            + $" · Entgelt {belegung.Entgelt} eingefroren{(belegung.Ermaessigt ? " (ermäßigt)" : "")}{(ueberbuchen ? " · überbucht" : "")}",
            nameof(Person), person.Id, abbruch: abbruch);

        var nachricht = warteplatz is null
            ? await BestaetigungAsync(person, kurs, belegung, abbruch)
            : await MailAsync(person, Textvorlagen.Wartelisteneintrag, Werte(person, kurs, belegung, ("Warteplatz", warteplatz.Value.ToString(De))), null, abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new AnmeldeErgebnis(belegung, pruefung, nachricht);
    }

    // ---------- Warteliste und Nachrücken ----------

    /// <summary>Freie Plätze an die Warteliste vergeben (Nachrückangebot mit Annahmefrist).</summary>
    public async Task<int> NachrueckenAnbietenAsync(Guid kursId, CancellationToken abbruch = default)
    {
        var kurs = await KursAsync(kursId, abbruch);
        var n = await NachrueckenAsync(kurs, await speicher.BelegungenDesKursesAsync(kursId, abbruch), abbruch);
        await speicher.SpeichernAsync(abbruch);
        return n;
    }

    public async Task AngebotAnnehmenAsync(Guid belegungId, CancellationToken abbruch = default)
    {
        var wer = await BuchendeRolleAsync(abbruch);
        var b = await BelegungAsync(belegungId, abbruch);
        var kurs = await KursAsync(b.KursId, abbruch);
        var person = await PersonAsync(b.PersonId, abbruch);
        b.AngebotAnnehmen(kurs.Richtlinie, uhr.GetLocalNow(), wer.Anzeige);
        await protokoll.ProtokollierenAsync("Anmeldung.AngebotAngenommen", $"{Pseudonym.Person(b.PersonId)} hat das Nachrückangebot für {kurs.Kursnummer} angenommen",
            nameof(Person), b.PersonId, abbruch: abbruch);
        await BestaetigungAsync(person, kurs, b, abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task AngebotAblehnenAsync(Guid belegungId, CancellationToken abbruch = default)
    {
        await BuchendeRolleAsync(abbruch);
        var b = await BelegungAsync(belegungId, abbruch);
        var kurs = await KursAsync(b.KursId, abbruch);
        b.AngebotVerfallenLassen(uhr.GetLocalNow(), "Angebot abgelehnt");
        await protokoll.ProtokollierenAsync("Anmeldung.AngebotAbgelehnt", $"{Pseudonym.Person(b.PersonId)} hat das Nachrückangebot für {kurs.Kursnummer} abgelehnt",
            nameof(Person), b.PersonId, abbruch: abbruch);
        await NachrueckenAsync(kurs, await speicher.BelegungenDesKursesAsync(kurs.Id, abbruch), abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Wartende Person sofort anmelden, obwohl der Kurs voll ist (nur mit Recht zum Überbuchen).</summary>
    public async Task AusWartelisteAnmeldenAsync(Guid belegungId, CancellationToken abbruch = default)
    {
        var wer = await BuchendeRolleAsync(abbruch);
        var b = await BelegungAsync(belegungId, abbruch);
        var kurs = await KursAsync(b.KursId, abbruch);
        if (!kurs.Richtlinie.DarfUeberbuchen(WegFuer(wer.Rolle)))
        {
            throw new InvalidOperationException("Ihr Buchungsweg darf diesen Kurs nicht überbuchen.");
        }

        b.AusWartelisteAnmelden(kurs.Richtlinie, uhr.GetLocalNow(), wer.Anzeige);
        await protokoll.ProtokollierenAsync("Anmeldung.Ueberbucht", $"{Pseudonym.Person(b.PersonId)} von der Warteliste in {kurs.Kursnummer} angemeldet (überbucht)",
            nameof(Person), b.PersonId, abbruch: abbruch);
        await BestaetigungAsync(await PersonAsync(b.PersonId, abbruch), kurs, b, abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Automatik: abgelaufene Nachrückangebote verfallen lassen und den Platz der nächsten Person anbieten.</summary>
    public async Task<int> AbgelaufeneAngeboteAsync(CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetLocalNow();
        var abgelaufen = await speicher.AbgelaufeneAngeboteAsync(jetzt, abbruch);
        foreach (var b in abgelaufen)
        {
            b.AngebotVerfallenLassen(jetzt, "Annahmefrist abgelaufen");
            await protokoll.ProtokollierenAsync("Anmeldung.AngebotVerfallen",
                $"Nachrückangebot an {Pseudonym.Person(b.PersonId)} für {b.Kursnummer} verfallen (Frist abgelaufen)", nameof(Person), b.PersonId, abbruch: abbruch);
        }

        foreach (var kursId in abgelaufen.Select(b => b.KursId).Distinct())
        {
            await NachrueckenAsync(await KursAsync(kursId, abbruch), await speicher.BelegungenDesKursesAsync(kursId, abbruch), abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return abgelaufen.Count;
    }

    /// <summary>Automatik: In allen Kursen mit Warteliste freie Plätze anbieten (falls ein Storno ohne Nachrücken blieb).</summary>
    public async Task<int> WartelistenNachrueckenAsync(CancellationToken abbruch = default)
    {
        var angeboten = 0;
        foreach (var kursId in await speicher.KurseMitWartelisteAsync(abbruch))
        {
            angeboten += await NachrueckenAsync(await KursAsync(kursId, abbruch), await speicher.BelegungenDesKursesAsync(kursId, abbruch), abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return angeboten;
    }

    // ---------- Kursabsage und Erinnerung ----------

    /// <summary>Wer einen Kurs absagen darf (Automatik nur, wenn die Aufgabenregel es erlaubt).</summary>
    public static readonly Rolle[] AbsageRollen = [Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    /// <summary>
    /// Sagt den Kurs ab: künftige Veranstaltungen fallen aus, alle offenen Belegungen werden ohne Gebühr storniert
    /// und die Teilnehmenden per Mail (mit Übersetzungslink) benachrichtigt.
    /// </summary>
    /// <returns>Anzahl der aufgehobenen Belegungen.</returns>
    public async Task<int> KursAbsagenAsync(Guid kursId, string grund, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !AbsageRollen.Contains(r)))
        {
            throw new InvalidOperationException(Oberflaeche.Freigabe.NurFuer(wer.Rolle, AbsageRollen).Begruendung);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(grund);
        var kurs = await KursAsync(kursId, abbruch);
        if (kurs.Status == Kursstatus.Abgesagt)
        {
            throw new InvalidOperationException("Der Kurs ist bereits abgesagt.");
        }

        var jetzt = uhr.GetLocalNow();
        kurs.StatusSetzen(Kursstatus.Abgesagt);
        foreach (var v in kurs.Veranstaltungen.Where(v => !v.Ausgefallen && v.Beginn >= jetzt.DateTime))
        {
            v.Ausfallen($"Kurs abgesagt: {grund}");
        }

        var anzahl = 0;
        foreach (var b in (await speicher.BelegungenDesKursesAsync(kursId, abbruch)).Where(b => b.Offen))
        {
            b.Stornieren(0, $"Kurs abgesagt: {grund}", wer.Anzeige, jetzt);
            anzahl++;
            var person = await personen.LadenAsync(b.PersonId, abbruch);
            if (person is not null)
            {
                await MailAsync(person, Textvorlagen.Kursabsage, Werte(person, kurs, b, ("Regel", grund)), null, abbruch);
            }
        }

        await protokoll.ProtokollierenAsync("Kurs.Abgesagt", $"{kurs.Kursnummer} abgesagt ({grund}); {anzahl} Belegung(en) ohne Gebühr aufgehoben und benachrichtigt",
            nameof(Kurs), kurs.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return anzahl;
    }

    /// <summary>Erinnerung vor Kursbeginn an alle Angemeldeten, die noch keine bekommen haben.</summary>
    public async Task<int> ErinnernAsync(Guid kursId, CancellationToken abbruch = default)
    {
        var kurs = await KursAsync(kursId, abbruch);
        var jetzt = uhr.GetLocalNow();
        var anzahl = 0;
        foreach (var b in (await speicher.BelegungenDesKursesAsync(kursId, abbruch)).Where(b => b.Status == Belegungsstatus.Angemeldet && b.ErinnertAm is null))
        {
            b.Erinnern(jetzt);
            var person = await personen.LadenAsync(b.PersonId, abbruch);
            if (person is not null && await MailAsync(person, Textvorlagen.Kurserinnerung, Werte(person, kurs, b), null, abbruch) is not null)
            {
                anzahl++;
            }
        }

        if (anzahl > 0)
        {
            await protokoll.ProtokollierenAsync("Kurs.Erinnerung", $"{kurs.Kursnummer}: Erinnerung vor Kursbeginn an {anzahl} Teilnehmende verschickt", nameof(Kurs), kurs.Id,
                abbruch: abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return anzahl;
    }

    private async Task<int> NachrueckenAsync(Kurs kurs, IReadOnlyList<Belegung> belegungen, CancellationToken abbruch)
    {
        if (kurs.Status != Kursstatus.Veroeffentlicht || kurs.Richtlinie.Belegung == Belegungsart.Kontingent)
        {
            return 0;
        }

        var frei = kurs.MaxTeilnehmende - belegungen.Count(b => b.HaeltPlatz);
        var angeboten = 0;
        var jetzt = uhr.GetLocalNow();
        var frist = new DateTimeOffset(jetzt.Date.AddDays(kurs.Richtlinie.NachrueckfristTage + 1).AddSeconds(-1), jetzt.Offset);
        foreach (var b in belegungen.Where(b => b.Status == Belegungsstatus.Warteliste).OrderBy(b => b.WartelisteSeit).Take(Math.Max(0, frei)))
        {
            b.NachrueckenAnbieten(frist);
            angeboten++;
            await protokoll.ProtokollierenAsync("Anmeldung.Nachrueckangebot",
                $"Warteliste {kurs.Kursnummer}: Nachrückangebot an {Pseudonym.Person(b.PersonId)}, Frist {frist:dd.MM.yyyy HH:mm}", nameof(Person), b.PersonId,
                abbruch: abbruch);
            var person = await personen.LadenAsync(b.PersonId, abbruch);
            if (person is not null)
            {
                await MailAsync(person, Textvorlagen.Nachrueckangebot, Werte(person, kurs, b, ("Frist", $"{frist:dd.MM.yyyy, HH:mm} Uhr")), null, abbruch);
            }
        }

        return angeboten;
    }

    // ---------- Storno und Umbuchung ----------

    public async Task<Stornoregel> StornoVorschauAsync(Guid belegungId, CancellationToken abbruch = default)
    {
        var b = await BelegungAsync(belegungId, abbruch);
        return Buchungspruefung.Storno(b, await KursAsync(b.KursId, abbruch), uhr.GetLocalNow());
    }

    public async Task<Stornoregel> StornierenAsync(Guid belegungId, string grund, CancellationToken abbruch = default)
    {
        var wer = await BuchendeRolleAsync(abbruch);
        var b = await BelegungAsync(belegungId, abbruch);
        var kurs = await KursAsync(b.KursId, abbruch);
        var belegungen = await speicher.BelegungenDesKursesAsync(kurs.Id, abbruch);
        var jetzt = uhr.GetLocalNow();
        var regel = Buchungspruefung.Storno(b, kurs, jetzt);
        var hielt = b.HaeltPlatz;
        b.Stornieren(regel.GebuehrCent, string.IsNullOrWhiteSpace(grund) ? "ohne Angabe" : grund.Trim(), wer.Anzeige, jetzt);
        await protokoll.ProtokollierenAsync("Anmeldung.Storniert",
            $"{Pseudonym.Person(b.PersonId)} von {kurs.Kursnummer} abgemeldet · {regel.Erklaerung} · Gebühr {regel.Gebuehr}", nameof(Person), b.PersonId, abbruch: abbruch);
        var person = await personen.LadenAsync(b.PersonId, abbruch);
        if (person is not null)
        {
            await MailAsync(person, Textvorlagen.Stornobestaetigung,
                Werte(person, kurs, b, ("Regel", regel.Erklaerung), ("Gebuehr", regel.Gebuehr.ToString())), null, abbruch);
        }

        if (hielt)
        {
            await NachrueckenAsync(kurs, belegungen, abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return regel;
    }

    /// <summary>Ähnliche Kurse, die die Person buchen kann (gleiche Kursart oder ähnlicher Titel im selben Fachbereich).</summary>
    public async Task<IReadOnlyList<KursOption>> UmbuchungsvorschlaegeAsync(Guid belegungId, int anzahl = 6, CancellationToken abbruch = default)
    {
        var b = await BelegungAsync(belegungId, abbruch);
        var kurs = await KursAsync(b.KursId, abbruch);
        var jetzt = uhr.GetLocalNow();
        var semester = (await programm.SemesterAsync(abbruch)).Where(s => s.Ende >= DateOnly.FromDateTime(jetzt.Date)).Select(s => s.Id).ToList();
        var woerter = Suchtext.Woerter(kurs.Titel).Where(w => w.Length > 3).ToHashSet();
        var optionen = await KursauswahlAsync(b.PersonId, semester, b.BevollmaechtigterId, b.Weg, abbruch);
        return optionen
            .Where(o => o.Kurs.Id != kurs.Id && o.Pruefung.Code == Buchungscode.Buchbar
                        && (o.Kurs.KursartId == kurs.KursartId || o.Kurs.Fachbereich == kurs.Fachbereich))
            .OrderByDescending(o => (o.Kurs.KursartId == kurs.KursartId ? 2 : 0) + Suchtext.Woerter(o.Kurs.Titel).Count(woerter.Contains) * 3)
            .ThenBy(o => o.Kurs.ErsterTermin)
            .Take(anzahl)
            .ToList();
    }

    public async Task<Belegung> UmbuchenAsync(Guid belegungId, Guid neuerKursId, CancellationToken abbruch = default)
    {
        var wer = await BuchendeRolleAsync(abbruch);
        var alt = await BelegungAsync(belegungId, abbruch);
        var altKurs = await KursAsync(alt.KursId, abbruch);
        var neuKurs = await KursAsync(neuerKursId, abbruch);
        var person = await PersonAsync(alt.PersonId, abbruch);
        var neuBelegungen = await speicher.BelegungenDesKursesAsync(neuKurs.Id, abbruch);
        var altBelegungen = await speicher.BelegungenDesKursesAsync(altKurs.Id, abbruch);
        var jetzt = uhr.GetLocalNow();
        var lage = new Buchungslage(neuBelegungen.Count(b => b.HaeltPlatz), neuBelegungen.Count(b => b.Status == Belegungsstatus.Warteliste),
            neuBelegungen.Where(b => b.PersonId == person.Id && b.Offen).Select(b => (Belegungsstatus?)b.Status).FirstOrDefault());
        var pruefung = Buchungspruefung.Pruefen(neuKurs, person, alt.Weg, alt.BevollmaechtigterId, lage, jetzt);
        if (pruefung.Code != Buchungscode.Buchbar)
        {
            throw new InvalidOperationException($"Umbuchung nicht möglich: {pruefung.Erklaerung}");
        }

        if (altKurs.ErsterTermin is { } beginn && beginn < jetzt.LocalDateTime && altKurs.Status != Kursstatus.Abgesagt)
        {
            throw new InvalidOperationException($"Der Kurs {altKurs.Kursnummer} hat am {beginn:dd.MM.yyyy} begonnen; eine Umbuchung ist nur vor Kursbeginn möglich. Bitte stornieren.");
        }

        var hielt = alt.HaeltPlatz;
        var buchender = new Buchender(wer.Name, wer.Kennung, wer.Rolle?.Anzeigename(), alt.BevollmaechtigterId, alt.BevollmaechtigterName, pruefung.VollmachtId);
        var neu = Belegung.Anlegen(neuKurs, person.Id, false, alt.Weg, buchender, await ErmaessigungHeuteAsync(person.Id, abbruch), alt.Zahlart, jetzt);
        var gebuehr = altKurs.Status == Kursstatus.Abgesagt ? 0 : altKurs.Richtlinie.UmbuchungsgebuehrCent;
        alt.Umbuchen(neu, gebuehr, wer.Anzeige, jetzt);
        speicher.Hinzufuegen(neu);
        await protokoll.ProtokollierenAsync("Anmeldung.Umgebucht",
            $"{Pseudonym.Person(person.Id)} von {altKurs.Kursnummer} auf {neuKurs.Kursnummer} umgebucht · Gebühr {new Geld(gebuehr)} · neues Entgelt {neu.Entgelt} eingefroren",
            nameof(Person), person.Id, abbruch: abbruch);
        await MailAsync(person, Textvorlagen.Umbuchungsbestaetigung,
            Werte(person, neuKurs, neu, ("Kursnummer", altKurs.Kursnummer), ("NeuerKurs", $"{neuKurs.Kursnummer} {neuKurs.Titel}"), ("Gebuehr", new Geld(gebuehr).ToString())),
            Anhang(neuKurs), abbruch);
        if (hielt)
        {
            await NachrueckenAsync(altKurs, altBelegungen, abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return neu;
    }

    // ---------- Vorab-Einbuchung und Kontingent ----------

    public async Task<Einbuchungsergebnis> EinbuchenAsync(Guid belegungId, Guid veranstaltungId, Guid ortId, CancellationToken abbruch = default)
    {
        var wer = await BuchendeRolleAsync(abbruch);
        var b = await BelegungAsync(belegungId, abbruch);
        var kurs = await KursAsync(b.KursId, abbruch);
        if (!kurs.Richtlinie.VorabEinbuchung)
        {
            throw new InvalidOperationException("Dieser Kurs braucht keine Vorab-Einbuchung: Angemeldete nehmen an allen Veranstaltungen teil.");
        }

        var v = kurs.Veranstaltungen.SingleOrDefault(x => x.Id == veranstaltungId) ?? throw new InvalidOperationException("Veranstaltung nicht gefunden.");
        var vo = v.Orte.SingleOrDefault(o => o.OrtId == ortId) ?? throw new InvalidOperationException("Dieser Ort gehört nicht zur Veranstaltung.");
        var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
        var lage = Einbuchungslage.Berechnen(await speicher.BelegungenDesKursesAsync(kurs.Id, abbruch));
        var kapazitaet = Ortsplanung.KapazitaetAmOrt(vo, kurs, verzeichnis);
        var ergebnis = b.Einbuchen(v, ortId, lage.Belegt(v.Id, ortId), kapazitaet, kurs.Richtlinie, uhr.GetLocalNow(), wer.Anzeige);
        if (!ergebnis.Moeglich)
        {
            throw new InvalidOperationException(ergebnis.Begruendung);
        }

        await protokoll.ProtokollierenAsync(ergebnis.Code == Einbuchungscode.Eingebucht ? "Kontingent.Eingebucht" : "Kontingent.Warteliste",
            $"{Pseudonym.Person(b.PersonId)} {(ergebnis.Code == Einbuchungscode.Eingebucht ? "eingebucht" : "auf die Warteliste gesetzt")}: {kurs.Kursnummer} am {v.Beginn:dd.MM.yyyy HH:mm} ({vo.OrtName}) · Kontingent: {Text(b.Kontingent)}",
            nameof(Person), b.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return ergebnis;
    }

    /// <summary>Austragen aus einer Veranstaltung; wer für diese Veranstaltung wartet, rückt automatisch nach (mit Mail).</summary>
    public async Task AustragenAsync(Guid belegungId, Guid veranstaltungId, CancellationToken abbruch = default)
    {
        var wer = await BuchendeRolleAsync(abbruch);
        var b = await BelegungAsync(belegungId, abbruch);
        var kurs = await KursAsync(b.KursId, abbruch);
        var v = kurs.Veranstaltungen.SingleOrDefault(x => x.Id == veranstaltungId) ?? throw new InvalidOperationException("Veranstaltung nicht gefunden.");
        var jetzt = uhr.GetLocalNow();
        var warAufWarteliste = b.WartetAuf.ContainsKey(veranstaltungId);
        var ort = b.Einbuchungen.GetValueOrDefault(veranstaltungId);
        b.Austragen(veranstaltungId, v.Beginn, kurs.Richtlinie, jetzt, wer.Anzeige);
        await protokoll.ProtokollierenAsync("Kontingent.Ausgetragen",
            $"{Pseudonym.Person(b.PersonId)} {(warAufWarteliste ? "von der Warteliste" : "aus")} {kurs.Kursnummer} am {v.Beginn:dd.MM.yyyy HH:mm} ausgetragen · Kontingent: {Text(b.Kontingent)}",
            nameof(Person), b.PersonId, abbruch: abbruch);
        if (!warAufWarteliste && ort is { } ortId)
        {
            await VeranstaltungNachrueckenAsync(kurs, v, ortId, wer.Anzeige, abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
    }

    private async Task VeranstaltungNachrueckenAsync(Kurs kurs, Veranstaltung v, Guid ortId, string von, CancellationToken abbruch)
    {
        var belegungen = await speicher.BelegungenDesKursesAsync(kurs.Id, abbruch);
        var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
        var vo = v.Orte.First(o => o.OrtId == ortId);
        var kapazitaet = Ortsplanung.KapazitaetAmOrt(vo, kurs, verzeichnis);
        var jetzt = uhr.GetLocalNow();
        foreach (var wartend in belegungen.Where(x => x.Status == Belegungsstatus.Angemeldet && x.WartetAuf.TryGetValue(v.Id, out var w) && (w.Ort ?? ortId) == ortId)
                     .OrderBy(x => x.WartetAuf[v.Id].Seit))
        {
            var lage = Einbuchungslage.Berechnen(belegungen);
            if (wartend.EinbuchungPruefen(v, ortId, lage.Belegt(v.Id, ortId), kapazitaet, kurs.Richtlinie, jetzt).Code != Einbuchungscode.Eingebucht)
            {
                continue;
            }

            wartend.Einbuchen(v, ortId, lage.Belegt(v.Id, ortId), kapazitaet, kurs.Richtlinie, jetzt, $"Automatik (nach Austragen durch {von})");
            await protokoll.ProtokollierenAsync("Kontingent.Nachgerueckt",
                $"{Pseudonym.Person(wartend.PersonId)} von der Warteliste in {kurs.Kursnummer} am {v.Beginn:dd.MM.yyyy HH:mm} eingebucht", nameof(Person), wartend.PersonId,
                abbruch: abbruch);
            var person = await personen.LadenAsync(wartend.PersonId, abbruch);
            if (person is not null)
            {
                await MailAsync(person, Textvorlagen.Einbuchungsbestaetigung,
                    Werte(person, kurs, wartend, ("Veranstaltung", $"{v.Beginn:ddd dd.MM.yyyy, HH:mm} Uhr ({vo.OrtName})")), null, abbruch);
            }

            break;
        }
    }

    /// <summary>Automatik: Kontingente nach Ablauf der Gültigkeit schließen; der Rest verfällt.</summary>
    public async Task<int> KontingenteAblaufenAsync(CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetLocalNow();
        var heute = Heute;
        var anzahl = 0;
        foreach (var b in await speicher.KontingenteMitAblaufVorAsync(heute, abbruch))
        {
            var verfallen = b.KontingentAblaufenLassen(heute, jetzt);
            if (verfallen > 0)
            {
                anzahl++;
                await protokoll.ProtokollierenAsync("Kontingent.Abgelaufen", $"Kontingent von {Pseudonym.Person(b.PersonId)} in {b.Kursnummer} abgelaufen: {verfallen} Einheiten verfallen",
                    nameof(Person), b.PersonId, abbruch: abbruch);
            }
        }

        await speicher.SpeichernAsync(abbruch);
        return anzahl;
    }

    // ---------- Abfragen ----------

    public async Task<IReadOnlyList<TeilnehmerZeile>> TeilnehmendeAsync(Guid kursId, CancellationToken abbruch = default)
    {
        var belegungen = await speicher.BelegungenDesKursesAsync(kursId, abbruch);
        var namen = await personen.AnzeigenamenAsync(belegungen.Select(b => b.PersonId).Distinct(), abbruch);
        var warte = belegungen.Where(b => b.Status == Belegungsstatus.Warteliste).OrderBy(b => b.WartelisteSeit).Select((b, i) => (b.Id, i + 1))
            .ToDictionary(x => x.Id, x => x.Item2);
        return belegungen
            .Select(b => new TeilnehmerZeile(b, namen.GetValueOrDefault(b.PersonId) ?? Pseudonym.Person(b.PersonId), warte.TryGetValue(b.Id, out var p) ? p : null))
            .OrderBy(z => z.Belegung.Status switch
            {
                Belegungsstatus.Angemeldet => 0,
                Belegungsstatus.Nachrueckangebot => 1,
                Belegungsstatus.Warteliste => 2,
                _ => 3,
            })
            .ThenBy(z => z.Warteplatz ?? 0)
            .ThenBy(z => z.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public Task<IReadOnlyList<PersonBelegung>> BelegungenDerPersonAsync(Guid personId, CancellationToken abbruch = default)
        => speicher.BelegungenDerPersonAsync(personId, abbruch);

    public Task<Belegung?> BelegungLadenAsync(Guid id, CancellationToken abbruch = default) => speicher.BelegungLadenAsync(id, abbruch);

    public async Task<Kurs?> KursZurBelegungAsync(Belegung b, CancellationToken abbruch = default)
        => await programm.KursLadenAsync(b.KursId, abbruch);

    /// <summary>Kalenderdatei mit den (künftigen) Terminen; bei Kontingenten nur die eingebuchten.</summary>
    public async Task<(string Dateiname, string Inhalt)> KalenderdateiAsync(Guid belegungId, CancellationToken abbruch = default)
    {
        var b = await BelegungAsync(belegungId, abbruch);
        var kurs = await KursAsync(b.KursId, abbruch);
        return ($"{kurs.Kursnummer}-termine.ics", Kalenderdatei.Erzeugen(kurs, Termine(kurs, b), await EinrichtungAsync(abbruch), uhr.GetUtcNow()));
    }

    // ---------- Hilfen ----------

    private IEnumerable<Veranstaltung> Termine(Kurs kurs, Belegung b)
        => b.IstKontingent && kurs.Richtlinie.VorabEinbuchung
            ? kurs.Veranstaltungen.Where(v => b.Einbuchungen.ContainsKey(v.Id))
            : kurs.Veranstaltungen.Where(v => v.Ende >= uhr.GetLocalNow().LocalDateTime);

    private (string, string, string)? Anhang(Kurs kurs, Belegung? b = null)
    {
        var termine = b is null ? kurs.Veranstaltungen.Where(v => v.Ende >= uhr.GetLocalNow().LocalDateTime) : Termine(kurs, b);
        var liste = termine.ToList();
        return liste.Count == 0 ? null : ($"{kurs.Kursnummer}-termine.ics", "text/calendar", Kalenderdatei.Erzeugen(kurs, liste, "Volkshochschule", uhr.GetUtcNow()));
    }

    /// <summary>Bestätigung mit Kalenderdatei, persönlichem Check-in-Link (Gerät merken) und QR-Ausweis als Bild.</summary>
    private Task<Guid?> BestaetigungAsync(Person person, Kurs kurs, Belegung b, CancellationToken abbruch)
    {
        var werte = Werte(person, kurs, b);
        List<Postausgang.Mailanhang> weitere = [];
        if (MailMoeglich(person))
        {
            werte["Checkinlink"] = zugang.LinkErzeugen(person.Id).ToString();
            weitere.Add(new Postausgang.Mailanhang("checkin-ausweis.svg", "image/svg+xml", qr.Svg(zugang.AusweisErzeugen(person.Id))));
        }

        return MailAsync(person, Textvorlagen.Anmeldebestaetigung, werte, Anhang(kurs, b), abbruch, weitere);
    }

    private static bool MailMoeglich(Person person) => person.EMail is { Length: > 0 } m && m != "–" && !person.IstAnonymisiert;

    private async Task<Guid?> MailAsync(Person person, string vorlageSchluessel, Dictionary<string, string> werte, (string, string, string)? anhang,
        CancellationToken abbruch, IEnumerable<Postausgang.Mailanhang>? weitere = null)
    {
        if (person.EMail is not { Length: > 0 } mail || person.IstAnonymisiert || mail == "–")
        {
            return null;
        }

        var vorlage = await vorlagen.VorlageAsync(vorlageSchluessel, abbruch);
        var vorschlag = Textvorlagen.Vorschlaege.First(v => v.Schluessel == vorlageSchluessel);
        vorlage ??= new Textvorlage(vorschlag.Schluessel, vorschlag.Bezeichnung, vorschlag.Betreff, vorschlag.Text, uhr.GetUtcNow(), "Vorschlag");
        werte["Einrichtung"] = await EinrichtungAsync(abbruch);
        var (betreff, text) = vorlage.Fuellen(werte);
        if (werte.TryGetValue("Checkinlink", out var checkin) && !text.Contains(checkin, StringComparison.Ordinal))
        {
            text += $"\n\nPersönlicher Check-in: Öffnen Sie diesen Link einmal auf Ihrem Handy – danach genügt ein Scan des QR-Codes im Kursraum.\n{checkin}\nIm Anhang finden Sie außerdem Ihren QR-Ausweis zum Vorzeigen bei der Kursleitung.";
        }

        var n = await post.KundenmailEinreihenAsync(mail, person.Anzeigename, betreff, ZeilenOhneLeere(text), person.Sprache, person.Id, abbruch, anhang, weitere);
        return n.Id;
    }

    private static string ZeilenOhneLeere(string text) => text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);

    private Dictionary<string, string> Werte(Person person, Kurs kurs, Belegung b, params (string, string)[] weitere)
    {
        var termine = kurs.Veranstaltungen.Where(v => !v.Ausgefallen).OrderBy(v => v.Beginn).ToList();
        var erster = termine.FirstOrDefault(v => v.Ende >= uhr.GetLocalNow().LocalDateTime) ?? termine.FirstOrDefault();
        var frist = kurs.ErsterTermin?.Date.AddDays(-kurs.Richtlinie.StornofristTage);
        var w = new Dictionary<string, string>
        {
            ["Name"] = person.Anzeigename,
            ["Kursnummer"] = kurs.Kursnummer,
            ["Kurstitel"] = kurs.Titel,
            ["Beginn"] = erster is null ? "wird noch bekannt gegeben" : erster.Beginn.ToString("ddd dd.MM.yyyy, HH:mm 'Uhr'", De),
            ["Ort"] = erster is null ? "–" : string.Join(" + ", erster.Orte.Select(o => o.OrtName)),
            ["Termine"] = kurs.Umfang == Kursumfang.Unbefristet ? "fortlaufend" : $"{termine.Count} Termine",
            ["Entgelt"] = b.Entgelt + (b.Ermaessigt ? " (ermäßigt)" : ""),
            ["Zahlart"] = BelegungAnzeige.Zahlart(b.Zahlart),
            ["Abmeldefrist"] = frist is not { } f ? "Kursbeginn"
                : f < uhr.GetLocalNow().Date ? $"{f.ToString("dd.MM.yyyy", De)} (Frist bereits abgelaufen; es gelten die AGB)" : f.ToString("dd.MM.yyyy", De),
            ["Kontingent"] = b.IstKontingent ? $"Kontingent: {Text(b.Kontingent)}" : string.Empty,
        };
        foreach (var (k, v) in weitere)
        {
            w[k] = v;
        }

        return w;
    }

    public static string Text(Kontingentstand s)
        => $"{s.Anzahl}er-Kontingent, {s.Besucht} besucht, {s.Verfallen} verfallen, {s.Eingebucht} eingebucht, {s.Verbleibend} verbleibend{(s.GueltigBis is { } b ? $", gültig bis {b:dd.MM.yyyy}" : "")}";

    private static string BevollmaechtigterProtokoll(Guid bev, Person person)
        => person.Vollmachten.Any(v => v.BevollmaechtigterId == bev && v.Art == Bevollmaechtigtenart.Organisation)
           || person.Organisationen.Any(o => o.OrganisationId == bev)
            ? person.Vollmachten.FirstOrDefault(v => v.BevollmaechtigterId == bev)?.BevollmaechtigterName
              ?? person.Organisationen.First(o => o.OrganisationId == bev).OrganisationName
            : Pseudonym.Person(bev);

    private async Task<string?> BevollmaechtigterNameAsync(Person person, Guid? bev, CancellationToken abbruch)
    {
        if (bev is not { } id || id == person.Id)
        {
            return null;
        }

        return person.Vollmachten.FirstOrDefault(v => v.BevollmaechtigterId == id)?.BevollmaechtigterName
               ?? person.Organisationen.FirstOrDefault(o => o.OrganisationId == id)?.OrganisationName
               ?? (await personen.AnzeigenamenAsync([id], abbruch)).GetValueOrDefault(id);
    }

    private async Task<string> EinrichtungAsync(CancellationToken abbruch) => (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Ihre Volkshochschule";

    private async Task<Benutzer> BuchendeRolleAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !BuchungsRollen.Contains(r)))
        {
            throw new InvalidOperationException(Oberflaeche.Freigabe.NurFuer(wer.Rolle, BuchungsRollen).Begruendung);
        }

        return wer;
    }

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    private async Task<Kurs> KursAsync(Guid id, CancellationToken abbruch)
        => await programm.KursLadenAsync(id, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");

    private async Task<Person> PersonAsync(Guid id, CancellationToken abbruch)
        => await personen.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");

    private async Task<Belegung> BelegungAsync(Guid id, CancellationToken abbruch)
        => await speicher.BelegungLadenAsync(id, abbruch) ?? throw new InvalidOperationException("Belegung nicht gefunden.");
}
