using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Dokumente;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Dokumente;

public sealed record AnwesenheitsTag(string Datum, string Zeit, string Status, decimal Ue);

/// <summary>Eingefrorener Datenstand einer Bescheinigung (wird als JSON archiviert).</summary>
public sealed record Bescheinigungsdaten(
    string Art,
    string Einrichtung,
    string Stichtag,
    string Name,
    string? Geburtsdatum,
    IReadOnlyList<string> Anschrift,
    string Kursnummer,
    string Kurstitel,
    string Zeitraum,
    string? Kursleitung,
    int TermineGesamt,
    decimal UeGesamt,
    int TermineAnwesend,
    decimal UeAnwesend,
    IReadOnlyList<AnwesenheitsTag> Tage,
    string? Bildungsurlaub,
    bool Zwischenstand,
    string AusgestelltAm);

/// <summary>
/// Dokumente: Bescheinigungen nach Stichtagsregel (Namen wie zum Kurszeitpunkt, Adresse aktuell), Teilnehmerausweis, Archiv mit
/// PDF, JSON-Datenstand und SHA-256. Nachdruck liefert das Original; neu ausgestellt wird nur bewusst und mit Begründung.
/// </summary>
public sealed class DokumentDienst(
    IDokumentSpeicher speicher,
    IAnmeldungSpeicher anmeldungen,
    IProgrammSpeicher programm,
    IPersonenSpeicher personen,
    IDurchfuehrungSpeicher durchfuehrung,
    IStammdaten stammdaten,
    IBriefPdf briefPdf,
    IUebersetzungslinks links,
    CheckinZugang zugang,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    public static readonly Rolle[] AusstellRollen = [Rolle.Teilnehmerservice, Rolle.IntegrationskursTeam, Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    public static readonly Rolle[] NeuausstellRollen = [Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    /// <summary>So lange ist die Übersetzungsseite eines Briefs erreichbar (QA-22).</summary>
    public static readonly TimeSpan Uebersetzbar = TimeSpan.FromDays(180);

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ---------- Bescheinigungen ----------

    /// <summary>Daten nach Stichtagsregel: Name der Person und der Kursleitung zum Kurszeitpunkt (letzter Termin bzw. heute), Anschrift aktuell.</summary>
    public async Task<Bescheinigungsdaten> BescheinigungsdatenAsync(Guid belegungId, Dokumentart art, CancellationToken abbruch = default)
    {
        var b = await anmeldungen.BelegungLadenAsync(belegungId, abbruch) ?? throw new InvalidOperationException("Belegung nicht gefunden.");
        var kurs = await programm.KursLadenAsync(b.KursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        var person = await personen.LadenAsync(b.PersonId, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var termine = kurs.Veranstaltungen.Where(v => !v.Ausgefallen).OrderBy(v => v.Beginn).ToList();
        var letzter = termine.LastOrDefault() is { } l ? DateOnly.FromDateTime(l.Ende) : heute;
        var stichtag = letzter < heute ? letzter : heute;
        var bisStichtag = termine.Where(v => v.Datum <= stichtag).ToList();

        var name = person.NameAm(stichtag, jetzt) ?? person.AktuellerName(jetzt);
        var stand = person.StandAm(heute, jetzt);
        var anschrift = stand.BriefAn is { } a
            ? new[] { a.Zusatz, a.Strasse, $"{a.Postleitzahl} {a.Ort}" }.Where(z => !string.IsNullOrWhiteSpace(z)).Select(z => z!).ToList()
            : [];
        string? leitung = null;
        if (kurs.Leitung is { } l2 && await personen.LadenAsync(l2.PersonId, abbruch) is { } dozent)
        {
            var n = dozent.NameAm(stichtag, jetzt) ?? dozent.AktuellerName(jetzt);
            leitung = dozent.IstAnonymisiert ? dozent.Anzeigename : string.IsNullOrEmpty(dozent.Titel) ? n.Anzeige : $"{dozent.Titel} {n.Anzeige}";
        }

        var anwesenheiten = (await durchfuehrung.AnwesenheitenDesKursesAsync(kurs.Id, abbruch)).Where(x => x.BelegungId == b.Id).ToList();
        var tage = bisStichtag.Select(v =>
        {
            var x = anwesenheiten.FirstOrDefault(y => y.VeranstaltungId == v.Id);
            return new AnwesenheitsTag(v.Beginn.ToString("ddd dd.MM.yyyy", De), $"{v.Beginn:HH:mm}–{v.Ende:HH:mm}",
                x is null ? "–" : AnwesenheitAnzeige.Status(x.Status), x?.Status == Anwesenheitsstatus.Anwesend ? x.Ue : 0);
        }).ToList();

        var einrichtung = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Volkshochschule";
        return new Bescheinigungsdaten(
            DokumentAnzeige.Art(art),
            einrichtung,
            stichtag.ToString("dd.MM.yyyy", De),
            string.IsNullOrEmpty(person.Titel) ? name.Anzeige : $"{person.Titel} {name.Anzeige}",
            (person.Geburtsdatum ?? stand.Geburtsdatum)?.ToString("dd.MM.yyyy", De),
            anschrift,
            kurs.Kursnummer,
            kurs.Titel,
            termine.Count == 0 ? "–" : $"{termine[0].Beginn:dd.MM.yyyy} – {termine[^1].Beginn:dd.MM.yyyy}",
            leitung,
            bisStichtag.Count,
            AnwesenheitAnzeige.Ue(bisStichtag.Sum(v => (int)(v.Ende - v.Beginn).TotalMinutes)),
            anwesenheiten.Count(x => x.Status == Anwesenheitsstatus.Anwesend && bisStichtag.Any(v => v.Id == x.VeranstaltungId)),
            anwesenheiten.Where(x => x.Status == Anwesenheitsstatus.Anwesend && bisStichtag.Any(v => v.Id == x.VeranstaltungId)).Sum(x => x.Ue),
            tage,
            kurs.Bildungsurlaub,
            letzter > heute,
            heute.ToString("dd.MM.yyyy", De));
    }

    public async Task<Dokument> BescheinigungErstellenAsync(Guid belegungId, Dokumentart art, CancellationToken abbruch = default)
    {
        await RolleAsync(AusstellRollen, abbruch);
        var vorhanden = (await speicher.ZurBelegungAsync(belegungId, abbruch)).FirstOrDefault(d => d.Art == art && d.ErsetztDurch is null);
        if (vorhanden is not null)
        {
            throw new InvalidOperationException(
                $"Es gibt bereits eine {DokumentAnzeige.Art(art)} vom {vorhanden.ErstelltAm.ToLocalTime():dd.MM.yyyy}. Bitte das Original nachdrucken oder bewusst neu ausstellen.");
        }

        return await BescheinigungAsync(belegungId, art, null, null, abbruch);
    }

    /// <summary>Bewusste Neuausstellung mit aktuellem Datenstand (z. B. nach Korrektur); das alte Dokument bleibt im Archiv.</summary>
    public async Task<Dokument> NeuAusstellenAsync(Guid dokumentId, string grund, CancellationToken abbruch = default)
    {
        await RolleAsync(NeuausstellRollen, abbruch);
        if (string.IsNullOrWhiteSpace(grund))
        {
            throw new InvalidOperationException("Eine Neuausstellung braucht eine Begründung.");
        }

        var alt = await speicher.LadenAsync(dokumentId, abbruch) ?? throw new InvalidOperationException("Dokument nicht gefunden.");
        if (alt.BelegungId is not { } belegungId || alt.Art is not (Dokumentart.Teilnahmebescheinigung or Dokumentart.Bildungsurlaub))
        {
            throw new InvalidOperationException("Nur Bescheinigungen können neu ausgestellt werden.");
        }

        if (alt.ErsetztDurch is not null)
        {
            throw new InvalidOperationException("Dieses Dokument wurde bereits durch eine Neuausstellung ersetzt.");
        }

        var neu = await BescheinigungAsync(belegungId, alt.Art, alt.Id, grund.Trim(), abbruch);
        alt.ErsetzenDurch(neu.Id);
        await speicher.SpeichernAsync(abbruch);
        return neu;
    }

    private async Task<Dokument> BescheinigungAsync(Guid belegungId, Dokumentart art, Guid? ersetzt, string? grund, CancellationToken abbruch)
    {
        var b = await anmeldungen.BelegungLadenAsync(belegungId, abbruch) ?? throw new InvalidOperationException("Belegung nicht gefunden.");
        var kurs = await programm.KursLadenAsync(b.KursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        if (art == Dokumentart.Bildungsurlaub && kurs.Bildungsurlaub is null)
        {
            throw new InvalidOperationException("Der Kurs ist nicht als Bildungsurlaub anerkannt.");
        }

        var d = await BescheinigungsdatenAsync(belegungId, art, abbruch);
        if (d.TermineAnwesend == 0)
        {
            throw new InvalidOperationException("Für diese Belegung ist noch keine Anwesenheit erfasst; eine Bescheinigung ist erst danach möglich.");
        }

        var person = await personen.LadenAsync(b.PersonId, abbruch);
        var titel = $"{DokumentAnzeige.Art(art)} {kurs.Kursnummer}{(d.Zwischenstand ? " (Zwischenstand)" : "")}{(ersetzt is null ? "" : $" – Neuausstellung vom {d.AusgestelltAm}")}";
        return await ArchivierenAsync(art, b.PersonId, kurs.Id, b.Id, titel, JsonSerializer.Serialize(d, Json), person?.Sprache,
            link => Bescheinigungsbrief(d, art, link, ersetzt is not null ? grund : null), ersetzt, grund, abbruch);
    }

    private static Brief Bescheinigungsbrief(Bescheinigungsdaten d, Dokumentart art, Uri link, string? neuausstellung)
    {
        var absaetze = new List<string>();
        if (art == Dokumentart.Bildungsurlaub)
        {
            absaetze.Add($"Hiermit wird bescheinigt, dass {d.Name}{(d.Geburtsdatum is null ? "" : $", geboren am {d.Geburtsdatum}")}, an der nach dem Arbeitnehmerweiterbildungsgesetz NRW (AWbG) anerkannten Bildungsveranstaltung „{d.Kurstitel}“ ({d.Kursnummer}) teilgenommen hat.");
            absaetze.Add($"Anerkennung: {d.Bildungsurlaub}. Die Teilnahme an den folgenden Tagen ist durch die Anwesenheitsliste belegt.");
        }
        else
        {
            absaetze.Add($"Hiermit wird bescheinigt, dass {d.Name}{(d.Geburtsdatum is null ? "" : $", geboren am {d.Geburtsdatum}")}, {(d.Zwischenstand ? "am laufenden Kurs" : "am Kurs")} „{d.Kurstitel}“ ({d.Kursnummer}) teilgenommen hat.");
            absaetze.Add(d.Zwischenstand
                ? $"Zwischenstand zum {d.Stichtag}: {d.TermineAnwesend} von {d.TermineGesamt} bisherigen Terminen besucht ({d.UeAnwesend:0.##} von {d.UeGesamt:0.##} Unterrichtseinheiten à 45 Minuten)."
                : $"Besucht wurden {d.TermineAnwesend} von {d.TermineGesamt} Terminen ({d.UeAnwesend:0.##} von {d.UeGesamt:0.##} Unterrichtseinheiten à 45 Minuten).");
        }

        if (neuausstellung is not null)
        {
            absaetze.Add($"Neuausstellung vom {d.AusgestelltAm}; Grund: {neuausstellung}. Sie ersetzt die frühere Ausfertigung.");
        }

        return new Brief(
            d.Einrichtung,
            "Volkshochschule · Bescheinigung",
            [d.Name, .. d.Anschrift],
            d.AusgestelltAm,
            art == Dokumentart.Bildungsurlaub ? "Bescheinigung über die Teilnahme an einer Bildungsveranstaltung (AWbG NRW)" : "Teilnahmebescheinigung",
            absaetze,
            [("Kurs", $"{d.Kursnummer} {d.Kurstitel}"), ("Zeitraum", d.Zeitraum), ("Kursleitung", d.Kursleitung ?? "–")],
            art == Dokumentart.Bildungsurlaub ? ["Datum", "Zeit", "Teilnahme", "UE"] : null,
            art == Dokumentart.Bildungsurlaub
                ? d.Tage.Select(t => (IReadOnlyList<string>)[t.Datum, t.Zeit, t.Status, t.Ue.ToString("0.##", De)]).ToList()
                : null,
            "Unterschrift / Stempel der Volkshochschule",
            "Diese Bescheinigung wurde maschinell erstellt und ist im Archiv der Volkshochschule mit einer Prüfsumme hinterlegt.",
            link);
    }

    // ---------- Teilnehmerausweis ----------

    public async Task<Dokument> TeilnehmerausweisAsync(Guid personId, CancellationToken abbruch = default)
    {
        await RolleAsync(AusstellRollen, abbruch);
        var person = await personen.LadenAsync(personId, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
        var jetzt = uhr.GetLocalNow();
        var stand = person.StandAm(DateOnly.FromDateTime(jetzt.Date), jetzt);
        var einrichtung = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Volkshochschule";
        var ausweis = zugang.AusweisErzeugen(personId);
        var anschrift = stand.BriefAn is { } a ? new[] { a.Zusatz, a.Strasse, $"{a.Postleitzahl} {a.Ort}" }.Where(z => !string.IsNullOrWhiteSpace(z)).Select(z => z!).ToList() : [];
        var daten = new { Art = "Teilnehmerausweis", Name = stand.NameMitTitel, Anschrift = anschrift, Ausweis = "neues Ausweis-Token (Pseudonym, widerrufbar)", Ausgestellt = jetzt.ToString("dd.MM.yyyy", De) };
        return await ArchivierenAsync(Dokumentart.Teilnehmerausweis, personId, null, null, $"Teilnehmerausweis {stand.NameMitTitel}", JsonSerializer.Serialize(daten, Json),
            person.Sprache, link => new Brief(einrichtung, "Volkshochschule · Teilnehmerausweis", [stand.NameMitTitel, .. anschrift], jetzt.ToString("dd.MM.yyyy", De),
                "Ihr Teilnehmerausweis",
                [
                    "Mit diesem Ausweis checken Sie in Ihren Kursen ein: Zeigen Sie den QR-Code der Kursleitung; sie scannt ihn mit der Anwesenheitsliste. Alternativ scannen Sie mit Ihrem Handy den Termin-QR im Raum (dafür einmal den persönlichen Link aus der Anmeldebestätigung öffnen).",
                    "Bitte geben Sie den Ausweis nicht weiter. Bei Verlust stellen wir Ihnen gern einen neuen aus.",
                ],
                [("Name", stand.NameMitTitel)], null, null, null, "Der QR-Code enthält nur ein Pseudonym und keine persönlichen Daten. Ein verlorener Ausweis kann gesperrt werden.", link, ausweis, stand.NameMitTitel),
            null, null, abbruch);
    }

    // ---------- Auskunft (Art. 15) ----------

    /// <summary>Archiviert die Auskunft als Brief mit Übersetzungs-QR und liefert das PDF.</summary>
    public async Task<Dokument> AuskunftArchivierenAsync(Datenschutz.Auskunft auskunft, Func<Uri, byte[]> pdf, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(auskunft);
        var person = await personen.LadenAsync(auskunft.PersonId, abbruch);
        var json = JsonSerializer.Serialize(auskunft, Json);
        var id = Ids.Neu();
        var token = NeuesToken();
        var link = links.FuerBrief(token, person?.Sprache);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var dok = new Dokument(id, Dokumentart.Auskunft, auskunft.PersonId, null, null, $"Auskunft nach Art. 15 DSGVO vom {auskunft.ErstelltAm:dd.MM.yyyy}", pdf(link), json,
            $"Auskunft über gespeicherte personenbezogene Daten (Art. 15 DSGVO) für {auskunft.Name}, erstellt am {auskunft.ErstelltAm:dd.MM.yyyy}.",
            token, uhr.GetUtcNow(), wer.Anzeige, Uebersetzbar);
        speicher.Hinzufuegen(dok);
        await protokoll.ProtokollierenAsync("Dokument.Archiviert", $"Auskunft für {Pseudonym.Person(auskunft.PersonId)} archiviert (SHA-256 {dok.Sha256[..12]}…)", "Person",
            auskunft.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return dok;
    }

    // ---------- Archiv ----------

    public Task<IReadOnlyList<Dokument>> DerPersonAsync(Guid personId, CancellationToken abbruch = default) => speicher.DerPersonAsync(personId, abbruch);

    public Task<IReadOnlyList<Dokument>> DesKursesAsync(Guid kursId, CancellationToken abbruch = default) => speicher.DesKursesAsync(kursId, abbruch);

    public Task<IReadOnlyList<Dokument>> ZurBelegungAsync(Guid belegungId, CancellationToken abbruch = default) => speicher.ZurBelegungAsync(belegungId, abbruch);

    /// <summary>Nachdruck: liefert das archivierte Original, nachdem der Hash geprüft wurde.</summary>
    public async Task<(string Dateiname, byte[] Pdf)> NachdruckAsync(Guid dokumentId, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (wer.Rolle is null or Rolle.Dozierende)
        {
            throw new InvalidOperationException("Archivdokumente drucken Verwaltung und Leitung.");
        }

        var d = await speicher.LadenAsync(dokumentId, abbruch) ?? throw new InvalidOperationException("Dokument nicht gefunden.");
        if (d.IstAnonymisiert)
        {
            throw new InvalidOperationException($"Das Dokument wurde am {d.AnonymisiertAm!.Value.ToLocalTime():dd.MM.yyyy} anonymisiert; der Inhalt ist gelöscht.");
        }

        if (!d.Unveraendert)
        {
            await protokoll.ProtokollierenAsync("Dokument.HashFehler", $"Archivdokument {d.Id.ToString()[..8]} weicht von der Prüfsumme ab", "Person", d.PersonId,
                erfolgreich: false, abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
            throw new InvalidOperationException("Das archivierte Dokument stimmt nicht mehr mit seiner Prüfsumme (SHA-256) überein und wird nicht ausgegeben. Bitte die Systemverwaltung informieren.");
        }

        await protokoll.ProtokollierenAsync("Dokument.Nachdruck", $"Nachdruck „{d.Titel}“ für {Pseudonym.Person(d.PersonId)} (Original vom {d.ErstelltAm.ToLocalTime():dd.MM.yyyy}, Prüfsumme bestätigt)",
            "Person", d.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return ($"{d.Art.ToString().ToLowerInvariant()}-{d.ErstelltAm.ToLocalTime():yyyy-MM-dd}-{d.Id.ToString()[..8]}.pdf", d.Pdf);
    }

    /// <summary>Für die öffentliche Übersetzungsseite (QR-Code auf dem Brief): Dokument zum Token, solange es gültig ist.</summary>
    public async Task<(Dokument? Dokument, string? Grund)> UebersetzungAsync(string token, CancellationToken abbruch = default)
    {
        var d = await speicher.ZumUebersetzungsTokenAsync(token, abbruch);
        return d is null || d.IstAnonymisiert
            ? (null, "Dieser Link ist ungültig. / This link is not valid.")
            : d.UebersetzbarBis < uhr.GetUtcNow()
                ? (null, $"Dieser Link ist am {d.UebersetzbarBis.ToLocalTime():dd.MM.yyyy} abgelaufen. Bitte wenden Sie sich an die Volkshochschule. / This link has expired.")
                : (d, null);
    }

    private async Task<Dokument> ArchivierenAsync(Dokumentart art, Guid personId, Guid? kursId, Guid? belegungId, string titel, string datenJson, string? sprache,
        Func<Uri, Brief> brief, Guid? ersetzt, string? grund, CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var token = NeuesToken();
        var b = brief(links.FuerBrief(token, sprache));
        var dok = new Dokument(Ids.Neu(), art, personId, kursId, belegungId, titel, briefPdf.Erzeugen(b), datenJson, b.Klartext(), token, uhr.GetUtcNow(), wer.Anzeige,
            Uebersetzbar, ersetzt, grund);
        speicher.Hinzufuegen(dok);
        await protokoll.ProtokollierenAsync(ersetzt is null ? "Dokument.Erstellt" : "Dokument.Neuausstellung",
            $"„{titel}“ für {Pseudonym.Person(personId)} erstellt und archiviert (SHA-256 {dok.Sha256[..12]}…){(grund is null ? "" : $" · Grund: {grund}")}", "Person", personId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return dok;
    }

    private static string NeuesToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private async Task RolleAsync(Rolle[] rollen, CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !rollen.Contains(r)))
        {
            throw new InvalidOperationException(Oberflaeche.Freigabe.NurFuer(wer.Rolle, rollen).Begruendung);
        }
    }
}
