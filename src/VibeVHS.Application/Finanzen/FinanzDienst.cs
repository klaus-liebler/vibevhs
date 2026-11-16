using System.Globalization;
using System.Text.Json;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Finanzen;

/// <summary>Empfänger eines Belegs: Person oder Organisation, mit Anschrift zum Belegzeitpunkt.</summary>
public sealed record Belegempfaenger(Guid? PersonId, Guid? OrganisationId, string Name, IReadOnlyList<string> Anschrift, string? EMail, string Sprache,
    string? LeitwegId = null)
{
    public Guid ParteiId => PersonId ?? OrganisationId ?? Guid.Empty;
}

public sealed record Rechnungslaufergebnis(int Rechnungen, int Stornos, int Gebuehren, int Firmenrechnungen, long SummeCent)
{
    public override string ToString() => $"{Rechnungen} Rechnungen, {Firmenrechnungen} Firmenrechnungen, {Stornos} Stornos, {Gebuehren} Gebührenrechnungen";
}

/// <summary>
/// Kern der Finanzen: Einstellungen, Festschreibung mit lückenloser Nummer und Hash-Kette, Storno statt Änderung,
/// Rechnungslauf aus den Belegungen, Teilnehmerkonto, SEPA-Mandate und Bankverbindungen.
/// </summary>
public sealed class FinanzDienst(
    IFinanzSpeicher speicher,
    IFinanzprotokoll finanzprotokoll,
    IPersonenSpeicher personen,
    IProgrammSpeicher programm,
    IStammdaten stammdaten,
    DokumentDienst dokumente,
    PostausgangDienst post,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    internal static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    // ---------- Rechte ----------

    public async Task<Benutzer> PruefenAsync(Rolle[] rollen, CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !rollen.Contains(r)))
        {
            throw new InvalidOperationException(wer.Rolle == Rolle.Pruefung
                ? "Die Rolle „Prüfung“ hat nur lesenden Zugriff."
                : Freigabe.NurFuer(wer.Rolle, rollen).Begruendung);
        }

        return wer;
    }

    // ---------- Einstellungen ----------

    public async Task<Finanzeinstellungen> EinstellungenAsync(CancellationToken abbruch = default)
    {
        if (await speicher.EinstellungenAsync(abbruch) is { } e)
        {
            return e;
        }

        var einrichtung = await stammdaten.EinrichtungAsync(abbruch);
        var name = einrichtung?.Name ?? "Volkshochschule";
        e = new Finanzeinstellungen(name, $"{name}\n{einrichtung?.Orte.FirstOrDefault() ?? ""}".Trim(), Iban.AusBlzUndKonto("40351060", "72000011"), "WELADED1STF",
            "DE98ZZZ09999999999", "311/5970/1234");
        speicher.Hinzufuegen(e);
        await finanzprotokoll.SchreibenAsync("Einstellungen.Angelegt", "Finanzeinstellungen", null, null,
            $"Konto {Iban.Maskiert(e.Iban)}, Gläubiger-ID {e.GlaeubigerId} (Testwerte)", abbruch);
        await speicher.SpeichernAsync(abbruch);
        return e;
    }

    public async Task EinstellungenAendernAsync(string kontoinhaber, string anschrift, string iban, string? bic, string glaeubigerId, string? steuernummer, string? ustId,
        string steuerhinweis, int zahlungszielTage, int vorabankuendigungTage, long ruecklastschriftGebuehrCent, IEnumerable<Mahnstufe> mahnstufen,
        CancellationToken abbruch = default)
    {
        await PruefenAsync([Rolle.Verwaltungsleitung, Rolle.Leitung], abbruch);
        var e = await EinstellungenAsync(abbruch);
        var vorher = Zusammenfassung(e);
        try
        {
            e.Aendern(kontoinhaber, anschrift, iban, bic, glaeubigerId, steuernummer, ustId, steuerhinweis, zahlungszielTage, vorabankuendigungTage,
                ruecklastschriftGebuehrCent, mahnstufen);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        await finanzprotokoll.SchreibenAsync("Einstellungen.Geaendert", "Finanzeinstellungen", null, vorher, Zusammenfassung(e), abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Einstellungen", "Finanzeinstellungen geändert", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    private static string Zusammenfassung(Finanzeinstellungen e)
        => $"Konto {Iban.Maskiert(e.Iban)}; Gläubiger-ID {e.GlaeubigerId}; Ziel {e.ZahlungszielTage} T; Vorab {e.VorabankuendigungTage} T; Rücklastschrift {new Geld(e.RuecklastschriftGebuehrCent)}; "
           + string.Join(", ", e.Mahnstufen.Select(s => $"{s.Bezeichnung} +{s.TageNachVorstufe} T {new Geld(s.GebuehrCent)}"));

    // ---------- Festschreibung und Storno ----------

    /// <summary>
    /// Schreibt den Beleg fest (lückenlose Nummer, Prüfsumme, Hash-Kette), legt das Druckstück mit Übersetzungs-QR ab und
    /// verschickt es auf Wunsch per Mail. Gespeichert wird sofort, damit Nummer und Protokoll zusammen festgeschrieben sind.
    /// </summary>
    public async Task FestschreibenAsync(Beleg b, Belegempfaenger? empfaenger, bool mailen, Func<Beleg, string?>? xmlErzeugen = null, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(b);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var kreis = await speicher.NummernkreisAsync(b.Art, b.Belegdatum.Year, abbruch);
        b.Festschreiben(kreis, uhr.GetLocalNow(), wer.Anzeige);
        var partei = b.PersonId is { } p ? Pseudonym.Person(p) : b.OrganisationId is { } o ? $"Organisation {o.ToString()[..8]}" : "–";
        await finanzprotokoll.SchreibenAsync("Beleg.Festgeschrieben", BelegartInfo.Name(b.Art), b.Id, null,
            $"{b.Nummer}; {b.Belegdatum:yyyy-MM-dd}; {new Geld(b.BetragCent)}; {partei}; Bezug {b.BezugBelegId?.ToString()[..8] ?? "–"}; SHA-256 {b.Sha256}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Beleg", $"{b.Nummer} festgeschrieben ({BelegartInfo.Name(b.Art)}, {new Geld(b.BetragCent)}, {partei})", nameof(Beleg), b.Id,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);

        var xml = xmlErzeugen?.Invoke(b);
        if (empfaenger is null && xml is not null)
        {
            b.DokumenteAblegen(null, xml);
            await speicher.SpeichernAsync(abbruch);
        }

        if (empfaenger is not null)
        {
            await DruckstueckAblegenAsync(b, empfaenger, mailen, xml, abbruch);
        }
    }

    /// <summary>
    /// Druckstück (PDF mit Übersetzungs-QR) archivieren, mit dem Beleg verknüpfen und auf Wunsch per Mail verschicken.
    /// Nummer, Prüfsumme und Hash-Kette sind zu diesem Zeitpunkt schon gespeichert; scheitert das Druckstück (z. B. PDF-Fehler),
    /// bleibt der Beleg lückenlos festgeschrieben und <see cref="DruckstueckeNachholenAsync"/> holt es beim nächsten Lauf nach.
    /// </summary>
    private async Task DruckstueckAblegenAsync(Beleg b, Belegempfaenger empfaenger, bool mailen, string? xml, CancellationToken abbruch)
    {
        var einst = await EinstellungenAsync(abbruch);
        var einrichtung = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Volkshochschule";
        var bezug = b.BezugBelegId is { } bid ? (await speicher.BelegAsync(bid, abbruch))?.Nummer : null;
        var dok = await dokumente.FinanzbelegArchivierenAsync(empfaenger.ParteiId, $"{BelegartInfo.Name(b.Art)} {b.Nummer}",
            JsonSerializer.Serialize(new { b.Nummer, Art = b.Art.ToString(), b.Belegdatum, b.Faellig, b.BetragCent, b.Sha256 }, Json), empfaenger.Sprache,
            link => Belegdruck.Brief(b, empfaenger, einst, einrichtung, bezug, link), abbruch);
        b.DokumenteAblegen(dok.Id, xml);
        await speicher.SpeichernAsync(abbruch);

        if (mailen && empfaenger.EMail is { Length: > 0 } mail && mail != "–")
        {
            List<Mailanhang> anhaenge = [new($"{b.Nummer}.pdf", "application/pdf;base64", Convert.ToBase64String(dok.Pdf))];
            if ((xml ?? b.Xml) is { } x)
            {
                anhaenge.Add(new($"{b.Nummer}-xrechnung.xml", "application/xml", x));
            }

            await post.KundenmailEinreihenAsync(mail, empfaenger.Name, $"{BelegartInfo.Name(b.Art)} {b.Nummer}", Belegdruck.Mailtext(b, empfaenger, einst, einrichtung, bezug),
                empfaenger.Sprache, empfaenger.PersonId, abbruch, weitere: anhaenge);
            await speicher.SpeichernAsync(abbruch);
        }
    }

    /// <summary>
    /// Festgeschriebene Belege ohne Druckstück (Druck beim Festschreiben gescheitert) nachträglich drucken, archivieren und – wie
    /// beim regulären Lauf – versenden. Zahlungsein- und -ausgänge haben bewusst kein Druckstück. Die Belegdaten selbst bleiben
    /// unverändert (das Druckstück gehört nicht zur Prüfsumme); jede Nachholung wird protokolliert.
    /// </summary>
    public async Task<int> DruckstueckeNachholenAsync(bool mailen = true, CancellationToken abbruch = default)
    {
        await PruefenAsync(Finanzrollen.Buchen, abbruch);
        var offen = (await speicher.BelegeAsync(abbruch: abbruch))
            .Where(b => b.Festgeschrieben && b.DokumentId is null && b.AnonymisiertAm is null && b.Art is not (Belegart.Zahlungseingang or Belegart.Zahlungsausgang))
            .OrderBy(b => b.Nummer, StringComparer.Ordinal).ThenBy(b => b.Id)
            .Select(b => b.Id)
            .ToList();
        var nachgeholt = 0;
        foreach (var id in offen)
        {
            if (await speicher.BelegAsync(id, abbruch) is not { DokumentId: null } b || await EmpfaengerZuAsync(b, abbruch) is not { } empf)
            {
                continue;
            }

            var xml = b.Xml is null && b.OrganisationId is not null && b.Art == Belegart.Rechnung ? XRechnung.Erzeugen(b, empf, await EinstellungenAsync(abbruch)) : null;
            await DruckstueckAblegenAsync(b, empf, mailen, xml, abbruch);
            await protokoll.ProtokollierenAsync("Finanzen.Beleg", $"{b.Nummer}: Druckstück nachgeholt (beim Festschreiben gescheitert)", nameof(Beleg), b.Id, abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
            nachgeholt++;
        }

        return nachgeholt;
    }

    /// <summary>Storno statt Änderung (FA-126): Stornobeleg mit Begründung und Verweis, Ursprungsbeleg bleibt unverändert.</summary>
    public async Task<Beleg> StornierenAsync(Guid belegId, string grund, bool mailen = true, CancellationToken abbruch = default)
    {
        await PruefenAsync(Finanzrollen.Buchen, abbruch);
        if (string.IsNullOrWhiteSpace(grund))
        {
            throw new InvalidOperationException("Ein Storno braucht eine Begründung.");
        }

        var b = await speicher.BelegAsync(belegId, abbruch) ?? throw new InvalidOperationException("Beleg nicht gefunden.");
        return await StornoIntern(b, grund, mailen, abbruch);
    }

    internal async Task<Beleg> StornoIntern(Beleg b, string grund, bool mailen, CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var storno = b.StornoErzeugen(grund, Heute, uhr.GetLocalNow(), wer.Anzeige);
        speicher.Hinzufuegen(storno);
        var empfaenger = b.Art is Belegart.Rechnung or Belegart.Mahnung or Belegart.Honorarabrechnung ? await EmpfaengerZuAsync(b, abbruch) : null;
        await FestschreibenAsync(storno, empfaenger, mailen, abbruch: abbruch);
        b.StornoVermerken(storno);
        await finanzprotokoll.SchreibenAsync("Beleg.Storniert", BelegartInfo.Name(b.Art), b.Id, $"{b.Nummer} gültig", $"{b.Nummer} storniert durch {storno.Nummer}: {grund}",
            abbruch);
        await speicher.SpeichernAsync(abbruch);
        return storno;
    }

    /// <summary>Entwürfe (ohne Nummer) dürfen verworfen werden; festgeschriebene nie.</summary>
    public async Task EntwurfVerwerfenAsync(Guid belegId, CancellationToken abbruch = default)
    {
        await PruefenAsync(Finanzrollen.Buchen, abbruch);
        var b = await speicher.BelegAsync(belegId, abbruch) ?? throw new InvalidOperationException("Beleg nicht gefunden.");
        if (b.Festgeschrieben)
        {
            throw new InvalidOperationException($"{b.Nummer} ist festgeschrieben und wird nie gelöscht – bitte stornieren.");
        }

        speicher.Entfernen(b);
        await protokoll.ProtokollierenAsync("Finanzen.EntwurfVerworfen", $"Entwurf {BelegartInfo.Name(b.Art)} über {new Geld(b.BetragCent)} verworfen", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    // ---------- Konto ----------

    public async Task<Personenkonto> KontoAsync(Guid parteiId, CancellationToken abbruch = default)
    {
        var belege = await speicher.BelegeAsync(parteiId, abbruch);
        var alle = belege.ToDictionary(b => b.Id);
        var offen = Offenposten.Berechnen(belege);
        var zeilen = belege.Where(b => b.Festgeschrieben)
            .OrderBy(b => b.Belegdatum).ThenBy(b => b.FestgeschriebenAm)
            .Select(b => new Kontozeile(b, Offenposten.Wirkung(b, alle), offen.TryGetValue(b.Id, out var o) ? o : null,
                b.BezugBelegId is { } id && alle.TryGetValue(id, out var bezug) ? bezug.Nummer : null))
            .ToList();
        return new Personenkonto(parteiId, zeilen, belege.Where(b => !b.Festgeschrieben).ToList());
    }

    // ---------- Rechnungslauf ----------

    /// <summary>
    /// Automatik (täglich): Rechnungen für neue Belegungen, Storno bei Abmeldung oder Umbuchung, Gebührenrechnungen,
    /// Firmenrechnungen (mit XRechnung) für geschlossene Kurse. Kostenträger-Belegungen (z. B. BAMF) rechnen andere Wege ab.
    /// </summary>
    public async Task<Rechnungslaufergebnis> RechnungslaufAsync(bool mailen = true, CancellationToken abbruch = default)
    {
        await PruefenAsync(Finanzrollen.Buchen, abbruch);
        var einst = await EinstellungenAsync(abbruch);
        var heute = Heute;
        var jetzt = uhr.GetLocalNow();
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var belege = await speicher.BelegeAsync(abbruch: abbruch);
        var aktiveRechnungen = belege.Where(b => b.Art == Belegart.Rechnung && b.Festgeschrieben && !b.Storniert).ToList();
        var entgeltRechnung = new Dictionary<Guid, Guid>();
        var gebuehrAbgerechnet = new HashSet<Guid>();
        var firmenkurse = new HashSet<Guid>();
        foreach (var r in aktiveRechnungen)
        {
            foreach (var p in r.Positionen)
            {
                if (p.Art == Positionsart.Kursentgelt && p.BelegungId is { } bid)
                {
                    entgeltRechnung[bid] = r.Id;
                }
                else if (p.Art == Positionsart.Gebuehr && p.BelegungId is { } gid)
                {
                    gebuehrAbgerechnet.Add(gid);
                }
                else if (p.Art == Positionsart.Kursentgelt && r.OrganisationId is not null && p.KursId is { } kid)
                {
                    firmenkurse.Add(kid);
                }
            }
        }

        var kurse = new Dictionary<Guid, Kurs?>();
        async Task<Kurs?> KursAsync(Guid id) => kurse.TryGetValue(id, out var k) ? k : kurse[id] = await programm.KursLadenAsync(id, abbruch);

        int neu = 0, stornos = 0, gebuehren = 0, firmen = 0;
        long summe = 0;
        var belegungen = await speicher.AbrechenbareBelegungenAsync(abbruch);
        foreach (var b in belegungen.OrderBy(b => b.GebuchtAm))
        {
            var kurs = await KursAsync(b.KursId);
            if (kurs is null || kurs.AuftraggeberId is not null || b.Zahlart == Zahlart.Kostentraeger)
            {
                continue;
            }

            if (b.Status == Belegungsstatus.Angemeldet && b.EntgeltCent > 0 && !entgeltRechnung.ContainsKey(b.Id))
            {
                var r = await RechnungAsync(b.PersonId, [new Belegposition($"Kursentgelt {kurs.Kursnummer} {kurs.Titel}{(b.Ermaessigt ? " (ermäßigt)" : "")}", b.EntgeltCent,
                    belegungId: b.Id, kursId: kurs.Id, art: Positionsart.Kursentgelt)], einst, heute, jetzt, wer.Anzeige,
                    b.Zahlart == Zahlart.Lastschrift ? "Zahlung per SEPA-Lastschrift" : null, mailen, abbruch);
                if (r is not null)
                {
                    neu++;
                    summe += r.BetragCent;
                }
            }
            else if (b.Status is Belegungsstatus.Storniert or Belegungsstatus.Umgebucht)
            {
                if (entgeltRechnung.TryGetValue(b.Id, out var rid) && await speicher.BelegAsync(rid, abbruch) is { Storniert: false } alt)
                {
                    await StornoIntern(alt, b.Status == Belegungsstatus.Storniert ? $"Abmeldung vom Kurs {kurs.Kursnummer}" : $"Umbuchung aus Kurs {kurs.Kursnummer}", mailen, abbruch);
                    stornos++;
                }

                if (b.GebuehrCent is > 0 && !gebuehrAbgerechnet.Contains(b.Id))
                {
                    var art = b.Status == Belegungsstatus.Storniert ? "Bearbeitungsgebühr Abmeldung" : "Umbuchungsgebühr";
                    var r = await RechnungAsync(b.PersonId, [new Belegposition($"{art} {kurs.Kursnummer} {kurs.Titel}", b.GebuehrCent.Value, belegungId: b.Id, kursId: kurs.Id,
                        art: Positionsart.Gebuehr)], einst, heute, jetzt, wer.Anzeige, null, mailen, abbruch);
                    if (r is not null)
                    {
                        gebuehren++;
                        summe += r.BetragCent;
                    }
                }
            }
        }

        // Firmenkurse: eine Rechnung an den Auftraggeber, sobald der Kurs in 30 Tagen beginnt.
        foreach (var gruppe in belegungen.Where(b => b.Status == Belegungsstatus.Angemeldet).GroupBy(b => b.KursId))
        {
            var kurs = await KursAsync(gruppe.Key);
            if (kurs?.AuftraggeberId is not { } orgId || firmenkurse.Contains(kurs.Id) || kurs.Status != Kursstatus.Veroeffentlicht
                || kurs.ErsterTermin is not { } beginn || DateOnly.FromDateTime(beginn) > heute.AddDays(30))
            {
                continue;
            }

            var org = await speicher.OrganisationAsync(orgId, abbruch);
            var anzahl = gruppe.Count();
            var einzel = gruppe.Max(b => b.EntgeltCent) is > 0 and var m ? m : kurs.EntgeltAm(heute, jetzt)?.Cent ?? 0;
            if (org is null || einzel <= 0)
            {
                continue;
            }

            var empf = Firmenempfaenger(org);
            var r = new Beleg(Belegart.Rechnung, heute, null, org.Id, empf.Name, string.Join('\n', empf.Anschrift),
                [new Belegposition($"Firmenkurs {kurs.Kursnummer} {kurs.Titel} – {anzahl} Teilnehmende", einzel * anzahl, anzahl, einzel, kursId: kurs.Id, art: Positionsart.Kursentgelt)],
                jetzt, wer.Anzeige, heute.AddDays(einst.ZahlungszielTage), text: org.LeitwegId is null ? null : $"Leitweg-ID {org.LeitwegId}");
            speicher.Hinzufuegen(r);
            await FestschreibenAsync(r, empf, mailen, beleg => XRechnung.Erzeugen(beleg, empf, einst), abbruch);
            firmen++;
            summe += r.BetragCent;
        }

        if (neu + stornos + gebuehren + firmen > 0)
        {
            await protokoll.ProtokollierenAsync("Finanzen.Rechnungslauf",
                $"Rechnungslauf: {neu} Rechnungen, {firmen} Firmenrechnungen, {gebuehren} Gebührenrechnungen, {stornos} Stornos (zusammen {new Geld(summe)})", abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
        }

        return new(neu, stornos, gebuehren, firmen, summe);
    }

    private async Task<Beleg?> RechnungAsync(Guid personId, IReadOnlyList<Belegposition> positionen, Finanzeinstellungen einst, DateOnly heute, DateTimeOffset jetzt,
        string von, string? text, bool mailen, CancellationToken abbruch)
    {
        var person = await personen.LadenAsync(personId, abbruch);
        if (person is null || person.IstAnonymisiert)
        {
            return null;
        }

        var empf = Personenempfaenger(person, jetzt);
        var r = new Beleg(Belegart.Rechnung, heute, person.Id, null, empf.Name, string.Join('\n', empf.Anschrift), positionen, jetzt, von,
            heute.AddDays(einst.ZahlungszielTage), text: text);
        speicher.Hinzufuegen(r);
        await FestschreibenAsync(r, empf, mailen, abbruch: abbruch);
        return r;
    }

    // ---------- Empfänger ----------

    public static Belegempfaenger Personenempfaenger(Person person, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(person);
        var stand = person.StandAm(DateOnly.FromDateTime(jetzt.Date), jetzt);
        var anschrift = (stand.BriefAn ?? stand.Adressen.FirstOrDefault()) is { } a
            ? new[] { a.Zusatz, a.Strasse, $"{a.Postleitzahl} {a.Ort}" }.Where(z => !string.IsNullOrWhiteSpace(z)).Select(z => z!).ToList()
            : [];
        return new Belegempfaenger(person.Id, null, stand.NameMitTitel, [stand.NameMitTitel, .. anschrift], person.EMail, person.Sprache);
    }

    public static Belegempfaenger Firmenempfaenger(Organisation org)
    {
        ArgumentNullException.ThrowIfNull(org);
        var zeilen = (org.Rechnungsanschrift ?? org.Ort ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new Belegempfaenger(null, org.Id, org.Name, [org.Name, .. zeilen], org.RechnungsEMail, "de", org.LeitwegId);
    }

    public async Task<Belegempfaenger?> EmpfaengerZuAsync(Beleg b, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(b);
        if (b.PersonId is { } pid && await personen.LadenAsync(pid, abbruch) is { IstAnonymisiert: false } p)
        {
            return Personenempfaenger(p, uhr.GetLocalNow());
        }

        return b.OrganisationId is { } oid && await speicher.OrganisationAsync(oid, abbruch) is { } o ? Firmenempfaenger(o) : null;
    }

    // ---------- Mandate und Bankverbindungen ----------

    public async Task<SepaMandat> MandatAnlegenAsync(Guid personId, string kontoinhaber, string iban, string? bic, DateOnly unterschriebenAm,
        CancellationToken abbruch = default)
    {
        await PruefenAsync(Finanzrollen.Mandate, abbruch);
        return await MandatErfassenAsync(personId, kontoinhaber, iban, bic, unterschriebenAm, "erfasst", abbruch);
    }

    /// <summary>Mandat, das die Person selbst online erteilt hat (Webshop-Kasse); ohne Rollenprüfung, im Finanzprotokoll als „online“ vermerkt.</summary>
    public Task<SepaMandat> MandatOnlineErteiltAsync(Guid personId, string kontoinhaber, string iban, string? bic, CancellationToken abbruch = default)
        => MandatErfassenAsync(personId, kontoinhaber, iban, bic, Heute, "online erteilt (Webshop)", abbruch);

    private async Task<SepaMandat> MandatErfassenAsync(Guid personId, string kontoinhaber, string iban, string? bic, DateOnly unterschriebenAm, string herkunft,
        CancellationToken abbruch)
    {
        if (Iban.Fehler(iban) is { } fehler)
        {
            throw new InvalidOperationException(fehler);
        }

        if (unterschriebenAm > Heute)
        {
            throw new InvalidOperationException("Das Unterschriftsdatum liegt in der Zukunft.");
        }

        _ = await personen.LadenAsync(personId, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
        foreach (var alt in (await speicher.MandateAsync(personId, abbruch)).Where(m => m.Status == Mandatsstatus.Aktiv))
        {
            // Ein neues Mandat ersetzt das alte (z. B. neue Bank).
            if (await speicher.MandatAsync(alt.Id, abbruch) is { } m)
            {
                m.Widerrufen(Heute);
                await finanzprotokoll.SchreibenAsync("Mandat.Ersetzt", "SepaMandat", m.Id, $"{m.Referenz} aktiv", $"{m.Referenz} durch neues Mandat ersetzt", abbruch);
            }
        }

        var referenz = $"VHS-{Heute.Year}-{await speicher.MandateAnzahlAsync(abbruch) + 1:000000}";
        var mandat = new SepaMandat(personId, referenz, kontoinhaber, iban, bic, unterschriebenAm, uhr.GetLocalNow());
        speicher.Hinzufuegen(mandat);
        await finanzprotokoll.SchreibenAsync("Mandat.Angelegt", "SepaMandat", mandat.Id, null,
            $"{referenz}; {Pseudonym.Person(personId)}; {Iban.Maskiert(mandat.Iban)}; unterschrieben {unterschriebenAm:yyyy-MM-dd}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Mandat", $"SEPA-Mandat {referenz} für {Pseudonym.Person(personId)} {herkunft}", nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return mandat;
    }

    public async Task<string?> MandatReferenzAsync(Guid mandatId, CancellationToken abbruch = default) => (await speicher.MandatAsync(mandatId, abbruch))?.Referenz;

    public async Task MandatWiderrufenAsync(Guid mandatId, CancellationToken abbruch = default, Guid? selbstPersonId = null)
    {
        if (selbstPersonId is null)
        {
            await PruefenAsync(Finanzrollen.Mandate, abbruch);
        }

        var m = await speicher.MandatAsync(mandatId, abbruch) ?? throw new InvalidOperationException("Mandat nicht gefunden.");
        if (selbstPersonId is { } p && m.PersonId != p)
        {
            throw new InvalidOperationException("Dieses Mandat gehört nicht zu Ihnen.");
        }

        if (m.Status != Mandatsstatus.Aktiv)
        {
            throw new InvalidOperationException("Das Mandat ist bereits widerrufen.");
        }

        m.Widerrufen(Heute);
        await finanzprotokoll.SchreibenAsync("Mandat.Widerrufen", "SepaMandat", m.Id, $"{m.Referenz} aktiv", $"{m.Referenz} widerrufen am {Heute:yyyy-MM-dd}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Mandat", $"SEPA-Mandat {m.Referenz} widerrufen", nameof(Person), m.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task BankverbindungSetzenAsync(Guid personId, string kontoinhaber, string iban, string? bic, CancellationToken abbruch = default)
    {
        await PruefenAsync(Finanzrollen.Mandate, abbruch);
        if (Iban.Fehler(iban) is { } fehler)
        {
            throw new InvalidOperationException(fehler);
        }

        var bv = await speicher.BankverbindungAsync(personId, abbruch);
        var vorher = bv is null ? null : Iban.Maskiert(bv.Iban);
        if (bv is null)
        {
            bv = new Bankverbindung(personId, kontoinhaber, iban, bic);
            speicher.Hinzufuegen(bv);
        }
        else
        {
            bv.Aendern(kontoinhaber, iban, bic);
        }

        await finanzprotokoll.SchreibenAsync("Bankverbindung.Gesetzt", "Bankverbindung", personId, vorher, $"{Pseudonym.Person(personId)}; {Iban.Maskiert(bv.Iban)}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Bankverbindung", $"Auszahlungskonto für {Pseudonym.Person(personId)} gesetzt", nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    // ---------- Firmenkunden ----------

    public async Task RechnungsdatenSetzenAsync(Guid organisationId, string? anschrift, string? leitwegId, string? email, CancellationToken abbruch = default)
    {
        await PruefenAsync(Finanzrollen.Buchen, abbruch);
        var org = await speicher.OrganisationAsync(organisationId, abbruch) ?? throw new InvalidOperationException("Organisation nicht gefunden.");
        org.RechnungsdatenSetzen(anschrift, leitwegId, email);
        await finanzprotokoll.SchreibenAsync("Firmenkunde.Rechnungsdaten", "Organisation", org.Id, null, $"Leitweg-ID {org.LeitwegId ?? "–"}; E-Rechnung an {org.RechnungsEMail ?? "–"}", abbruch);
        await speicher.SpeichernAsync(abbruch);
    }
}
