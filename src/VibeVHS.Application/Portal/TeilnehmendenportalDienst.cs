using System.Globalization;
using System.Security.Cryptography;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Shop;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Dokumente;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Portal;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Shop;

namespace VibeVHS.Application.Portal;

public sealed record TnBuchung(
    Guid BelegungId,
    Guid PersonId,
    string Fuer,
    string Kursnummer,
    string Titel,
    DateTime? Beginn,
    DateTime? Ende,
    Belegungsstatus Status,
    string StatusText,
    int? Warteplatz,
    DateTimeOffset? AngebotBis,
    long EntgeltCent,
    bool Vergangen,
    long? StornoGebuehrCent,
    string? StornoText,
    bool Umbuchbar,
    string? UmbuchungGesperrt);

public sealed record TnDokument(Guid Id, string Titel, string Art, DateTimeOffset Erstellt, bool Verfuegbar);

public sealed record TnPerson(Guid Id, string Name, string? Nachweis);

public sealed record TnDaten(
    string Name,
    string? EMail,
    string? Strasse,
    string? Postleitzahl,
    string? Ort,
    string? Telefon,
    string Sprache,
    IReadOnlyList<(Guid Id, string Referenz, string Iban, DateOnly Unterschrieben, bool Aktiv)> Mandate,
    bool Newsletter,
    bool Fotos,
    IReadOnlyList<TnPerson> DuerfenFuerMichBuchen,
    IReadOnlyList<TnPerson> IchDarfBuchenFuer,
    IReadOnlyList<Portalantrag> Anliegen);

