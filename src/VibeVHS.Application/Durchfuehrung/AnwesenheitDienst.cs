using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Durchfuehrung;

public interface IDurchfuehrungSpeicher
{
    /// <summary>Anwesenheiten einer Veranstaltung, verfolgt.</summary>
    Task<IReadOnlyList<Anwesenheit>> AnwesenheitenAsync(Guid veranstaltungId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Anwesenheit>> AnwesenheitenDesKursesAsync(Guid kursId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Anwesenheit>> AnwesenheitenDerPersonAsync(Guid personId, CancellationToken abbruch = default);

    Task<Listenabschluss?> AbschlussAsync(Guid veranstaltungId, CancellationToken abbruch = default);

    Task<IReadOnlySet<Guid>> AbgeschlosseneAsync(IEnumerable<Guid> veranstaltungIds, CancellationToken abbruch = default);

    Task<Guid?> KursZurVeranstaltungAsync(Guid veranstaltungId, CancellationToken abbruch = default);

    /// <summary>Vergangene, noch nicht abgeschlossene Veranstaltungen von Kursen mit Belegungen (Veranstaltung, Kurs).</summary>
    Task<IReadOnlyList<(Guid VeranstaltungId, Guid KursId)>> OffeneListenAsync(DateTime endeVor, DateTime endeNach, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Termin in der Tagesübersicht der Anwesenheit.</summary>
public sealed record TerminZeile(Guid VeranstaltungId, Guid KursId, string Kursnummer, string Titel, DateTime Beginn, DateTime Ende, string Orte, string Dozent,
    bool Abgeschlossen, bool Eigene);

public sealed record ListenZeile(Belegung Belegung, string Name, Anwesenheit? Anwesenheit, bool Eingebucht, Checkinergebnis Erfassbar);

public sealed record Anwesenheitsliste(Kurs Kurs, Veranstaltung Veranstaltung, IReadOnlyList<ListenZeile> Zeilen, Listenabschluss? Abschluss)
{
    public int Dauer => (int)(Veranstaltung.Ende - Veranstaltung.Beginn).TotalMinutes;

    public int Anwesend => Zeilen.Count(z => z.Anwesenheit?.Status == Anwesenheitsstatus.Anwesend);
}

/// <summary>Ergebnis eines Check-ins mit Kurs und Name (für die Rückmeldung auf dem Handy bzw. in der Liste).</summary>
public sealed record CheckinAntwort(Checkinergebnis Ergebnis, string? Kurs, string? Name);

public enum RaumCheckinStatus
{
    Eingecheckt,
    Abgelehnt,
    AuswahlNoetig,
    KeinGeraet,
    TokenUngueltig,
}

/// <summary>Antwort auf einen Termin-QR-Scan (mit Personenauswahl, wenn mehrere auf dem Gerät gemerkt sind).</summary>
public sealed record RaumCheckinAntwort(RaumCheckinStatus Status, string Text, string? Kurs, IReadOnlyList<(Guid PersonId, string Name)> Personen, CheckinAntwort? Checkin);

/// <summary>
/// Anwesenheit: Liste je Veranstaltung (minuten-/UE-genau), QR-Check-in (Raum-QR mit persönlichem Code oder gescannter Ausweis),
/// Nacherfassung mit Begründung, Abschluss mit Nichterscheinen-Regel. Jede Erfassung wird protokolliert.
/// </summary>
public sealed class AnwesenheitDienst(
    IDurchfuehrungSpeicher speicher,
    IAnmeldungSpeicher anmeldungen,
    IProgrammSpeicher programm,
    IPersonenSpeicher personen,
    IBenutzerkonten konten,
    Termincodes termincodes,
    CheckinZugang zugang,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    public static readonly Rolle[] ErfassungsRollen =
        [Rolle.Dozierende, Rolle.Teilnehmerservice, Rolle.IntegrationskursTeam, Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    private DateTimeOffset Jetzt => uhr.GetLocalNow();

    // ---------- Übersicht und Liste ----------

    public async Task<IReadOnlyList<TerminZeile>> TermineAsync(DateOnly tag, bool nurEigene, CancellationToken abbruch = default)
    {
        var eigene = await EigenePersonAsync(abbruch);
        var termine = await programm.TermineAsync(tag.ToDateTime(TimeOnly.MinValue), tag.AddDays(1).ToDateTime(TimeOnly.MinValue), abbruch);
        var zu = await speicher.AbgeschlosseneAsync(termine.Select(t => t.Termin.VeranstaltungId), abbruch);
        return termine.Where(t => !t.Termin.Ausgefallen)
            .Select(t => new TerminZeile(t.Termin.VeranstaltungId, t.Termin.KursId, t.Kursnummer, t.Termin.Kurs, t.Termin.Beginn, t.Termin.Ende,
                string.Join(" + ", t.OrtNamen), t.Dozent, zu.Contains(t.Termin.VeranstaltungId), eigene is not null && t.Termin.DozentId == eigene))
            .Where(t => !nurEigene || t.Eigene)
            .OrderBy(t => t.Beginn).ThenBy(t => t.Kursnummer, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<Anwesenheitsliste?> ListeAsync(Guid veranstaltungId, CancellationToken abbruch = default)
    {
        if (await speicher.KursZurVeranstaltungAsync(veranstaltungId, abbruch) is not { } kursId
            || await programm.KursLadenAsync(kursId, abbruch) is not { } kurs)
        {
            return null;
        }

        var v = kurs.Veranstaltungen.First(x => x.Id == veranstaltungId);
        var belegungen = await anmeldungen.BelegungenDesKursesAsync(kursId, abbruch);
        var vorhanden = await speicher.AnwesenheitenAsync(veranstaltungId, abbruch);
        var namen = await personen.AnzeigenamenAsync(belegungen.Select(b => b.PersonId).Distinct(), abbruch);
        var jetzt = Jetzt;
        var zeilen = belegungen
            .Where(b => b.Status == Belegungsstatus.Angemeldet || vorhanden.Any(a => a.BelegungId == b.Id))
            .Where(b => !(b.IstKontingent && kurs.Richtlinie.VorabEinbuchung) || b.Einbuchungen.ContainsKey(v.Id) || vorhanden.Any(a => a.BelegungId == b.Id)
                        || b.Ereignisse.Any(e => e.VeranstaltungId == v.Id))
            .Select(b =>
            {
                var a = vorhanden.FirstOrDefault(x => x.BelegungId == b.Id);
                return new ListenZeile(b, namen.GetValueOrDefault(b.PersonId) ?? Pseudonym.Person(b.PersonId), a, b.Einbuchungen.ContainsKey(v.Id),
                    Checkinregel.Pruefen(b, kurs, v, null, false, jetzt, zeitfensterPruefen: false));
            })
            .OrderBy(z => z.Name, StringComparer.CurrentCulture)
            .ToList();
        return new Anwesenheitsliste(kurs, v, zeilen, await speicher.AbschlussAsync(veranstaltungId, abbruch));
    }

    /// <summary>Darf der aktuelle Benutzer die Liste dieser Veranstaltung bearbeiten? (Dozierende nur eigene.)</summary>
    public async Task<string?> ZugriffVerweigertAsync(Kurs kurs, Veranstaltung v, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (wer.IstAutomatik)
        {
            return null;
        }

        if (wer.Rolle is not { } r || !ErfassungsRollen.Contains(r))
        {
            return Oberflaeche.Freigabe.NurFuer(wer.Rolle, ErfassungsRollen).Begruendung;
        }

        if (r == Rolle.Dozierende)
        {
            var eigene = await EigenePersonAsync(abbruch);
            if (eigene is null || (v.DozentId != eigene && kurs.Team.All(t => t.PersonId != eigene)))
            {
                return "Dozierende erfassen nur die Anwesenheit eigener Veranstaltungen.";
            }
        }

        return null;
    }

    /// <summary>Signiertes Raum-Token, gültig von 30 Minuten vor Beginn bis zum Ende der Veranstaltung.</summary>
    public async Task<string> RaumTokenAsync(Guid veranstaltungId, Guid ortId, CancellationToken abbruch = default)
    {
        var liste = await ListeAsync(veranstaltungId, abbruch) ?? throw new InvalidOperationException("Veranstaltung nicht gefunden.");
        if (await ZugriffVerweigertAsync(liste.Kurs, liste.Veranstaltung, abbruch) is { } grund)
        {
            throw new InvalidOperationException(grund);
        }

        var v = liste.Veranstaltung;
        if (v.Orte.All(o => o.OrtId != ortId))
        {
            throw new InvalidOperationException("Der Ort gehört nicht zu dieser Veranstaltung.");
        }

        if ((await programm.OrtsverzeichnisAsync(abbruch)).Finden(ortId)?.Art == Ortsart.VirtuellerRaum)
        {
            throw new InvalidOperationException("Online-Teilnahme wird separat in der Liste erfasst; für virtuelle Räume gibt es keinen Termin-QR.");
        }

        if (liste.Kurs.Richtlinie.SelbstCheckin == SelbstCheckin.Aus)
        {
            throw new InvalidOperationException("Für diesen Kurs ist der Selbst-Check-in abgeschaltet (abrechnungsrelevante Anwesenheit). Bitte Ausweise scannen oder die Liste abhaken.");
        }

        var offset = Jetzt.Offset;
        return await termincodes.CodeAsync(v.Id, ortId, new DateTimeOffset(v.Beginn - Checkinregel.Vorlauf, offset), new DateTimeOffset(v.Beginn + Checkinregel.Vorlauf, offset), abbruch);
    }

    // ---------- Erfassen ----------

    public async Task ErfassenAsync(Guid veranstaltungId, Guid belegungId, Anwesenheitsstatus status, int? minuten, string? bemerkung,
        CancellationToken abbruch = default)
    {
        if (status == Anwesenheitsstatus.Unbestaetigt)
        {
            throw new InvalidOperationException("„Unbestätigt“ entsteht nur durch Selbst-Check-in.");
        }

        var liste = await ListeAsync(veranstaltungId, abbruch) ?? throw new InvalidOperationException("Veranstaltung nicht gefunden.");
        var (kurs, v) = (liste.Kurs, liste.Veranstaltung);
        if (await ZugriffVerweigertAsync(kurs, v, abbruch) is { } grund)
        {
            throw new InvalidOperationException(grund);
        }

        var jetzt = Jetzt;
        if (jetzt.LocalDateTime < v.Beginn - Checkinregel.Vorlauf)
        {
            throw new InvalidOperationException($"Die Anwesenheit kann erst ab {v.Beginn - Checkinregel.Vorlauf:dd.MM. HH:mm} Uhr erfasst werden.");
        }

        var nacherfassung = jetzt.LocalDateTime > v.Ende.AddHours(1);
        if (nacherfassung && string.IsNullOrWhiteSpace(bemerkung))
        {
            throw new InvalidOperationException("Nacherfassung nach Ende der Veranstaltung: Bitte einen Grund angeben (z. B. „Liste vom Papier übertragen“).");
        }

        var zeile = liste.Zeilen.FirstOrDefault(z => z.Belegung.Id == belegungId) ?? throw new InvalidOperationException("Belegung gehört nicht zu dieser Liste.");
        var b = zeile.Belegung;
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var vorhanden = (await speicher.AnwesenheitenAsync(veranstaltungId, abbruch)).FirstOrDefault(a => a.BelegungId == belegungId);
        var weg = nacherfassung ? Erfassungsweg.Nacherfassung : Erfassungsweg.Liste;
        var bestaetigung = vorhanden?.Status == Anwesenheitsstatus.Unbestaetigt;

        // QA-63: Berichtigung eines vorhandenen Eintrags – 30 Minuten frei für die eintragende Person, danach und an fremden Einträgen mit Grund.
        if (vorhanden is not null && !bestaetigung && vorhanden.Status != status
            && Korrekturregel.Pruefen(vorhanden.ErfasstVon == wer.Anzeige, vorhanden.ErfasstAm, jetzt, bemerkung) is { } korrektur)
        {
            throw new InvalidOperationException(korrektur);
        }
        await KontingentBuchenAsync(b, kurs, v, status, bestaetigung ? null : vorhanden, jetzt, wer.Anzeige, abbruch);

        var min = status == Anwesenheitsstatus.Anwesend ? minuten ?? (bestaetigung ? vorhanden!.Minuten : liste.Dauer) : 0;
        if (vorhanden is null)
        {
            speicher.Hinzufuegen(new Anwesenheit(b, v, v.Orte.FirstOrDefault()?.OrtId, status, min, weg, wer.Anzeige, jetzt, bemerkung));
        }
        else
        {
            vorhanden.Aendern(null, status, min, bestaetigung && status == Anwesenheitsstatus.Anwesend ? vorhanden.Weg : weg, wer.Anzeige, jetzt,
                bestaetigung ? $"Selbst-Check-in {(status == Anwesenheitsstatus.Anwesend ? "bestätigt" : "nicht bestätigt")}{(string.IsNullOrWhiteSpace(bemerkung) ? "" : $": {bemerkung}")}" : bemerkung,
                liste.Dauer);
        }

        await protokoll.ProtokollierenAsync(nacherfassung ? "Anwesenheit.Nacherfassung" : bestaetigung ? "Anwesenheit.Bestaetigt" : "Anwesenheit.Erfasst",
            $"{Pseudonym.Person(b.PersonId)} in {kurs.Kursnummer} am {v.Beginn:dd.MM.yyyy}: {AnwesenheitAnzeige.Status(status)}"
            + (status == Anwesenheitsstatus.Anwesend ? $", {min} Min. ({AnwesenheitAnzeige.Ue(min)} UE)" : "")
            + (vorhanden is null ? "" : $" (vorher: {AnwesenheitAnzeige.Status(vorhanden.Status)})")
            + (string.IsNullOrWhiteSpace(bemerkung) ? "" : $" · Grund: {bemerkung.Trim()}"),
            "Person", b.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Kontingent: Anwesend verbraucht die Einheit, nicht erschienen nach Kursregel, entschuldigt gibt sie zurück. Einmal verbucht, ist es endgültig.</summary>
    private static Task KontingentBuchenAsync(Belegung b, Kurs kurs, Veranstaltung v, Anwesenheitsstatus status, Anwesenheit? vorhanden, DateTimeOffset jetzt,
        string von, CancellationToken abbruch)
    {
        _ = abbruch;
        if (!b.IstKontingent || status == Anwesenheitsstatus.Unbestaetigt)
        {
            return Task.CompletedTask;
        }

        var verbucht = b.Ereignisse.Any(e => e.VeranstaltungId == v.Id && e.Art is Kontingentereignisart.Besucht or Kontingentereignisart.NichtErschienen
            or Kontingentereignisart.NichtErschienenOhneAnrechnung);
        if (verbucht)
        {
            if (vorhanden is not null && vorhanden.Status != status)
            {
                throw new InvalidOperationException("Diese Veranstaltung ist im Kontingent bereits verbucht (Ereignisse sind unveränderlich). Eine Korrektur macht die Verwaltung über eine Gutschrift.");
            }

            return Task.CompletedTask;
        }

        if (status == Anwesenheitsstatus.Anwesend)
        {
            var pruefung = Checkinregel.Pruefen(b, kurs, v, null, false, jetzt, zeitfensterPruefen: false);
            if (!pruefung.Ok)
            {
                throw new InvalidOperationException(pruefung.Text);
            }

            b.AnwesenheitFesthalten(v.Id, true, kurs.Richtlinie, jetzt, von);
        }
        else if (b.Einbuchungen.ContainsKey(v.Id))
        {
            if (status == Anwesenheitsstatus.Entschuldigt)
            {
                b.Austragen(v.Id, v.Beginn, kurs.Richtlinie, jetzt, von, fristIgnorieren: true);
            }
            else
            {
                b.AnwesenheitFesthalten(v.Id, false, kurs.Richtlinie, jetzt, von);
            }
        }

        return Task.CompletedTask;
    }

    // ---------- Check-in (ein Scan) ----------

    /// <summary>
    /// Weg A, primär: Teilnehmende öffnen ihren persönlichen Link, tippen „Einchecken“, die Kamera liest den Termin-QR;
    /// Personen-Token und Termin-Code gehen zusammen an den Server.
    /// </summary>
    public async Task<CheckinAntwort> LinkCheckinAsync(string linkGeheimnis, string terminInhalt, CancellationToken abbruch = default)
    {
        var personId = await zugang.PersonZumLinkAsync(linkGeheimnis, abbruch);
        if (personId is null)
        {
            return new(new Checkinergebnis(Checkincode.NichtAngemeldet, "Ihr persönlicher Link ist ungültig oder wurde gesperrt. Bitte beim Teilnehmerservice melden."), null, null);
        }

        var pruefung = await termincodes.PruefenAsync(Termincodes.AusScan(terminInhalt), abbruch: abbruch);
        return await SelbstCheckinAsync(pruefung, personId.Value, abbruch);
    }

    /// <summary>
    /// Weg A, Komfort: Termin-QR mit der normalen Kamera-App gescannt; das Gerät kennt die Person über das gemerkte Geräte-Token (Cookie).
    /// Mehrere gemerkte Personen (Familie): Auswahl. Ohne gemerkte Person: Hinweis auf den persönlichen Link.
    /// </summary>
    public async Task<RaumCheckinAntwort> RaumCheckinAsync(string code, IReadOnlyList<string> geraete, Guid? gewaehlt = null, CancellationToken abbruch = default)
    {
        var pruefung = await termincodes.PruefenAsync(code, abbruch: abbruch);
        if (!pruefung.Gueltig)
        {
            return new(RaumCheckinStatus.TokenUngueltig, pruefung.Text, null, [], null);
        }

        var liste = await ListeAsync(pruefung.VeranstaltungId!.Value, abbruch);
        if (liste is null)
        {
            return new(RaumCheckinStatus.TokenUngueltig, "Veranstaltung nicht gefunden.", null, [], null);
        }

        var kurs = $"{liste.Kurs.Kursnummer} {liste.Kurs.Titel}";
        var gemerkt = await zugang.GemerktePersonenAsync(geraete, abbruch);
        if (gemerkt.Count == 0)
        {
            return new(RaumCheckinStatus.KeinGeraet,
                "Dieses Gerät kennt Sie noch nicht. Bitte öffnen Sie den persönlichen Check-in-Link aus Ihrer Anmeldebestätigung oder melden Sie sich bei der Kursleitung.",
                kurs, [], null);
        }

        var namen = await personen.AnzeigenamenAsync(gemerkt.Select(p => p.PersonId), abbruch);
        var auswahl = gemerkt.Select(p => (p.PersonId, namen.GetValueOrDefault(p.PersonId) ?? "?")).ToList();
        var person = gewaehlt is { } g ? gemerkt.FirstOrDefault(p => p.PersonId == g) : gemerkt.Count == 1 ? gemerkt[0] : null;
        if (person is null)
        {
            return new(RaumCheckinStatus.AuswahlNoetig, "Wer checkt ein?", kurs, auswahl, null);
        }

        await zugang.GenutztAsync(person.Geheimnis, abbruch);
        var antwort = await SelbstCheckinAsync(pruefung, person.PersonId, abbruch);
        return new(antwort.Ergebnis.Ok ? RaumCheckinStatus.Eingecheckt : RaumCheckinStatus.Abgelehnt, antwort.Ergebnis.Text, kurs, auswahl, antwort);
    }

    private async Task<CheckinAntwort> SelbstCheckinAsync(Tokenpruefung pruefung, Guid personId, CancellationToken abbruch)
    {
        var name = (await personen.AnzeigenamenAsync([personId], abbruch)).GetValueOrDefault(personId);
        if (!pruefung.Gueltig)
        {
            await speicher.SpeichernAsync(abbruch);
            return new(new Checkinergebnis(Checkincode.ZuSpaet, pruefung.Text), null, name);
        }

        var liste = await ListeAsync(pruefung.VeranstaltungId!.Value, abbruch);
        if (liste is null)
        {
            return new(new Checkinergebnis(Checkincode.NichtAngemeldet, "Veranstaltung nicht gefunden."), null, name);
        }

        var kurs = $"{liste.Kurs.Kursnummer} {liste.Kurs.Titel}";
        if (liste.Kurs.Richtlinie.SelbstCheckin == SelbstCheckin.Aus)
        {
            return new(new Checkinergebnis(Checkincode.NichtAngemeldet, "In diesem Kurs erfasst die Kursleitung die Anwesenheit. Bitte zeigen Sie Ihren QR-Ausweis vor."), kurs, name);
        }

        var b = (await anmeldungen.BelegungenDesKursesAsync(liste.Kurs.Id, abbruch)).Where(x => x.PersonId == personId).OrderBy(x => x.Status).FirstOrDefault();
        if (b is null)
        {
            await speicher.SpeichernAsync(abbruch);
            return new(new Checkinergebnis(Checkincode.NichtAngemeldet, $"{name} ist in diesem Kurs nicht angemeldet. Bitte am Tresen oder bei der Kursleitung melden."), kurs, name);
        }

        var mitBestaetigung = liste.Kurs.Richtlinie.SelbstCheckin == SelbstCheckin.MitBestaetigung;
        return await CheckinAsync(liste, b, pruefung.OrtId, Erfassungsweg.RaumQr, "Selbst-Check-in (Termin-QR)", abbruch, mitBestaetigung);
    }

    /// <summary>Weg B: Kursleitung oder Verwaltung scannt den Teilnehmerausweis (Pseudonym-Token).</summary>
    public async Task<CheckinAntwort> AusweisCheckinAsync(Guid veranstaltungId, string scaninhalt, CancellationToken abbruch = default)
    {
        var liste = await ListeAsync(veranstaltungId, abbruch) ?? throw new InvalidOperationException("Veranstaltung nicht gefunden.");
        if (await ZugriffVerweigertAsync(liste.Kurs, liste.Veranstaltung, abbruch) is { } grund)
        {
            throw new InvalidOperationException(grund);
        }

        if (await zugang.AusweisLesenAsync(scaninhalt, abbruch) is not { } personId)
        {
            return new(new Checkinergebnis(Checkincode.NichtAngemeldet, "Kein gültiger Teilnehmerausweis erkannt (ggf. gesperrt)."), null, null);
        }

        var b = (await anmeldungen.BelegungenDesKursesAsync(liste.Kurs.Id, abbruch)).Where(x => x.PersonId == personId).OrderBy(x => x.Status).FirstOrDefault();
        var name = (await personen.AnzeigenamenAsync([personId], abbruch)).GetValueOrDefault(personId);
        if (b is null)
        {
            return new(new Checkinergebnis(Checkincode.NichtAngemeldet, $"{name} ist in diesem Kurs nicht angemeldet."), liste.Kurs.Kursnummer, name);
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        return await CheckinAsync(liste, b, null, Erfassungsweg.Ausweis, wer.Anzeige, abbruch);
    }

    /// <summary>Check-in Weg B für eine bereits erkannte Person (z. B. nach dem Sichtvergleich im Integrationskurs).</summary>
    public async Task<CheckinAntwort> AusweisCheckinPersonAsync(Guid veranstaltungId, Guid personId, CancellationToken abbruch = default)
    {
        var liste = await ListeAsync(veranstaltungId, abbruch) ?? throw new InvalidOperationException("Veranstaltung nicht gefunden.");
        if (await ZugriffVerweigertAsync(liste.Kurs, liste.Veranstaltung, abbruch) is { } grund)
        {
            throw new InvalidOperationException(grund);
        }

        var b = (await anmeldungen.BelegungenDesKursesAsync(liste.Kurs.Id, abbruch)).Where(x => x.PersonId == personId).OrderBy(x => x.Status).FirstOrDefault();
        if (b is null)
        {
            var name = (await personen.AnzeigenamenAsync([personId], abbruch)).GetValueOrDefault(personId);
            return new(new Checkinergebnis(Checkincode.NichtAngemeldet, $"{name} ist in diesem Kurs nicht angemeldet."), liste.Kurs.Kursnummer, name);
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        return await CheckinAsync(liste, b, null, Erfassungsweg.Ausweis, wer.Anzeige, abbruch);
    }

    private async Task<CheckinAntwort> CheckinAsync(Anwesenheitsliste liste, Belegung b, Guid? ortId, Erfassungsweg weg, string von, CancellationToken abbruch,
        bool nurVormerken = false)
    {
        var (kurs, v) = (liste.Kurs, liste.Veranstaltung);
        var vorhanden = (await speicher.AnwesenheitenAsync(v.Id, abbruch)).FirstOrDefault(a => a.BelegungId == b.Id);
        var name = (await personen.AnzeigenamenAsync([b.PersonId], abbruch)).GetValueOrDefault(b.PersonId);
        var jetzt = Jetzt;
        var bereits = vorhanden?.Status is Anwesenheitsstatus.Anwesend or Anwesenheitsstatus.Unbestaetigt;
        var ergebnis = Checkinregel.Pruefen(b, kurs, v, ortId, bereits, jetzt);
        if (!ergebnis.Ok)
        {
            if (ergebnis.Code != Checkincode.BereitsEingecheckt)
            {
                await protokoll.ProtokollierenAsync("Anwesenheit.CheckinAbgelehnt", $"Check-in von {Pseudonym.Person(b.PersonId)} in {kurs.Kursnummer} abgelehnt: {ergebnis.Code}",
                    "Person", b.PersonId, erfolgreich: false, abbruch: abbruch);
            }

            await speicher.SpeichernAsync(abbruch);
            return new(ergebnis, $"{kurs.Kursnummer} {kurs.Titel}", name);
        }

        var status = nurVormerken ? Anwesenheitsstatus.Unbestaetigt : Anwesenheitsstatus.Anwesend;
        await KontingentBuchenAsync(b, kurs, v, status, vorhanden, jetzt, von, abbruch);
        if (vorhanden is null)
        {
            speicher.Hinzufuegen(new Anwesenheit(b, v, ortId ?? v.Orte.FirstOrDefault()?.OrtId, status, liste.Dauer, weg, von, jetzt));
        }
        else
        {
            vorhanden.Aendern(ortId, status, liste.Dauer, weg, von, jetzt, null, liste.Dauer);
        }

        await protokoll.ProtokollierenAsync("Anwesenheit.Checkin",
            $"{Pseudonym.Person(b.PersonId)} in {kurs.Kursnummer} am {v.Beginn:dd.MM.yyyy} {(nurVormerken ? "selbst eingecheckt (Bestätigung durch Kursleitung offen)" : "eingecheckt")} ({AnwesenheitAnzeige.Weg(weg)})",
            "Person", b.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new(nurVormerken ? new Checkinergebnis(Checkincode.Ok, "Vorgemerkt – die Kursleitung bestätigt Ihre Anwesenheit.") : ergebnis, $"{kurs.Kursnummer} {kurs.Titel}", name);
    }
    // ---------- Abschluss ----------

    /// <summary>Liste abschließen: Wer nicht erfasst ist, gilt als nicht anwesend; bei Kontingenten greift die Nichterscheinen-Regel.</summary>
    public async Task<int> AbschliessenAsync(Guid veranstaltungId, CancellationToken abbruch = default)
    {
        var liste = await ListeAsync(veranstaltungId, abbruch) ?? throw new InvalidOperationException("Veranstaltung nicht gefunden.");
        var (kurs, v) = (liste.Kurs, liste.Veranstaltung);
        if (await ZugriffVerweigertAsync(kurs, v, abbruch) is { } grund)
        {
            throw new InvalidOperationException(grund);
        }

        if (liste.Abschluss is not null)
        {
            throw new InvalidOperationException($"Die Liste wurde am {liste.Abschluss.Am.ToLocalTime():dd.MM.yyyy HH:mm} von {liste.Abschluss.Von} abgeschlossen.");
        }

        var jetzt = Jetzt;
        if (jetzt.LocalDateTime < v.Beginn)
        {
            throw new InvalidOperationException($"Die Liste kann erst ab Beginn der Veranstaltung ({v.Beginn:dd.MM. HH:mm} Uhr) abgeschlossen werden.");
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        var anzahl = 0;
        foreach (var z in liste.Zeilen.Where(z => z.Anwesenheit?.Status == Anwesenheitsstatus.Unbestaetigt))
        {
            await KontingentBuchenAsync(z.Belegung, kurs, v, Anwesenheitsstatus.Abwesend, null, jetzt, wer.Anzeige, abbruch);
            z.Anwesenheit!.Aendern(null, Anwesenheitsstatus.Abwesend, 0, Erfassungsweg.Automatik, wer.Anzeige, jetzt, "Selbst-Check-in nicht bestätigt", liste.Dauer);
            anzahl++;
        }

        foreach (var z in liste.Zeilen.Where(z => z.Anwesenheit is null && z.Belegung.Status == Belegungsstatus.Angemeldet && z.Belegung.GebuchtAm.LocalDateTime < v.Ende))
        {
            if (z.Belegung.IstKontingent && !z.Eingebucht)
            {
                continue; // ohne Einbuchung kein Termin
            }

            await KontingentBuchenAsync(z.Belegung, kurs, v, Anwesenheitsstatus.Abwesend, null, jetzt, wer.Anzeige, abbruch);
            speicher.Hinzufuegen(new Anwesenheit(z.Belegung, v, null, Anwesenheitsstatus.Abwesend, 0, Erfassungsweg.Automatik, wer.Anzeige, jetzt, "beim Abschluss nicht erfasst"));
            anzahl++;
        }

        speicher.Hinzufuegen(new Listenabschluss(v.Id, kurs.Id, jetzt, wer.Anzeige));
        await protokoll.ProtokollierenAsync("Anwesenheit.Abgeschlossen",
            $"Anwesenheitsliste {kurs.Kursnummer} am {v.Beginn:dd.MM.yyyy} abgeschlossen: {liste.Anwesend} anwesend, {anzahl} als nicht anwesend vermerkt"
            + (kurs.Richtlinie.Belegung == Belegungsart.Kontingent ? $" (Nichterscheinen {(kurs.Richtlinie.NichterscheinenZaehlt ? "verbraucht die Einheit" : "wird nicht angerechnet")})" : ""),
            "Kurs", kurs.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return anzahl;
    }

    /// <summary>Automatik (nachts): Listen vergangener Veranstaltungen abschließen, die noch offen sind.</summary>
    public async Task<int> OffeneListenAbschliessenAsync(CancellationToken abbruch = default)
    {
        var jetzt = Jetzt.LocalDateTime;
        var n = 0;
        foreach (var (vid, _) in await speicher.OffeneListenAsync(jetzt.AddHours(-12), jetzt.AddDays(-30), abbruch))
        {
            try
            {
                await AbschliessenAsync(vid, abbruch);
                n++;
            }
            catch (InvalidOperationException)
            {
                // bereits abgeschlossen oder nicht abschließbar: überspringen
            }
        }

        return n;
    }

    /// <summary>Anwesenheiten einer Belegung (für Belegungsseite und Bescheinigung).</summary>
    public async Task<IReadOnlyList<Anwesenheit>> AnwesenheitenDerBelegungAsync(Belegung b, CancellationToken abbruch = default)
        => (await speicher.AnwesenheitenDesKursesAsync(b.KursId, abbruch)).Where(a => a.BelegungId == b.Id).ToList();

    private async Task<Guid?> EigenePersonAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        return wer.IstAutomatik ? null : (await konten.FindenAsync(wer.Kennung, abbruch))?.PersonId;
    }
}
