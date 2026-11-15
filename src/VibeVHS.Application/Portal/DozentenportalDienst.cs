using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Portal;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Portal;

/// <summary>
/// Dozierendenportal: Kursleitungen sehen und bearbeiten nur ihre eigenen Kurse und Termine (Team oder Leitung eines Termins).
/// Alles, was die Verwaltung entscheidet (Ausfall, Bankverbindung, Nachweise, Vorschläge, Einwände), wird zum Anliegen mit Aufgabe.
/// </summary>
public sealed class DozentenportalDienst(
    IAktuellerBenutzer benutzer,
    IBenutzerkonten konten,
    IPortalSpeicher speicher,
    IProgrammSpeicher programm,
    IAnmeldungSpeicher anmeldungen,
    IDurchfuehrungSpeicher durchfuehrung,
    IPersonenSpeicher personen,
    IZugangstokenSpeicher tokens,
    IFinanzSpeicher finanzen,
    IDokumentSpeicher dokumente,
    IStammdaten stammdaten,
    IVirenpruefung viren,
    PostausgangDienst post,
    PortalVerwaltung verwaltung,
    OeffentlicheAdresse adresse,
    IAuditProtokoll protokoll,
    TimeProvider uhr)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>So lange bleiben Unterlagen nach dem letzten Termin abrufbar.</summary>
    public static readonly TimeSpan UnterlagenNachKursende = TimeSpan.FromDays(60);

    /// <summary>Gültigkeit des Anmeldelinks per Mail.</summary>
    public static readonly TimeSpan PortalLinkGueltig = TimeSpan.FromMinutes(15);

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    // ---------- Wer bin ich? ----------

    /// <summary>Person der angemeldeten Kursleitung; sonst 403.</summary>
    public async Task<Guid> IchAsync(CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (wer.Rolle != Rolle.Dozierende)
        {
            throw new PortalFehler("Das Dozierendenportal ist für Kursleitungen (Rolle „Dozierende“).", 403);
        }

        return (await konten.FindenAsync(wer.Kennung, abbruch))?.PersonId
               ?? throw new PortalFehler("Ihr Konto ist mit keiner Person verknüpft. Bitte wenden Sie sich an die Verwaltung.", 403);
    }

    public async Task<string> NameAsync(CancellationToken abbruch = default) => (await benutzer.ErmittelnAsync(abbruch)).Name;

    // ---------- Anmeldung per Link (Magic Link) ----------

    /// <summary>
    /// Anmeldelink anfordern. Antwortet immer gleich (keine Auskunft, ob es die Adresse gibt). Nur aktive Personen mit Rolle Dozierende;
    /// fehlt das Benutzerkonto, wird es mit der Rolle „Dozierende“ angelegt.
    /// </summary>
    public async Task LinkAnfordernAsync(string eMail, CancellationToken abbruch = default)
    {
        var mail = (eMail ?? "").Trim();
        if (mail.Length < 5 || !mail.Contains('@', StringComparison.Ordinal))
        {
            return;
        }

        var treffer = (await speicher.PersonenMitEMailAsync(mail, abbruch))
            .Where(p => p.Datenschutz == Datenschutzstatus.Aktiv && p.Rollen.HasFlag(Personenrolle.Dozierende)).ToList();
        if (treffer.Count != 1)
        {
            await protokoll.ProtokollierenAsync("Portal.LinkAngefordert", $"Anmeldelink Dozierendenportal angefordert – {(treffer.Count == 0 ? "keine passende Kursleitung" : "Adresse nicht eindeutig")}, nichts verschickt",
                erfolgreich: false, abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
            return;
        }

        var person = treffer[0];
        var (token, geheimnis) = Zugangstoken.Erzeugen(person.Id, Tokenart.PortalLink, uhr.GetUtcNow());
        tokens.Hinzufuegen(token);
        var name = person.AktuellerName(uhr.GetLocalNow());
        await post.KundenmailEinreihenAsync(mail, person.Anzeigename, "Ihr Anmeldelink zum Dozierendenportal",
            $"Guten Tag {name.Vorname} {name.Nachname},\n\nmit diesem Link melden Sie sich im Dozierendenportal an (gültig {PortalLinkGueltig.TotalMinutes:0} Minuten, nur einmal):\n"
            + $"{adresse.Link($"portal/dozierende/link/{geheimnis}")}\n\nWenn Sie den Link nicht angefordert haben, ignorieren Sie diese Mail.", person.Sprache, person.Id, abbruch);
        await protokoll.ProtokollierenAsync("Portal.LinkAngefordert", $"Anmeldelink Dozierendenportal an {Pseudonym.Person(person.Id)} verschickt", nameof(Person), person.Id,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Link geöffnet: einmalig, 15 Minuten. Liefert das Benutzerkonto (wird bei Bedarf angelegt) oder null.</summary>
    public async Task<Benutzerkonto?> LinkEinloesenAsync(string geheimnis, CancellationToken abbruch = default)
    {
        var t = await tokens.ZumHashAsync(Zugangstoken.HashVon(geheimnis ?? ""), abbruch);
        var jetzt = uhr.GetUtcNow();
        if (t is not { Art: Tokenart.PortalLink, Gueltig: true } || jetzt - t.ErstelltAm > PortalLinkGueltig)
        {
            return null;
        }

        t.Widerrufen(jetzt, "eingelöst");
        var konto = await speicher.KontoZurPersonAsync(t.PersonId, abbruch);
        if (konto is null)
        {
            var person = await personen.LadenAsync(t.PersonId, abbruch);
            if (person is null)
            {
                return null;
            }

            konto = new Benutzerkonto($"doz-{Pseudonym.Kurz(person.Id).ToLowerInvariant()}", person.Anzeigename, "Kursleitung (Portal)", [Rolle.Dozierende], [], person.Id,
                "Angelegt beim ersten Anmeldelink des Dozierendenportals");
            speicher.Hinzufuegen(konto);
        }
        else if (!konto.Rollen.Contains(Rolle.Dozierende))
        {
            // Portal-Konto einer Teilnehmerin, die inzwischen auch Kurse leitet: Zugang zum Dozierendenportal ergänzen.
            if (!konto.IstPortalkonto || await personen.LadenAsync(t.PersonId, abbruch) is not { } p || !p.Rollen.HasFlag(Personenrolle.Dozierende))
            {
                return null;
            }

            konto.Aendern(konto.Name, konto.Funktion, [.. konto.Rollen, Rolle.Dozierende], konto.Fachbereiche);
        }

        await protokoll.ProtokollierenAsync("Portal.Angemeldet", $"Anmeldung Dozierendenportal per Link: {Pseudonym.Person(t.PersonId)}", nameof(Person), t.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return konto;
    }

    // ---------- Kurse und Termine ----------

    public async Task<IReadOnlyList<PortalKurs>> KurseAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var kurse = await EigeneKurseAsync(ich, abbruch);
        var zahlen = await anmeldungen.BelegungszahlenAsync(kurse.Select(k => k.Id), abbruch);
        return kurse.Select(k =>
        {
            var t = k.Veranstaltungen.Where(v => !v.Ausgefallen).OrderBy(v => v.Beginn).ToList();
            return new PortalKurs(k.Id, k.Kursnummer, k.Titel, k.Fachbereich, t.Count, zahlen.GetValueOrDefault(k.Id).Belegt, t.FirstOrDefault()?.Beginn, t.LastOrDefault()?.Ende,
                k.Leitung?.PersonId == ich);
        }).OrderBy(k => k.Ende < uhr.GetLocalNow().DateTime ? 1 : 0).ThenBy(k => k.Beginn).ToList();
    }

    /// <summary>Eigene Termine im Zeitraum (Standard: heute bis in 4 Wochen); Raum- und Zeitänderungen hervorgehoben.</summary>
    public async Task<IReadOnlyList<PortalTermin>> TermineAsync(DateOnly? von = null, DateOnly? bis = null, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var start = (von ?? Heute).ToDateTime(TimeOnly.MinValue);
        var ende = (bis ?? Heute.AddDays(28)).ToDateTime(TimeOnly.MaxValue);
        var kurse = await EigeneKurseAsync(ich, abbruch);
        var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
        var termine = kurse.SelectMany(k => k.Veranstaltungen.Where(v => v.Beginn >= start && v.Beginn <= ende && Eigen(k, v, ich)).Select(v => (Kurs: k, V: v))).ToList();
        var zu = await durchfuehrung.AbgeschlosseneAsync(termine.Select(t => t.V.Id), abbruch);
        var liste = new List<PortalTermin>();
        foreach (var (k, v) in termine.OrderBy(t => t.V.Beginn))
        {
            var ort = v.Orte.Select(o => verzeichnis.Finden(o.OrtId)).OfType<Ort>().FirstOrDefault();
            var belegungen = await anmeldungen.BelegungenDesKursesAsync(k.Id, abbruch);
            liste.Add(new PortalTermin(v.Id, k.Id, k.Kursnummer, k.Titel, v.Nummer, v.Beginn, v.Ende, string.Join(" + ", v.Orte.Select(o => o.OrtName)),
                ort is null ? null : verzeichnis.Wegbeschreibung(ort), v.Ausgefallen, Aenderung(k, v), zu.Contains(v.Id), Teilnehmende(k, v, belegungen).Count));
        }

        return liste;
    }

    public async Task<(Kurs Kurs, Veranstaltung Termin)> TerminAsync(Guid veranstaltungId, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        foreach (var k in await EigeneKurseAsync(ich, abbruch))
        {
            if (k.Veranstaltungen.FirstOrDefault(v => v.Id == veranstaltungId) is { } v && Eigen(k, v, ich))
            {
                return (k, v);
            }
        }

        throw new PortalFehler("Dieser Termin gehört nicht zu Ihren Kursen.", 403);
    }

    public async Task<Kurs> KursAsync(Guid kursId, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        return (await EigeneKurseAsync(ich, abbruch)).FirstOrDefault(k => k.Id == kursId)
               ?? throw new PortalFehler("Dieser Kurs gehört nicht zu Ihren Kursen.", 403);
    }

    /// <summary>Teilnehmerliste eines Termins, datensparsam: nur Name und Status (keine Kontaktdaten).</summary>
    public async Task<IReadOnlyList<(string Name, string Status)>> TeilnehmendeAsync(Guid veranstaltungId, CancellationToken abbruch = default)
    {
        var (k, v) = await TerminAsync(veranstaltungId, abbruch);
        var liste = Teilnehmende(k, v, await anmeldungen.BelegungenDesKursesAsync(k.Id, abbruch));
        var namen = await personen.AnzeigenamenAsync(liste.Select(b => b.PersonId), abbruch);
        return liste.Select(b => (namen.GetValueOrDefault(b.PersonId) ?? Pseudonym.Person(b.PersonId), BelegungAnzeige.Status(b.Status)))
            .OrderBy(x => x.Item1, StringComparer.CurrentCulture).ToList();
    }

    // ---------- Kalender ----------

    /// <summary>Neue Kalender-Abo-Adresse; frühere werden widerrufen.</summary>
    public async Task<Uri> KalenderLinkErzeugenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var jetzt = uhr.GetUtcNow();
        foreach (var alt in (await tokens.DerPersonAsync(ich, abbruch)).Where(t => t is { Art: Tokenart.Kalender, Gueltig: true }))
        {
            alt.Widerrufen(jetzt, "neue Kalenderadresse erzeugt");
        }

        var (token, geheimnis) = Zugangstoken.Erzeugen(ich, Tokenart.Kalender, jetzt);
        tokens.Hinzufuegen(token);
        await protokoll.ProtokollierenAsync("Portal.Kalender", $"Kalender-Abo für {Pseudonym.Person(ich)} erzeugt (frühere Adressen gesperrt)", nameof(Person), ich, abbruch: abbruch);
        await tokens.SpeichernAsync(abbruch);
        return adresse.Link($"portal/dozierende/kalender/{geheimnis}.ics");
    }

    public async Task<bool> KalenderAktivAsync(CancellationToken abbruch = default)
        => (await tokens.DerPersonAsync(await IchAsync(abbruch), abbruch)).Any(t => t is { Art: Tokenart.Kalender, Gueltig: true });

    public async Task KalenderSperrenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        foreach (var t in (await tokens.DerPersonAsync(ich, abbruch)).Where(t => t is { Art: Tokenart.Kalender, Gueltig: true }))
        {
            t.Widerrufen(uhr.GetUtcNow(), "von der Kursleitung gesperrt");
        }

        await protokoll.ProtokollierenAsync("Portal.Kalender", $"Kalender-Abo von {Pseudonym.Person(ich)} gesperrt", nameof(Person), ich, abbruch: abbruch);
        await tokens.SpeichernAsync(abbruch);
    }

    /// <summary>ICS fürs Kalender-Abo (ohne Anmeldung, über die geheime Adresse): letzte 30 Tage bis 1 Jahr voraus.</summary>
    public async Task<string?> KalenderAsync(string geheimnis, CancellationToken abbruch = default)
    {
        var t = await tokens.ZumHashAsync(Zugangstoken.HashVon(geheimnis ?? ""), abbruch);
        if (t is not { Art: Tokenart.Kalender, Gueltig: true })
        {
            return null;
        }

        t.Genutzt(uhr.GetUtcNow());
        await tokens.SpeichernAsync(abbruch);
        var kurse = await EigeneKurseAsync(t.PersonId, abbruch);
        var von = Heute.AddDays(-30).ToDateTime(TimeOnly.MinValue);
        var bis = Heute.AddYears(1).ToDateTime(TimeOnly.MinValue);
        var name = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "vhs";
        return Kalenderdatei.Erzeugen($"Meine Kurse – {name}",
            kurse.SelectMany(k => k.Veranstaltungen.Where(v => v.Beginn >= von && v.Beginn <= bis && Eigen(k, v, t.PersonId)).Select(v => (k, v, Aenderung(k, v)))),
            name, uhr.GetUtcNow());
    }

    public async Task<(string Datei, string Inhalt)> TerminIcsAsync(Guid veranstaltungId, CancellationToken abbruch = default)
    {
        var (k, v) = await TerminAsync(veranstaltungId, abbruch);
        var name = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "vhs";
        return ($"{k.Kursnummer}-termin-{v.Nummer}.ics", Kalenderdatei.Erzeugen($"{k.Kursnummer} Termin {v.Nummer}", [(k, v, Aenderung(k, v))], name, uhr.GetUtcNow()));
    }

    // ---------- Unterlagen ----------

    public async Task<IReadOnlyList<PortalUnterlage>> UnterlagenAsync(Guid kursId, CancellationToken abbruch = default)
    {
        await KursAsync(kursId, abbruch);
        var liste = new List<PortalUnterlage>();
        foreach (var u in (await speicher.UnterlagenAsync(kursId, abbruch)).OrderByDescending(u => u.HochgeladenAm))
        {
            var d = await speicher.DateiAsync(u.DateiId, abbruch);
            liste.Add(new PortalUnterlage(u.Id, u.Titel, d?.Name ?? "?", d?.Groesse ?? 0, u.HochgeladenAm, u.Abrufe, u.ZurueckgezogenAm is not null));
        }

        return liste;
    }

    /// <summary>Unterlage hochladen (Typ, Größe, Signatur, Virenprüfung) und optional sofort an alle angemeldeten Teilnehmenden verteilen.</summary>
    public async Task<int> UnterlageHochladenAsync(Guid kursId, string titel, string dateiname, byte[] inhalt, bool verteilen, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var kurs = await KursAsync(kursId, abbruch);
        var typ = Uploadregeln.Pruefen(dateiname, inhalt);
        if (await viren.PruefenAsync(dateiname, inhalt, abbruch) is { } befund)
        {
            await protokoll.ProtokollierenAsync("Portal.UploadAbgelehnt", $"Upload für {kurs.Kursnummer} abgelehnt (Virenprüfung: {befund})", nameof(Kurs), kurs.Id,
                erfolgreich: false, abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
            throw new PortalFehler($"Die Datei wurde nicht angenommen: {befund}");
        }

        var jetzt = uhr.GetUtcNow();
        var datei = new Datei(dateiname, typ, inhalt, jetzt);
        var token = Zufall();
        var ende = kurs.Veranstaltungen.Count == 0 ? uhr.GetLocalNow() : new DateTimeOffset(kurs.Veranstaltungen.Max(v => v.Ende), uhr.GetLocalNow().Offset);
        var u = new Kursunterlage(kurs.Id, datei.Id, string.IsNullOrWhiteSpace(titel) ? Path.GetFileNameWithoutExtension(dateiname) : titel, ich, token, jetzt,
            Max(ende, uhr.GetLocalNow()) + UnterlagenNachKursende);
        speicher.Hinzufuegen(datei);
        speicher.Hinzufuegen(u);
        var anzahl = 0;
        if (verteilen)
        {
            var link = adresse.Link($"u/{token}");
            foreach (var b in (await anmeldungen.BelegungenDesKursesAsync(kurs.Id, abbruch)).Where(b => b.Status == Belegungsstatus.Angemeldet))
            {
                anzahl += await MailAsync(b.PersonId, $"Neue Unterlage: {u.Titel}",
                    $"für Ihren Kurs „{kurs.Titel}“ ({kurs.Kursnummer}) hat die Kursleitung eine Unterlage bereitgestellt: {u.Titel}\n{link}\n\n"
                    + $"Der Link ist bis {u.GueltigBis.ToLocalTime():dd.MM.yyyy} abrufbar. Bitte nicht weitergeben.", abbruch) ? 1 : 0;
            }
        }

        await protokoll.ProtokollierenAsync("Portal.Unterlage", $"Unterlage „{u.Titel}“ ({datei.Name}, {datei.Groesse / 1024} KB, SHA-256 {datei.Sha256[..12]}…) für {kurs.Kursnummer} hochgeladen"
            + (verteilen ? $", an {anzahl} Teilnehmende verschickt" : ", noch nicht verteilt"), nameof(Kurs), kurs.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return anzahl;
    }

    public async Task UnterlageZurueckziehenAsync(Guid unterlageId, CancellationToken abbruch = default)
    {
        var u = await speicher.UnterlageAsync(unterlageId, abbruch) ?? throw new PortalFehler("Unterlage nicht gefunden.", 404);
        var kurs = await KursAsync(u.KursId, abbruch);
        u.Zurueckziehen(uhr.GetUtcNow());
        await protokoll.ProtokollierenAsync("Portal.Unterlage", $"Unterlage „{u.Titel}“ für {kurs.Kursnummer} zurückgezogen (Link gesperrt)", nameof(Kurs), kurs.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Abruf durch Teilnehmende über den Link (ohne Anmeldung).</summary>
    public async Task<Datei?> UnterlageAbrufenAsync(string token, CancellationToken abbruch = default)
    {
        var u = await speicher.UnterlageMitTokenAsync(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""))), abbruch);
        if (u is null || !u.Abrufbar(uhr.GetUtcNow()))
        {
            return null;
        }

        u.Abgerufen();
        await speicher.SpeichernAsync(abbruch);
        return await speicher.DateiAsync(u.DateiId, abbruch);
    }

    // ---------- Ausfall und Nachrichten ----------

    /// <summary>Ausfall melden (z. B. Krankheit): Aufgabe für die Fachbereichsleitung; optional sofort Nachricht an die Teilnehmenden des Termins.</summary>
    public async Task<int> AusfallMeldenAsync(Guid veranstaltungId, string grund, string? nachrichtAnTeilnehmende, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var (k, v) = await TerminAsync(veranstaltungId, abbruch);
        if (v.Ausgefallen)
        {
            throw new PortalFehler("Dieser Termin ist bereits abgesagt.");
        }

        if (v.Ende < uhr.GetLocalNow().DateTime)
        {
            throw new PortalFehler("Der Termin liegt in der Vergangenheit.");
        }

        if ((await speicher.AntraegeAsync(ich, true, abbruch)).Any(a => a.Art == Antragsart.Ausfall && a.BezugId == v.Id))
        {
            throw new PortalFehler("Für diesen Termin ist bereits ein Ausfall gemeldet.");
        }

        if (string.IsNullOrWhiteSpace(grund))
        {
            throw new PortalFehler("Bitte kurz den Grund angeben (z. B. Krankheit).");
        }

        await verwaltung.EinreichenAsync(new Portalantrag(ich, Antragsart.Ausfall, $"{k.Kursnummer} Termin {v.Nummer} am {v.Beginn:dd.MM.yyyy HH:mm}", grund,
            new Dictionary<string, string> { ["kurs"] = k.Kursnummer, ["termin"] = v.Beginn.ToString("dd.MM.yyyy HH:mm", De) }, v.Id, k.Fachbereich, null, uhr.GetUtcNow()), abbruch);
        return string.IsNullOrWhiteSpace(nachrichtAnTeilnehmende) ? 0 : await NachrichtAsync(veranstaltungId, $"Termin am {v.Beginn:dd.MM.yyyy}", nachrichtAnTeilnehmende, abbruch);
    }

    /// <summary>Nachricht an alle Teilnehmenden eines Termins (Postausgang, mit Übersetzungslink).</summary>
    public async Task<int> NachrichtAsync(Guid veranstaltungId, string betreff, string text, CancellationToken abbruch = default)
    {
        var (k, v) = await TerminAsync(veranstaltungId, abbruch);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000)
        {
            throw new PortalFehler("Bitte eine Nachricht mit höchstens 2000 Zeichen eingeben.");
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        var anzahl = 0;
        foreach (var b in Teilnehmende(k, v, await anmeldungen.BelegungenDesKursesAsync(k.Id, abbruch)))
        {
            anzahl += await MailAsync(b.PersonId, $"{k.Titel}: {betreff}".Trim(),
                $"Nachricht Ihrer Kursleitung zum Kurs „{k.Titel}“ ({k.Kursnummer}), Termin {v.Nummer} am {v.Beginn:dddd, dd.MM.yyyy, HH:mm} Uhr:\n\n{text.Trim()}\n\n{wer.Name}", abbruch) ? 1 : 0;
        }

        await protokoll.ProtokollierenAsync("Portal.Nachricht", $"Nachricht der Kursleitung an {anzahl} Teilnehmende von {k.Kursnummer}, Termin {v.Nummer} („{betreff}“)",
            nameof(Kurs), k.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return anzahl;
    }

    // ---------- Honorar ----------

    public async Task<IReadOnlyList<PortalAbrechnung>> AbrechnungenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var einwaende = (await speicher.AntraegeAsync(ich, true, abbruch)).Where(a => a.Art == Antragsart.Honorareinwand).ToList();
        return (await finanzen.BelegeAsync(ich, abbruch)).Where(b => b.Art == Belegart.Honorarabrechnung && b.PersonId == ich)
            .OrderByDescending(b => b.Belegdatum)
            .Select(b => new PortalAbrechnung(b.Id, b.Nummer, b.Belegdatum, b.BetragCent, b.Festgeschrieben, b.Storniert, b.DokumentId is not null,
                b.Positionen.Select(p => $"{p.Text}: {new Geld(p.BetragCent)}").ToList(), einwaende.FirstOrDefault(e => e.BezugId == b.Id)))
            .ToList();
    }

    /// <summary>Gutschrift (Honorarabrechnung) als PDF – nur eigene, nur festgeschriebene, Original aus dem Archiv.</summary>
    public async Task<(string Datei, byte[] Pdf)> AbrechnungPdfAsync(Guid belegId, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var b = await finanzen.BelegAsync(belegId, abbruch);
        if (b is null || b.PersonId != ich || b.Art != Belegart.Honorarabrechnung)
        {
            throw new PortalFehler("Diese Abrechnung gehört nicht zu Ihnen.", 403);
        }

        var dok = b.DokumentId is { } did ? await dokumente.LadenAsync(did, abbruch) : null;
        if (dok is null || !dok.Unveraendert)
        {
            throw new PortalFehler(b.Festgeschrieben ? "Das PDF ist nicht verfügbar; bitte bei der Buchhaltung melden." : "Entwürfe haben noch kein PDF – erst nach der Freigabe durch die Buchhaltung.", 409);
        }

        return ($"{b.Nummer ?? "entwurf"}.pdf", dok.Pdf);
    }

    public async Task EinwandAsync(Guid belegId, string text, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var b = await finanzen.BelegAsync(belegId, abbruch);
        if (b is null || b.PersonId != ich || b.Art != Belegart.Honorarabrechnung)
        {
            throw new PortalFehler("Diese Abrechnung gehört nicht zu Ihnen.", 403);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new PortalFehler("Bitte beschreiben Sie, was nicht stimmt.");
        }

        if ((await speicher.AntraegeAsync(ich, true, abbruch)).Any(a => a.Art == Antragsart.Honorareinwand && a.BezugId == belegId))
        {
            throw new PortalFehler("Zu dieser Abrechnung ist bereits ein Einwand offen.");
        }

        await verwaltung.EinreichenAsync(new Portalantrag(ich, Antragsart.Honorareinwand, $"Einwand zu {b.Nummer ?? "Entwurf"} vom {b.Belegdatum:dd.MM.yyyy} ({new Geld(b.BetragCent)})",
            text, new Dictionary<string, string>(), b.Id, null, null, uhr.GetUtcNow()), abbruch);
    }

    // ---------- Eigene Daten ----------

    public async Task<PortalDaten> DatenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var p = await personen.LadenAsync(ich, abbruch) ?? throw new PortalFehler("Person nicht gefunden.", 404);
        var stand = p.StandAm(Heute, uhr.GetLocalNow());
        var bank = await finanzen.BankverbindungAsync(ich, abbruch);
        return new PortalDaten(stand.NameMitTitel, p.EMail, stand.BriefAn?.Anzeige, stand.BriefAn?.Strasse, stand.BriefAn?.Postleitzahl, stand.BriefAn?.Ort,
            bank is null ? null : $"{bank.Kontoinhaber}, {Iban.Maskiert(bank.Iban)}",
            stand.Beschaeftigung is { } ba ? BeschaeftigungAnzeige.Text(ba) : "nicht erfasst",
            (stand.Qualifikationen ?? []).Select(q => (q.Bezeichnung, q.WertText, q.GueltigVon, q.GueltigBisEinschliesslich)).ToList(),
            (await speicher.AntraegeAsync(ich, false, abbruch)).OrderByDescending(a => a.EingereichtAm).Take(20).ToList());
    }

    /// <summary>Eigene Anschrift ändern (Umzug ab Datum): wirkt sofort, wird protokolliert und in der Datenhistorie geführt.</summary>
    public async Task AdresseAendernAsync(string strasse, string postleitzahl, string ort, DateOnly ab, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        if (string.IsNullOrWhiteSpace(strasse) || string.IsNullOrWhiteSpace(postleitzahl) || string.IsNullOrWhiteSpace(ort))
        {
            throw new PortalFehler("Bitte Straße, Postleitzahl und Ort angeben.");
        }

        var p = await personen.LadenAsync(ich, abbruch) ?? throw new PortalFehler("Person nicht gefunden.", 404);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var e = Erfassung.Aenderung(uhr.GetLocalNow(), "Änderung im Dozierendenportal", wer.Anzeige);
        var alt = p.StandAm(Heute, uhr.GetLocalNow()).Aufenthaltsort;
        if (alt is null)
        {
            var id = p.AdresseHinzufuegen(null, strasse.Trim(), postleitzahl.Trim(), ort.Trim(), ab, e);
            p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, id, ab, e);
        }
        else
        {
            p.UmzugAb(alt.AdressId, null, strasse.Trim(), postleitzahl.Trim(), ort.Trim(), ab, e);
        }

        await protokoll.ProtokollierenAsync("Portal.Adresse", $"{Pseudonym.Person(ich)} hat die Anschrift im Dozierendenportal ab {ab:dd.MM.yyyy} geändert", nameof(Person), ich,
            abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    /// <summary>Neue Bankverbindung beantragen: wirksam erst nach Freigabe durch die Buchhaltung (Vier-Augen).</summary>
    public async Task BankverbindungBeantragenAsync(string kontoinhaber, string iban, string? bic, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        if (string.IsNullOrWhiteSpace(kontoinhaber))
        {
            throw new PortalFehler("Bitte den Kontoinhaber angeben.");
        }

        if (Iban.Fehler(iban) is { } f)
        {
            throw new PortalFehler(f);
        }

        if ((await speicher.AntraegeAsync(ich, true, abbruch)).Any(a => a.Art == Antragsart.Bankverbindung))
        {
            throw new PortalFehler("Eine Änderung der Bankverbindung ist bereits beantragt und wartet auf die Buchhaltung.");
        }

        await verwaltung.EinreichenAsync(new Portalantrag(ich, Antragsart.Bankverbindung, $"Neue Bankverbindung {Iban.Maskiert(iban)}", "Änderung im Dozierendenportal beantragt",
            new Dictionary<string, string> { ["kontoinhaber"] = kontoinhaber.Trim(), ["iban"] = Iban.Normalisieren(iban), ["bic"] = bic?.Trim() ?? "" }, null, null, null,
            uhr.GetUtcNow()), abbruch);
    }

    /// <summary>Qualifikationsnachweis hochladen: die Fachbereichsleitung prüft und trägt die Qualifikation ein.</summary>
    public async Task NachweisHochladenAsync(string merkmal, string? wert, DateOnly? gueltigBis, string dateiname, byte[] inhalt, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var typ = Uploadregeln.Pruefen(dateiname, inhalt);
        if (await viren.PruefenAsync(dateiname, inhalt, abbruch) is { } befund)
        {
            throw new PortalFehler($"Die Datei wurde nicht angenommen: {befund}");
        }

        if (string.IsNullOrWhiteSpace(merkmal))
        {
            throw new PortalFehler("Bitte angeben, welche Qualifikation der Nachweis belegt.");
        }

        var datei = new Datei(dateiname, typ, inhalt, uhr.GetUtcNow());
        speicher.Hinzufuegen(datei);
        var fb = (await EigeneKurseAsync(ich, abbruch)).Select(k => (int?)k.Fachbereich).FirstOrDefault();
        await verwaltung.EinreichenAsync(new Portalantrag(ich, Antragsart.Qualifikationsnachweis, $"Nachweis: {merkmal}{(string.IsNullOrWhiteSpace(wert) ? "" : $" ({wert})")}",
            $"Hochgeladen: {datei.Name}", new Dictionary<string, string> { ["merkmal"] = merkmal, ["wert"] = wert ?? "", ["bis"] = gueltigBis?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "" },
            null, fb, datei.Id, uhr.GetUtcNow()), abbruch);
    }

    /// <summary>Verfügbarkeit und Wunschzeiten für ein kommendes Semester.</summary>
    public async Task VerfuegbarkeitAsync(string semester, IReadOnlyList<string> wochentage, string zeiten, string? hinweis, int? fachbereich, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        if (string.IsNullOrWhiteSpace(semester) || (wochentage.Count == 0 && string.IsNullOrWhiteSpace(zeiten)))
        {
            throw new PortalFehler("Bitte Semester und mindestens Wochentage oder Zeiten angeben.");
        }

        await verwaltung.EinreichenAsync(new Portalantrag(ich, Antragsart.Verfuegbarkeit, $"Verfügbarkeit {semester}: {string.Join(", ", wochentage)} {zeiten}".Trim(), hinweis ?? "",
            new Dictionary<string, string> { ["semester"] = semester, ["wochentage"] = string.Join(",", wochentage), ["zeiten"] = zeiten ?? "" }, null,
            fachbereich ?? (await EigeneKurseAsync(ich, abbruch)).Select(k => (int?)k.Fachbereich).FirstOrDefault(), null, uhr.GetUtcNow()), abbruch);
    }

    public async Task KursvorschlagAsync(string titel, string beschreibung, int fachbereich, string? umfang, string? zielgruppe, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        if (string.IsNullOrWhiteSpace(titel) || string.IsNullOrWhiteSpace(beschreibung))
        {
            throw new PortalFehler("Bitte Titel und Beschreibung angeben.");
        }

        await verwaltung.EinreichenAsync(new Portalantrag(ich, Antragsart.Kursvorschlag, $"Kursvorschlag: {titel.Trim()}", beschreibung,
            new Dictionary<string, string> { ["umfang"] = umfang ?? "", ["zielgruppe"] = zielgruppe ?? "" }, null, fachbereich, null, uhr.GetUtcNow()), abbruch);
    }

    public async Task<IReadOnlyList<(int Nr, string Name)>> FachbereicheAsync(CancellationToken abbruch = default)
        => (await stammdaten.FachbereicheAsync(abbruch)).Select(f => (f.Nr, f.Name)).ToList();

    // ---------- Hilfen ----------

    private async Task<IReadOnlyList<Kurs>> EigeneKurseAsync(Guid ich, CancellationToken abbruch)
        => await speicher.KurseDerPersonAsync(ich, abbruch);

    /// <summary>Eigener Termin: Leitung dieses Termins oder Mitglied im Team des Kurses.</summary>
    public static bool Eigen(Kurs k, Veranstaltung v, Guid ich) => v.DozentId == ich || k.Team.Any(t => t.PersonId == ich);

    /// <summary>Hinweis auf Ausfall, Raum- oder Zeitänderung eines Termins (für Portale und Kalender).</summary>
    public static string? Aenderung(Kurs k, Veranstaltung v)
    {
        if (v.Ausgefallen)
        {
            return "Termin entfällt" + (v.Bemerkung is { Length: > 0 } b ? $": {b}" : ".");
        }

        if (!v.Ausnahme)
        {
            return null;
        }

        var regel = k.Serie;
        var teile = new List<string>();
        if (regel is not null && regel.OrtIds.Count > 0 && !v.Orte.Select(o => o.OrtId).OrderBy(x => x).SequenceEqual(regel.OrtIds.OrderBy(x => x)))
        {
            teile.Add($"Raumänderung: {string.Join(" + ", v.Orte.Select(o => o.OrtName))}");
        }

        if (regel is not null && (TimeOnly.FromDateTime(v.Beginn) != regel.Beginn))
        {
            teile.Add($"Zeitänderung: {v.Beginn:HH:mm}–{v.Ende:HH:mm}");
        }

        if (v.Bemerkung is { Length: > 0 } bem)
        {
            teile.Add(bem);
        }

        return teile.Count == 0 ? "Einzeln geänderter Termin." : string.Join(" · ", teile);
    }

    private static IReadOnlyList<Belegung> Teilnehmende(Kurs k, Veranstaltung v, IReadOnlyList<Belegung> belegungen)
        => belegungen.Where(b => b.Status == Belegungsstatus.Angemeldet && (!b.IstKontingent || b.Einbuchungen.ContainsKey(v.Id))).ToList();

    private async Task<bool> MailAsync(Guid personId, string betreff, string text, CancellationToken abbruch)
    {
        var p = await personen.LadenAsync(personId, abbruch);
        if (p is null || p.IstAnonymisiert || p.EMail is not { Length: > 0 } mail || mail == "–")
        {
            return false;
        }

        var name = p.AktuellerName(uhr.GetLocalNow());
        await post.KundenmailEinreihenAsync(mail, p.Anzeigename, betreff, $"Guten Tag {name.Vorname} {name.Nachname},\n\n{text}", p.Sprache, p.Id, abbruch);
        return true;
    }

    private static string Zufall() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