/// <summary>
/// Teilnehmendenportal: eigene Buchungen, Termine, Unterlagen, Dokumente und Daten – und Buchungen für Personen, für die eine
/// Buchungsvollmacht besteht. Storno, Umbuchung und Nachrückangebote laufen über dieselben Regeln wie am Tresen und im Webshop.
/// </summary>
public sealed class TeilnehmendenportalDienst(
    IAktuellerBenutzer benutzer,
    IBenutzerkonten konten,
    IPortalSpeicher speicher,
    IShopSpeicher shop,
    IAnmeldungSpeicher anmeldungen,
    IProgrammSpeicher programm,
    IPersonenSpeicher personen,
    IZugangstokenSpeicher tokens,
    IFinanzSpeicher finanzSpeicher,
    IDokumentSpeicher dokumentSpeicher,
    IStammdaten stammdaten,
    AnmeldungDienst anmeldung,
    FinanzDienst finanzen,
    DokumentDienst dokumente,
    CheckinZugang zugang,
    IQrCode qr,
    PostausgangDienst post,
    PortalVerwaltung verwaltung,
    OeffentlicheAdresse adresse,
    IAuditProtokoll protokoll,
    TimeProvider uhr)
{
    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    // ---------- Wer bin ich? ----------

    public async Task<Guid> IchAsync(CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (wer.Rolle != Rolle.Teilnehmende)
        {
            throw new PortalFehler("Das Teilnehmendenportal ist für Teilnehmende.", 403);
        }

        return (await konten.FindenAsync(wer.Kennung, abbruch))?.PersonId
               ?? throw new PortalFehler("Ihr Zugang ist mit keiner Person verknüpft. Bitte wenden Sie sich an die vhs.", 403);
    }

    /// <summary>Eigene Person oder eine, für die heute eine Buchungsvollmacht besteht.</summary>
    private async Task<Person> PersonMitRechtAsync(Guid personId, CancellationToken abbruch)
    {
        var ich = await IchAsync(abbruch);
        var p = await personen.LadenAsync(personId, abbruch) ?? throw new PortalFehler("Person nicht gefunden.", 404);
        if (p.Id != ich && !p.DarfBuchen(ich, Heute, uhr.GetLocalNow()))
        {
            throw new PortalFehler("Für diese Person haben Sie keine Buchungsvollmacht.", 403);
        }

        return p;
    }

    // ---------- Anmeldelink ----------

    /// <summary>Anmeldelink anfordern (Antwort immer gleich). Gefunden wird ein bestätigtes Shop-Kundenkonto oder genau eine Person mit dieser Adresse.</summary>
    public async Task LinkAnfordernAsync(string eMail, CancellationToken abbruch = default)
    {
        var mail = (eMail ?? "").Trim();
        if (mail.Length < 5 || !mail.Contains('@', StringComparison.Ordinal))
        {
            return;
        }

        Person? person = null;
        if (await shop.KontoMitEMailAsync(Shopkonto.Normalisieren(mail), abbruch) is { Status: Kontostatus.Aktiv, PersonId: { } pid })
        {
            person = await personen.LadenAsync(pid, abbruch);
        }

        if (person is null)
        {
            var treffer = (await speicher.PersonenMitEMailAsync(mail, abbruch)).Where(p => p.Datenschutz == Datenschutzstatus.Aktiv).ToList();
            person = treffer.Count == 1 ? treffer[0] : null;
        }

        if (person is null || person.Datenschutz != Datenschutzstatus.Aktiv)
        {
            await protokoll.ProtokollierenAsync("Portal.LinkAngefordert", "Anmeldelink Teilnehmendenportal angefordert – keine eindeutige Person, nichts verschickt", erfolgreich: false,
                abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
            return;
        }

        var (token, geheimnis) = Zugangstoken.Erzeugen(person.Id, Tokenart.PortalLinkTeilnehmende, uhr.GetUtcNow());
        tokens.Hinzufuegen(token);
        await post.KundenmailEinreihenAsync(mail, person.Anzeigename, "Ihr Link zu „Meine vhs“",
            $"{person.Briefanrede(uhr.GetLocalNow())},\n\nmit diesem Link kommen Sie zu Ihren Kursen, Terminen und Unterlagen (gilt 15 Minuten, nur einmal):\n"
            + $"{adresse.Link($"portal/teilnehmende/link/{geheimnis}")}\n\nSie haben den Link nicht angefordert? Dann löschen Sie diese Mail einfach.", person.Sprache, person.Id, abbruch);
        await protokoll.ProtokollierenAsync("Portal.LinkAngefordert", $"Anmeldelink Teilnehmendenportal an {Pseudonym.Person(person.Id)} verschickt", nameof(Person), person.Id,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task<Benutzerkonto?> LinkEinloesenAsync(string geheimnis, CancellationToken abbruch = default)
    {
        var t = await tokens.ZumHashAsync(Zugangstoken.HashVon(geheimnis ?? ""), abbruch);
        var jetzt = uhr.GetUtcNow();
        if (t is not { Art: Tokenart.PortalLinkTeilnehmende, Gueltig: true } || jetzt - t.ErstelltAm > DozentenportalDienst.PortalLinkGueltig)
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

            konto = new Benutzerkonto($"tn-{Pseudonym.Kurz(person.Id).ToLowerInvariant()}", person.Anzeigename, "Teilnehmende (Portal)", [Rolle.Teilnehmende], [], person.Id,
                "Portal-Konto: angelegt beim ersten Anmeldelink");
            speicher.Hinzufuegen(konto);
        }
        else if (!konto.Rollen.Contains(Rolle.Teilnehmende))
        {
            // Mitarbeitende nutzen ihr eigenes Konto. Ein Portal-Konto einer Kursleitung, die auch Teilnehmende ist, bekommt den Zugang dazu.
            if (!konto.IstPortalkonto || await personen.LadenAsync(t.PersonId, abbruch) is not { } p || !p.Rollen.HasFlag(Personenrolle.Teilnehmende))
            {
                return null;
            }

            konto.Aendern(konto.Name, konto.Funktion, [.. konto.Rollen, Rolle.Teilnehmende], konto.Fachbereiche);
        }

        await protokoll.ProtokollierenAsync("Portal.Angemeldet", $"Anmeldung Teilnehmendenportal per Link: {Pseudonym.Person(t.PersonId)}", nameof(Person), t.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return konto;
    }

    // ---------- Buchungen ----------

    /// <summary>Eigene Buchungen und die der Personen, für die ich buchen darf.</summary>
    public async Task<IReadOnlyList<TnBuchung>> BuchungenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var fuer = (await VollmachtgeberAsync(ich, abbruch)).Select(p => p.Id).Prepend(ich).ToList();
        var namen = await personen.AnzeigenamenAsync(fuer, abbruch);
        var jetzt = uhr.GetLocalNow();
        var liste = new List<TnBuchung>();
        foreach (var pid in fuer)
        {
            foreach (var pb in await anmeldungen.BelegungenDerPersonAsync(pid, abbruch))
            {
                var b = pb.Belegung;
                var kurs = await programm.KursLadenAsync(b.KursId, abbruch);
                if (kurs is null)
                {
                    continue;
                }

                var termine = kurs.Veranstaltungen.Where(v => !v.Ausgefallen).OrderBy(v => v.Beginn).ToList();
                var ende = termine.LastOrDefault()?.Ende;
                int? platz = null;
                if (b.Status == Belegungsstatus.Warteliste)
                {
                    platz = (await anmeldungen.BelegungenDesKursesAsync(kurs.Id, abbruch)).Where(x => x.Status == Belegungsstatus.Warteliste)
                        .OrderBy(x => x.GebuchtAm).ToList().FindIndex(x => x.Id == b.Id) + 1;
                }

                var regel = b.Offen ? Buchungspruefung.Storno(b, kurs, jetzt) : null;
                string? umbuchungGesperrt = !b.Offen ? "Nur aktive Anmeldungen lassen sich umbuchen."
                    : b.Status != Belegungsstatus.Angemeldet ? "Umbuchen geht nur bei einer festen Anmeldung, nicht von der Warteliste."
                    : termine.FirstOrDefault()?.Beginn < jetzt.DateTime && kurs.Status != Kursstatus.Abgesagt ? "Der Kurs hat schon begonnen. Sie können sich nur noch abmelden."
                    : null;
                liste.Add(new TnBuchung(b.Id, pid, pid == ich ? "Sie" : namen.GetValueOrDefault(pid) ?? "?", kurs.Kursnummer, kurs.Titel, termine.FirstOrDefault()?.Beginn, ende,
                    b.Status, BelegungAnzeige.Status(b.Status), platz, b.AngebotBis, b.EntgeltCent, !b.Offen || (ende is { } e && e < jetzt.DateTime),
                    regel?.GebuehrCent, regel?.Erklaerung, umbuchungGesperrt is null, umbuchungGesperrt));
            }
        }

        return liste.OrderBy(x => x.Vergangen).ThenBy(x => x.Beginn ?? DateTime.MaxValue).ToList();
    }

    private async Task<Belegung> BelegungMitRechtAsync(Guid belegungId, CancellationToken abbruch)
    {
        var b = await anmeldung.BelegungLadenAsync(belegungId, abbruch) ?? throw new PortalFehler("Buchung nicht gefunden.", 404);
        await PersonMitRechtAsync(b.PersonId, abbruch);
        return b;
    }

    public async Task<Stornoregel> StornierenAsync(Guid belegungId, CancellationToken abbruch = default)
    {
        var b = await BelegungMitRechtAsync(belegungId, abbruch);
        return await Fachlich(() => anmeldung.SelbstStornierenAsync(b.Id, b.PersonId, "Abmeldung im Teilnehmendenportal", abbruch, false, "Teilnehmendenportal"));
    }

    public async Task<IReadOnlyList<KursOption>> UmbuchungsoptionenAsync(Guid belegungId, CancellationToken abbruch = default)
    {
        var b = await BelegungMitRechtAsync(belegungId, abbruch);
        return (await anmeldung.UmbuchungsvorschlaegeAsync(b.Id, 6, abbruch)).Where(o => o.Kurs.Sichtbarkeit == Sichtbarkeit.Oeffentlich && o.Pruefung.Code == Buchungscode.Buchbar).ToList();
    }

    public async Task UmbuchenAsync(Guid belegungId, Guid neuerKursId, CancellationToken abbruch = default)
    {
        await BelegungMitRechtAsync(belegungId, abbruch);
        await Fachlich(async () => { await anmeldung.UmbuchenAsync(belegungId, neuerKursId, abbruch); return 0; });
    }

    public async Task AngebotAsync(Guid belegungId, bool annehmen, CancellationToken abbruch = default)
    {
        await BelegungMitRechtAsync(belegungId, abbruch);
        await Fachlich(async () =>
        {
            if (annehmen)
            {
                await anmeldung.AngebotAnnehmenAsync(belegungId, abbruch);
            }
            else
            {
                await anmeldung.AngebotAblehnenAsync(belegungId, abbruch);
            }

            return 0;
        });
    }

    private static async Task<T> Fachlich<T>(Func<Task<T>> arbeit)
    {
        try
        {
            return await arbeit();
        }
        catch (InvalidOperationException ex)
        {
            throw new PortalFehler(ex.Message, 409);
        }
    }

    /// <summary>Personen, für die ich heute buchen darf (sie haben mir eine Buchungsvollmacht erteilt).</summary>
    public async Task<IReadOnlyList<Person>> VollmachtgeberAsync(Guid ich, CancellationToken abbruch = default)
        => (await speicher.PersonenMitVollmachtFuerAsync(ich, abbruch)).Where(p => p.Id != ich && p.DarfBuchen(ich, Heute, uhr.GetLocalNow())).ToList();

    /// <summary>Für wen kann ich buchen? Ich selbst und Vollmachtgeber.</summary>
    public async Task<IReadOnlyList<TnPerson>> BuchbarFuerAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var selbst = await personen.LadenAsync(ich, abbruch);
        return new[] { new TnPerson(ich, $"{selbst?.Anzeigename} (Sie)", null) }
            .Concat((await VollmachtgeberAsync(ich, abbruch)).Select(p => new TnPerson(p.Id, p.Anzeigename, null))).ToList();
    }

    /// <summary>Für mich oder per Vollmacht prüfen (Bevollmächtigter = ich, wenn für andere).</summary>
    public async Task<(Guid PersonId, Guid? Bevollmaechtigter)> BuchenFuerAsync(Guid personId, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        await PersonMitRechtAsync(personId, abbruch);
        return (personId, personId == ich ? null : ich);
    }

    // ---------- Termine und Kalender ----------

    private async Task<IReadOnlyList<(Kurs Kurs, Veranstaltung Termin)>> EigeneTermineAsync(Guid ich, DateTime von, DateTime bis, CancellationToken abbruch)
    {
        var liste = new List<(Kurs, Veranstaltung)>();
        foreach (var pb in (await anmeldungen.BelegungenDerPersonAsync(ich, abbruch)).Where(x => x.Belegung.Status == Belegungsstatus.Angemeldet))
        {
            var b = pb.Belegung;
            if (await programm.KursLadenAsync(b.KursId, abbruch) is not { } kurs)
            {
                continue;
            }

            liste.AddRange(kurs.Veranstaltungen.Where(v => v.Beginn >= von && v.Beginn <= bis && (!b.IstKontingent || b.Einbuchungen.ContainsKey(v.Id))).Select(v => (kurs, v)));
        }

        return liste.OrderBy(t => t.Item2.Beginn).ToList();
    }

    public async Task<IReadOnlyList<PortalTermin>> TermineAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
        var termine = await EigeneTermineAsync(ich, Heute.ToDateTime(TimeOnly.MinValue), Heute.AddDays(42).ToDateTime(TimeOnly.MaxValue), abbruch);
        return termine.Select(t =>
        {
            var ort = t.Termin.Orte.Select(o => verzeichnis.Finden(o.OrtId)).OfType<Ort>().FirstOrDefault();
            return new PortalTermin(t.Termin.Id, t.Kurs.Id, t.Kurs.Kursnummer, t.Kurs.Titel, t.Termin.Nummer, t.Termin.Beginn, t.Termin.Ende,
                string.Join(" + ", t.Termin.Orte.Select(o => o.OrtName)), ort is null ? null : verzeichnis.Wegbeschreibung(ort), t.Termin.Ausgefallen,
                DozentenportalDienst.Aenderung(t.Kurs, t.Termin), false, 0);
        }).ToList();
    }

    public async Task<(string Datei, string Inhalt)> TerminIcsAsync(Guid veranstaltungId, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var t = (await EigeneTermineAsync(ich, DateTime.MinValue, DateTime.MaxValue, abbruch)).FirstOrDefault(x => x.Termin.Id == veranstaltungId);
        if (t.Kurs is null)
        {
            throw new PortalFehler("Dieser Termin gehört nicht zu Ihren Kursen.", 403);
        }

        var name = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "vhs";
        return ($"{t.Kurs.Kursnummer}-termin-{t.Termin.Nummer}.ics",
            Kalenderdatei.Erzeugen($"{t.Kurs.Kursnummer} Termin {t.Termin.Nummer}", [(t.Kurs, t.Termin, DozentenportalDienst.Aenderung(t.Kurs, t.Termin))], name, uhr.GetUtcNow()));
    }

    public async Task<Uri> KalenderLinkErzeugenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var jetzt = uhr.GetUtcNow();
        foreach (var alt in (await tokens.DerPersonAsync(ich, abbruch)).Where(t => t is { Art: Tokenart.KalenderTeilnahme, Gueltig: true }))
        {
            alt.Widerrufen(jetzt, "neue Kalenderadresse erzeugt");
        }

        var (token, geheimnis) = Zugangstoken.Erzeugen(ich, Tokenart.KalenderTeilnahme, jetzt);
        tokens.Hinzufuegen(token);
        await protokoll.ProtokollierenAsync("Portal.Kalender", $"Kalender-Abo (Teilnahme) für {Pseudonym.Person(ich)} erzeugt", nameof(Person), ich, abbruch: abbruch);
        await tokens.SpeichernAsync(abbruch);
        return adresse.Link($"portal/teilnehmende/kalender/{geheimnis}.ics");
    }

    public async Task KalenderSperrenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        foreach (var t in (await tokens.DerPersonAsync(ich, abbruch)).Where(t => t is { Art: Tokenart.KalenderTeilnahme, Gueltig: true }))
        {
            t.Widerrufen(uhr.GetUtcNow(), "von der Person gesperrt");
        }

        await tokens.SpeichernAsync(abbruch);
    }

    public async Task<bool> KalenderAktivAsync(CancellationToken abbruch = default)
        => (await tokens.DerPersonAsync(await IchAsync(abbruch), abbruch)).Any(t => t is { Art: Tokenart.KalenderTeilnahme, Gueltig: true });

    public async Task<string?> KalenderAsync(string geheimnis, CancellationToken abbruch = default)
    {
        var t = await tokens.ZumHashAsync(Zugangstoken.HashVon(geheimnis ?? ""), abbruch);
        if (t is not { Art: Tokenart.KalenderTeilnahme, Gueltig: true })
        {
            return null;
        }

        t.Genutzt(uhr.GetUtcNow());
        await tokens.SpeichernAsync(abbruch);
        var name = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "vhs";
        var termine = await EigeneTermineAsync(t.PersonId, Heute.AddDays(-30).ToDateTime(TimeOnly.MinValue), Heute.AddYears(1).ToDateTime(TimeOnly.MinValue), abbruch);
        return Kalenderdatei.Erzeugen($"Meine Kurse – {name}", termine.Select(x => (x.Kurs, x.Termin, DozentenportalDienst.Aenderung(x.Kurs, x.Termin))), name, uhr.GetUtcNow());
    }

    /// <summary>Neuer persönlicher Check-in-Link (frühere bleiben gültig, bis sie gesperrt werden).</summary>
    public async Task<Uri> CheckinLinkAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var link = zugang.LinkErzeugen(ich);
        await protokoll.ProtokollierenAsync("Portal.CheckinLink", $"Check-in-Link für {Pseudonym.Person(ich)} im Teilnehmendenportal abgerufen", nameof(Person), ich, abbruch: abbruch);
        await tokens.SpeichernAsync(abbruch);
        return link;
    }

    /// <summary>Teilnehmerausweis selbst ausstellen: PDF im Archiv (Dokumente) und QR als Bild fürs Handy.</summary>
    public async Task<(Guid DokumentId, byte[] QrPng)> AusweisAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var dok = await dokumente.TeilnehmerausweisAsync(ich, abbruch, selbst: true);
        // Das Ausweis-Token im PDF ist nur dort lesbar (gespeichert wird nur der Hash); fürs Handy-Bild ein eigenes, ebenfalls widerrufbares Token.
        var inhalt = zugang.AusweisErzeugen(ich);
        await tokens.SpeichernAsync(abbruch);
        return (dok.Id, qr.Png(inhalt));
    }

    // ---------- Unterlagen ----------

    public async Task<IReadOnlyList<(Guid Id, string Kurs, string Titel, string Datei, long Groesse, DateTimeOffset Am)>> UnterlagenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var jetzt = uhr.GetUtcNow();
        var liste = new List<(Guid, string, string, string, long, DateTimeOffset)>();
        foreach (var pb in (await anmeldungen.BelegungenDerPersonAsync(ich, abbruch)).Where(x => x.Belegung.Status == Belegungsstatus.Angemeldet))
        {
            foreach (var u in (await speicher.UnterlagenAsync(pb.Belegung.KursId, abbruch)).Where(u => u.Abrufbar(jetzt)))
            {
                var d = await speicher.DateiAsync(u.DateiId, abbruch);
                liste.Add((u.Id, $"{pb.Belegung.Kursnummer} {pb.Kurstitel}", u.Titel, d?.Name ?? "?", d?.Groesse ?? 0, u.HochgeladenAm));
            }
        }

        return liste.OrderByDescending(x => x.Item6).ToList();
    }

    public async Task<Datei> UnterlageAsync(Guid unterlageId, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var u = await speicher.UnterlageAsync(unterlageId, abbruch);
        var meine = (await anmeldungen.BelegungenDerPersonAsync(ich, abbruch)).Where(x => x.Belegung.Status == Belegungsstatus.Angemeldet).Select(x => x.Belegung.KursId).ToHashSet();
        if (u is null || !meine.Contains(u.KursId))
        {
            throw new PortalFehler("Diese Unterlage gehört nicht zu Ihren Kursen.", 403);
        }

        if (!u.Abrufbar(uhr.GetUtcNow()))
        {
            throw new PortalFehler("Diese Unterlage ist nicht mehr verfügbar.", 410);
        }

        u.Abgerufen();
        await speicher.SpeichernAsync(abbruch);
        return await speicher.DateiAsync(u.DateiId, abbruch) ?? throw new PortalFehler("Datei fehlt.", 404);
    }

    // ---------- Dokumente und Konto ----------

    public async Task<IReadOnlyList<TnDokument>> DokumenteAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        return (await dokumentSpeicher.DerPersonAsync(ich, abbruch))
            .OrderByDescending(d => d.ErstelltAm)
            .Select(d => new TnDokument(d.Id, d.Titel, DokumentAnzeige.Art(d.Art), d.ErstelltAm, d.Unveraendert))
            .ToList();
    }

    /// <summary>Original aus dem Archiv (Nachdruck = Original, nach Prüfung der Prüfsumme).</summary>
    public async Task<(string Datei, byte[] Pdf)> DokumentPdfAsync(Guid dokumentId, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var d = await dokumentSpeicher.LadenAsync(dokumentId, abbruch);
        if (d is null || d.PersonId != ich)
        {
            throw new PortalFehler("Dieses Dokument gehört nicht zu Ihnen.", 403);
        }

        if (!d.Unveraendert)
        {
            throw new PortalFehler("Das Dokument ist nicht verfügbar. Bitte wenden Sie sich an die vhs.", 409);
        }

        await protokoll.ProtokollierenAsync("Portal.Dokument", $"{Pseudonym.Person(ich)} hat „{d.Titel}“ im Teilnehmendenportal abgerufen (Original, Prüfsumme ok)", nameof(Person), ich,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return ($"{d.Titel}.pdf".Replace('/', '-'), d.Pdf);
    }

    public async Task<Personenkonto> KontoAsync(CancellationToken abbruch = default) => await finanzen.KontoAsync(await IchAsync(abbruch), abbruch);

    // ---------- Eigene Daten ----------

    public async Task<TnDaten> DatenAsync(CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        var p = await personen.LadenAsync(ich, abbruch) ?? throw new PortalFehler("Person nicht gefunden.", 404);
        var jetzt = uhr.GetLocalNow();
        var stand = p.StandAm(Heute, jetzt);
        var mandate = (await finanzSpeicher.MandateAsync(ich, abbruch)).OrderByDescending(m => m.UnterschriebenAm)
            .Select(m => (m.Id, m.Referenz, Iban.Maskiert(m.Iban), m.UnterschriebenAm, m.Status == Mandatsstatus.Aktiv)).ToList();
        var fuerMich = p.Vollmachten.AlleAmStichtag(Heute, jetzt).Select(v => new TnPerson(v.BevollmaechtigterId, v.BevollmaechtigterName, v.Nachweis)).ToList();
        var ichFuer = (await VollmachtgeberAsync(ich, abbruch)).Select(x => new TnPerson(x.Id, x.Anzeigename, null)).ToList();
        return new TnDaten(stand.NameMitTitel, p.EMail, stand.Aufenthaltsort?.Strasse, stand.Aufenthaltsort?.Postleitzahl, stand.Aufenthaltsort?.Ort,
            stand.Rufnummern.FirstOrDefault()?.Nummer, p.Sprache, mandate,
            p.EinwilligungAm(Kontaktkanal.EMail, Kontaktzweck.Werbung, Heute, jetzt), p.EinwilligungAm(Kontaktkanal.Brief, Kontaktzweck.Fotos, Heute, jetzt),
            fuerMich, ichFuer, (await speicher.AntraegeAsync(ich, false, abbruch)).OrderByDescending(a => a.EingereichtAm).Take(20).ToList());
    }

    private async Task<Person> SelbstAsync(CancellationToken abbruch)
        => await personen.LadenAsync(await IchAsync(abbruch), abbruch) ?? throw new PortalFehler("Person nicht gefunden.", 404);

    private Erfassung Aenderung(string grund) => Erfassung.Aenderung(uhr.GetLocalNow(), grund, "Teilnehmendenportal");

    public async Task AdresseAendernAsync(string strasse, string postleitzahl, string ort, CancellationToken abbruch = default)
    {
        if (string.IsNullOrWhiteSpace(strasse) || string.IsNullOrWhiteSpace(postleitzahl) || string.IsNullOrWhiteSpace(ort))
        {
            throw new PortalFehler("Bitte Straße, Postleitzahl und Ort angeben.");
        }

        var p = await SelbstAsync(abbruch);
        var e = Aenderung("Umzug im Teilnehmendenportal");
        if (p.StandAm(Heute, uhr.GetLocalNow()).Aufenthaltsort is { } alt)
        {
            p.UmzugAb(alt.AdressId, null, strasse.Trim(), postleitzahl.Trim(), ort.Trim(), Heute, e);
        }
        else
        {
            var id = p.AdresseHinzufuegen(null, strasse.Trim(), postleitzahl.Trim(), ort.Trim(), Heute, e);
            p.AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, id, Heute, e);
        }

        await protokoll.ProtokollierenAsync("Portal.Adresse", $"{Pseudonym.Person(p.Id)} hat die Anschrift im Teilnehmendenportal geändert", nameof(Person), p.Id, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    public async Task TelefonSetzenAsync(string nummer, CancellationToken abbruch = default)
    {
        var p = await SelbstAsync(abbruch);
        var nr = (nummer ?? "").Trim();
        if (nr.Length < 5 || nr.Count(char.IsDigit) < 5)
        {
            throw new PortalFehler("Bitte eine gültige Telefonnummer angeben.");
        }

        var e = Aenderung("Telefon im Teilnehmendenportal");
        if (p.StandAm(Heute, uhr.GetLocalNow()).Rufnummern.FirstOrDefault() is { } alt)
        {
            p.RufnummerAendernAb(alt.NummerId, alt.Art, nr, alt.Dienste, alt.Einwilligung, true, Heute, e);
        }
        else
        {
            p.RufnummerHinzufuegen(nr.StartsWith("01", StringComparison.Ordinal) || nr.StartsWith("+491", StringComparison.Ordinal) ? RufnummerArt.Mobil : RufnummerArt.Festnetz,
                nr, Messagingdienst.Keiner, false, true, Heute, e);
        }

        await protokoll.ProtokollierenAsync("Portal.Telefon", $"{Pseudonym.Person(p.Id)} hat die Telefonnummer im Teilnehmendenportal geändert", nameof(Person), p.Id, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    /// <summary>Neue E-Mail-Adresse: wirksam erst nach Klick auf den Link, der an die neue Adresse geht.</summary>
    public async Task EMailAendernAsync(string neu, CancellationToken abbruch = default)
    {
        var p = await SelbstAsync(abbruch);
        var mail = (neu ?? "").Trim();
        if (!ShopDienst.GueltigeEMail(mail))
        {
            throw new PortalFehler("Bitte eine gültige E-Mail-Adresse angeben.");
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await verwaltung.EinreichenAsync(new Portalantrag(p.Id, Antragsart.EMailAenderung, $"Neue Adresse {mail}", "wartet auf Bestätigung über den Link",
            new Dictionary<string, string> { ["email"] = mail, ["hash"] = Zugangstoken.HashVon(token) }, null, null, null, uhr.GetUtcNow()), abbruch);
        await post.KundenmailEinreihenAsync(mail, p.Anzeigename, "Bitte bestätigen Sie Ihre neue E-Mail-Adresse",
            $"Guten Tag,\n\nbitte bestätigen Sie, dass wir Ihnen künftig an diese Adresse schreiben dürfen:\n{adresse.Link($"portal/teilnehmende/email/{token}")}\n\n"
            + "Der Link gilt 48 Stunden. Haben Sie das nicht veranlasst? Dann ignorieren Sie diese Mail.", p.Sprache, p.Id, abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    /// <summary>Bestätigungslink der neuen E-Mail-Adresse (ohne Anmeldung).</summary>
    public async Task<bool> EMailBestaetigenAsync(string token, CancellationToken abbruch = default)
    {
        var hash = Zugangstoken.HashVon(token ?? "");
        var antrag = (await speicher.AntraegeAsync(null, true, abbruch))
            .FirstOrDefault(a => a.Art == Antragsart.EMailAenderung && a.Daten.GetValueOrDefault("hash") == hash);
        if (antrag is null || uhr.GetUtcNow() - antrag.EingereichtAm > TimeSpan.FromHours(48))
        {
            return false;
        }

        var p = await personen.LadenAsync(antrag.PersonId, abbruch);
        if (p is null)
        {
            return false;
        }

        var e = Aenderung("E-Mail im Teilnehmendenportal bestätigt");
        foreach (var alt in p.StandAm(Heute, uhr.GetLocalNow()).EMails)
        {
            p.EMailAufgebenAb(alt.AdressId, Heute, e);
        }

        p.EMailHinzufuegen(EMailArt.Privat, antrag.Daten["email"], true, Heute, e);
        antrag.Entscheiden(true, "Bestätigungslink", null, uhr.GetUtcNow());
        await protokoll.ProtokollierenAsync("Portal.EMail", $"{Pseudonym.Person(p.Id)} hat eine neue E-Mail-Adresse bestätigt", nameof(Person), p.Id, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
        return true;
    }

    public async Task EinwilligungAsync(Kontaktzweck zweck, bool erteilt, CancellationToken abbruch = default)
    {
        if (zweck is not (Kontaktzweck.Werbung or Kontaktzweck.Fotos))
        {
            throw new PortalFehler("Diese Einwilligung lässt sich hier nicht ändern.");
        }

        var p = await SelbstAsync(abbruch);
        p.EinwilligungSetzenAb(zweck == Kontaktzweck.Fotos ? Kontaktkanal.Brief : Kontaktkanal.EMail, zweck, erteilt, Heute,
            Aenderung(erteilt ? "Einwilligung im Teilnehmendenportal erteilt" : "Einwilligung im Teilnehmendenportal widerrufen"));
        await protokoll.ProtokollierenAsync("Portal.Einwilligung", $"{Pseudonym.Person(p.Id)}: Einwilligung „{Einwilligung.ZweckAnzeige(zweck)}“ {(erteilt ? "erteilt" : "widerrufen")}",
            nameof(Person), p.Id, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    public async Task SpracheAsync(string sprache, CancellationToken abbruch = default)
    {
        if (sprache is not { Length: >= 2 and <= 5 })
        {
            throw new PortalFehler("Bitte eine Sprache wählen.");
        }

        var p = await SelbstAsync(abbruch);
        p.SpracheAendernAb(sprache, Heute, Aenderung("Sprache im Teilnehmendenportal"));
        await personen.SpeichernAsync(abbruch);
    }

    public async Task MandatWiderrufenAsync(Guid mandatId, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        await Fachlich(async () => { await finanzen.MandatWiderrufenAsync(mandatId, abbruch, ich); return 0; });
    }

    public async Task MandatErteilenAsync(string kontoinhaber, string iban, string? bic, CancellationToken abbruch = default)
    {
        var ich = await IchAsync(abbruch);
        if (string.IsNullOrWhiteSpace(kontoinhaber) || Iban.Fehler(iban) is { } f && (f.Length > 0))
        {
            throw new PortalFehler(Iban.Fehler(iban) ?? "Bitte den Kontoinhaber angeben.");
        }

        await Fachlich(async () => await finanzen.MandatOnlineErteiltAsync(ich, kontoinhaber.Trim(), iban, string.IsNullOrWhiteSpace(bic) ? null : bic.Trim(), abbruch));
    }

    // ---------- Datenschutz ----------

    public async Task AuskunftAnfordernAsync(CancellationToken abbruch = default) => await DatenschutzAsync(Antragsart.Auskunft, "Ich möchte wissen, welche Daten die vhs über mich speichert.", abbruch);

    public async Task LoeschungAnfordernAsync(string? hinweis, CancellationToken abbruch = default)
        => await DatenschutzAsync(Antragsart.Loeschung, string.IsNullOrWhiteSpace(hinweis) ? "Bitte löschen Sie meine Daten." : hinweis, abbruch);

    private async Task DatenschutzAsync(Antragsart art, string text, CancellationToken abbruch)
    {
        var ich = await IchAsync(abbruch);
        if ((await speicher.AntraegeAsync(ich, true, abbruch)).Any(a => a.Art == art))
        {
            throw new PortalFehler("Ihre Anfrage liegt schon bei uns. Wir antworten innerhalb eines Monats.");
        }

        await verwaltung.EinreichenAsync(new Portalantrag(ich, art, AntragAnzeige.Art(art), text, new Dictionary<string, string>(), null, null, null, uhr.GetUtcNow()), abbruch);
    }
}
