using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Shop;

namespace VibeVHS.Application.Shop;

public interface IShopSpeicher
{
    /// <summary>Veröffentlichte, öffentliche, nicht archivierte Kurse mit Terminen und Entgelten (ohne Verfolgung).</summary>
    Task<IReadOnlyList<Kurs>> OeffentlicheKurseAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Kursstand>> KursstaendeAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Warenkorb>> OffeneWarenkoerbeAsync(CancellationToken abbruch = default);

    Task<Warenkorb?> WarenkorbAsync(Guid id, CancellationToken abbruch = default);

    Task<Shopbuchung?> BuchungMitSchluesselAsync(string schluessel, CancellationToken abbruch = default);

    Task<Shopkonto?> KontoAsync(Guid id, CancellationToken abbruch = default);

    Task<Shopkonto?> KontoMitEMailAsync(string eMail, CancellationToken abbruch = default);

    Task<Shopkonto?> KontoMitTokenAsync(string tokenHash, CancellationToken abbruch = default);

    Task<IReadOnlyList<Shopkonto>> UnbestaetigteKontenAsync(CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    void Entfernen(object eintrag);

    /// <summary>Führt die Arbeit in einer Datenbanktransaktion aus (alles oder nichts).</summary>
    Task<T> InTransaktionAsync<T>(Func<Task<T>> arbeit, CancellationToken abbruch = default);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>
/// Öffentliche Buchung (Webshop, TYPO3, Portale): veröffentlichte Kursdaten mit Delta-Stand, Verfügbarkeit,
/// Warenkorb mit Platzreservierung, Buchung mit Idempotenzschlüssel und Kundenkonto mit Double-Opt-in.
/// Gebucht wird über <see cref="AnmeldungDienst"/> mit dem Buchungsweg „Online“ – dieselbe Prüfung wie in der Verwaltung.
/// </summary>
public sealed class ShopDienst(
    IShopSpeicher speicher,
    IProgrammSpeicher programm,
    IAnmeldungSpeicher anmeldungen,
    IStammdaten stammdaten,
    IPersonenSpeicher personen,
    AnmeldungDienst anmeldung,
    FinanzDienst finanzen,
    PostausgangDienst post,
    IAuditProtokoll protokoll,
    ShopOptionen optionen,
    ShopZwischenspeicher zwischenspeicher,
    TimeProvider uhr)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Von = "Webshop";

    public const string MandatText =
        "Ich ermächtige die vhs, Zahlungen von meinem Konto mittels Lastschrift einzuziehen. Zugleich weise ich mein Kreditinstitut an, die von der vhs auf mein Konto "
        + "gezogenen Lastschriften einzulösen. Hinweis: Ich kann innerhalb von acht Wochen, beginnend mit dem Belastungsdatum, die Erstattung des belasteten Betrages verlangen. "
        + "Es gelten dabei die mit meinem Kreditinstitut vereinbarten Bedingungen.";

    // ---------- Einstellungen ----------

    public async Task<Shopeinstellungen> EinstellungenAsync(CancellationToken abbruch = default)
        => new(optionen.AgbVersion, optionen.AgbUrl, optionen.DatenschutzVersion, optionen.DatenschutzUrl, optionen.MandatVersion, MandatText,
            optionen.ReservierungMinuten, (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "vhs");

    // ---------- Kursdaten ----------

    /// <summary>Öffentliche Kurse, optional gefiltert und als Delta seit einem Stand (inkl. entfernter Kursnummern).</summary>
    public async Task<Kursliste> KurseAsync(int? fachbereich, string? suche, string? ort, DateTimeOffset? geaendertSeit, CancellationToken abbruch = default)
    {
        var (kurse, staende) = await AbgleichenAsync(abbruch);
        var treffer = kurse.Where(k => (fachbereich is null || k.Kurs.Fachbereich == fachbereich)
                                       && Enthaelt(k.Kurs.Titel + " " + k.Kurs.Nummer + " " + k.Beschreibung, suche)
                                       && Enthaelt(string.Join(" ", k.Orte.Select(o => $"{o.Name} {o.Stadt}")), ort)
                                       && (geaendertSeit is null || k.Kurs.GeaendertAm > geaendertSeit))
            .Select(k => k.Kurs).OrderBy(k => k.Beginn ?? DateTime.MaxValue).ThenBy(k => k.Nummer, StringComparer.Ordinal).ToList();
        var entfernt = geaendertSeit is { } seit
            ? staende.Where(s => s.Entfernt && s.GeaendertAm > seit).Select(s => s.Kursnummer).Order(StringComparer.Ordinal).ToList()
            : [];
        var stand = staende.Count == 0 ? uhr.GetUtcNow() : staende.Max(s => s.GeaendertAm);
        return new Kursliste(stand, treffer, entfernt);
    }

    public async Task<KursDetail> KursAsync(string nummer, CancellationToken abbruch = default)
    {
        var (kurse, _) = await AbgleichenAsync(abbruch);
        return kurse.FirstOrDefault(k => string.Equals(k.Kurs.Nummer, nummer, StringComparison.OrdinalIgnoreCase))
               ?? throw new ShopFehler("kurs-unbekannt", $"Kurs {nummer} ist nicht (mehr) öffentlich.", 404);
    }

    /// <summary>
    /// Bringt den Veröffentlichungsstand auf den aktuellen Kursbestand: neue und geänderte Kurse bekommen einen neuen Zeitstempel,
    /// nicht mehr öffentliche werden als entfernt markiert. Ergebnis: alle öffentlichen Kurse als Detail.
    /// </summary>
    public async Task<(IReadOnlyList<KursDetail> Kurse, IReadOnlyList<Kursstand> Staende)> AbgleichenAsync(CancellationToken abbruch = default)
    {
        // Auf Millisekunden gekürzt: so gespeichert, wie er nach dem Laden wieder herauskommt (stabiler ETag).
        var jetzt = uhr.GetUtcNow();
        jetzt = new DateTimeOffset(jetzt.Ticks - (jetzt.Ticks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
        var lokal = uhr.GetLocalNow();
        var kurse = (await speicher.OeffentlicheKurseAsync(abbruch)).Where(k => !k.BeendetAm(lokal.DateTime)).ToList();
        var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
        var semester = (await programm.SemesterAsync(abbruch)).ToDictionary(s => s.Id, s => s.Name);
        var fachbereiche = (await stammdaten.FachbereicheAsync(abbruch)).ToDictionary(f => f.Nr, f => f.Name);
        var staende = (await speicher.KursstaendeAsync(abbruch)).ToDictionary(s => s.KursId);
        var geaendert = false;
        var details = new List<KursDetail>();
        foreach (var k in kurse)
        {
            var detail = Darstellen(k, verzeichnis, semester, fachbereiche, lokal, DateTimeOffset.MinValue);
            var hash = Hash(JsonSerializer.Serialize(detail, Json));
            if (!staende.TryGetValue(k.Id, out var stand))
            {
                stand = new Kursstand(k.Id, k.Kursnummer, hash, jetzt);
                speicher.Hinzufuegen(stand);
                staende[k.Id] = stand;
                geaendert = true;
            }
            else
            {
                geaendert |= stand.Aktualisieren(hash, jetzt);
            }

            details.Add(detail with { Kurs = detail.Kurs with { GeaendertAm = stand.GeaendertAm } });
        }

        var oeffentlich = kurse.Select(k => k.Id).ToHashSet();
        foreach (var s in staende.Values.Where(s => !oeffentlich.Contains(s.KursId)))
        {
            geaendert |= s.Entfernen(jetzt);
        }

        if (geaendert)
        {
            await speicher.SpeichernAsync(abbruch);
        }

        zwischenspeicher.LetzterAbgleich = jetzt;
        return (details, staende.Values.ToList());
    }

    private KursDetail Darstellen(Kurs k, Ortsverzeichnis verzeichnis, IReadOnlyDictionary<Guid, string> semester, IReadOnlyDictionary<int, string> fachbereiche,
        DateTimeOffset jetzt, DateTimeOffset geaendert)
    {
        var termine = k.Veranstaltungen.OrderBy(v => v.Beginn).ToList();
        var stattfindend = termine.Where(v => !v.Ausgefallen).ToList();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var orte = stattfindend.SelectMany(v => v.Orte.Select(o => o.OrtId)).Distinct()
            .Select(id => verzeichnis.Finden(id)).OfType<Ort>()
            .Select(o =>
            {
                var adresse = verzeichnis.Pfad(o).LastOrDefault(x => x.Strasse is not null);
                var merkmale = verzeichnis.WirksameMerkmale(o, heute, jetzt)
                    .Where(m => optionen.OeffentlicheMerkmalgruppen.Contains(m.Merkmal.Gruppe, StringComparer.Ordinal)).ToList();
                var geo = verzeichnis.Pfad(o).LastOrDefault(x => x.Breitengrad is not null && x.Laengengrad is not null);
                return new OrtInfo(o.Anzeige, adresse?.Strasse, adresse?.Postleitzahl, adresse?.Stadt, verzeichnis.Wegbeschreibung(o),
                    merkmale.Select(m => m.Merkmal.Name).ToList(), merkmale.Any(m => m.Merkmal.Schluessel == optionen.BarrierefreiMerkmal),
                    geo?.Breitengrad, geo?.Laengengrad);
            }).ToList();
        var entgelt = k.EntgeltFuerBuchung(jetzt);
        var fbName = fachbereiche.GetValueOrDefault(k.Fachbereich) ?? $"Fachbereich {k.Fachbereich}";
        var erster = orte.FirstOrDefault();
        var kurz = new KursKurz(k.Kursnummer, k.Titel, ShopText.Slug($"{k.Kursnummer}-{k.Titel}"), k.Fachbereich, fbName, ShopText.Slug(fbName),
            semester.GetValueOrDefault(k.SemesterId) ?? "", stattfindend.FirstOrDefault()?.Beginn, stattfindend.LastOrDefault()?.Ende, stattfindend.Count,
            ShopText.Zeiten(stattfindend), erster is null ? null : erster.Stadt is null ? erster.Name : $"{erster.Name}, {erster.Stadt}",
            entgelt?.Cent, entgelt?.ErmaessigtCent, orte.Count > 0 && orte.All(o => o.Barrierefrei), k.Richtlinie.Darf(Buchungsweg.Online), geaendert);
        return new KursDetail(kurz, k.Beschreibung, k.Bildungsurlaub, k.MinTeilnehmende, k.MaxTeilnehmende, k.Richtlinie.StornofristTage,
            k.Richtlinie.BearbeitungsgebuehrCent, k.Richtlinie.Warteliste,
            termine.Select(v => new TerminInfo(v.Nummer, v.Beginn, v.Ende, v.Orte.Select(o => o.OrtName).ToList(), v.Ausgefallen)).ToList(), orte);
    }

    // ---------- Verfügbarkeit ----------

    /// <summary>Freie Plätze (abzüglich Reservierungen) und Buchbarkeit mit Code und Begründung; 30 s zwischengespeichert.</summary>
    public async Task<Verfuegbarkeit> VerfuegbarkeitAsync(string nummer, CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetUtcNow();
        if (zwischenspeicher.Versuchen(nummer, jetzt, TimeSpan.FromSeconds(optionen.VerfuegbarkeitSekunden), out var gemerkt))
        {
            return gemerkt!;
        }

        var kurs = (await speicher.OeffentlicheKurseAsync(abbruch)).FirstOrDefault(k => string.Equals(k.Kursnummer, nummer, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ShopFehler("kurs-unbekannt", $"Kurs {nummer} ist nicht (mehr) öffentlich.", 404);
        var v = await BerechnenAsync(kurs, null, abbruch);
        zwischenspeicher.Merken(v);
        return v;
    }

    private async Task<Verfuegbarkeit> BerechnenAsync(Kurs kurs, Guid? ausserWarenkorb, CancellationToken abbruch)
    {
        var jetzt = uhr.GetLocalNow();
        var (belegt, wartend) = (await anmeldungen.BelegungszahlenAsync([kurs.Id], abbruch)).GetValueOrDefault(kurs.Id);
        var reserviert = await ReserviertAsync(kurs.Id, ausserWarenkorb, abbruch);
        var gast = Person.Anlegen("Gast", "Webshop", DateOnly.FromDateTime(jetzt.Date), jetzt);
        var pruefung = Buchungspruefung.Pruefen(kurs, gast, Buchungsweg.Online, null, new Buchungslage(belegt + reserviert, wartend), jetzt);
        var kontingent = kurs.Richtlinie.Belegung == Belegungsart.Kontingent;
        var frei = kontingent ? kurs.MaxTeilnehmende : Math.Max(0, kurs.MaxTeilnehmende - belegt - reserviert);
        var text = pruefung.Code switch
        {
            Buchungscode.Buchbar => frei == 1 ? "Noch 1 Platz frei." : $"Noch {frei} Plätze frei.",
            Buchungscode.Warteliste => $"Ausgebucht – Anmeldung auf die Warteliste möglich (Platz {wartend + 1}).",
            _ => pruefung.Erklaerung,
        };
        return new Verfuegbarkeit(kurs.Kursnummer, kurs.MaxTeilnehmende, belegt, reserviert, frei, wartend, pruefung.Code == Buchungscode.Warteliste,
            pruefung.Moeglich, Code(pruefung.Code), text, uhr.GetUtcNow());
    }

    public static string Code(Buchungscode c) => JsonNamingPolicy.KebabCaseLower.ConvertName(c.ToString());

    /// <summary>Plätze, die offene Warenkörbe gerade halten (Wartelistenpositionen halten keinen Platz).</summary>
    public async Task<int> ReserviertAsync(Guid kursId, Guid? ausserWarenkorb, CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetUtcNow();
        return (await speicher.OffeneWarenkoerbeAsync(abbruch))
            .Where(w => w.Id != ausserWarenkorb && w.Haelt(jetzt))
            .Sum(w => w.Positionen.Count(p => p.KursId == kursId && !p.Warteliste));
    }

    // ---------- Warenkorb ----------

    public async Task<WarenkorbAntwort> WarenkorbAnlegenAsync(IReadOnlyList<string> kursnummern, Guid? kontoId, CancellationToken abbruch = default)
    {
        var korb = new Warenkorb(uhr.GetUtcNow(), optionen.Reservierung, kontoId);
        speicher.Hinzufuegen(korb);
        return await PositionenSetzenAsync(korb, kursnummern, abbruch);
    }

    public async Task<WarenkorbAntwort> WarenkorbAsync(Guid id, CancellationToken abbruch = default)
        => await AntwortAsync(await KorbAsync(id, abbruch), abbruch);

    public async Task<WarenkorbAntwort> PositionenSetzenAsync(Guid id, IReadOnlyList<string> kursnummern, CancellationToken abbruch = default)
        => await PositionenSetzenAsync(await KorbAsync(id, abbruch), kursnummern, abbruch);

    private async Task<WarenkorbAntwort> PositionenSetzenAsync(Warenkorb korb, IReadOnlyList<string> kursnummern, CancellationToken abbruch)
    {
        if (kursnummern.Count > 10)
        {
            throw new ShopFehler("zu-viele-positionen", "Höchstens 10 Kurse je Warenkorb.", 422);
        }

        await zwischenspeicher.Sperre.WaitAsync(abbruch);
        try
        {
            var jetzt = uhr.GetUtcNow();
            if (!korb.Haelt(jetzt))
            {
                throw korb.Status == Warenkorbstatus.Gebucht
                    ? new ShopFehler("warenkorb-gebucht", "Dieser Warenkorb ist bereits gebucht.", 409)
                    : new ShopFehler("reservierung-abgelaufen", "Die Reservierung ist abgelaufen; bitte einen neuen Warenkorb anlegen.", 410);
            }

            var kurse = await speicher.OeffentlicheKurseAsync(abbruch);
            var positionen = new List<Warenkorbposition>();
            foreach (var nummer in kursnummern.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var kurs = kurse.FirstOrDefault(k => string.Equals(k.Kursnummer, nummer, StringComparison.OrdinalIgnoreCase))
                           ?? throw new ShopFehler("kurs-unbekannt", $"Kurs {nummer} ist nicht (mehr) öffentlich.", 404);
                var bisher = korb.Positionen.FirstOrDefault(p => p.KursId == kurs.Id);
                if (bisher is { Warteliste: false })
                {
                    // Der Platz ist schon reserviert und bleibt es.
                    positionen.Add(bisher);
                    continue;
                }

                var v = await BerechnenAsync(kurs, korb.Id, abbruch);
                if (!v.Buchbar)
                {
                    throw new ShopFehler("nicht-buchbar", $"{kurs.Kursnummer}: {v.Text}", 409,
                        new Dictionary<string, object?> { ["kursnummer"] = kurs.Kursnummer, ["buchungscode"] = v.Code });
                }

                positionen.Add(new Warenkorbposition(kurs.Id, kurs.Kursnummer, v.Warteliste));
            }

            korb.PositionenSetzen(positionen, jetzt, optionen.Reservierung);
            await speicher.SpeichernAsync(abbruch);
            foreach (var p in positionen.Concat(korb.Positionen))
            {
                zwischenspeicher.Vergessen(p.Kursnummer);
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new ShopFehler("warenkorb", ex.Message, 409);
        }
        finally
        {
            zwischenspeicher.Sperre.Release();
        }

        return await AntwortAsync(korb, abbruch);
    }

    private async Task<Warenkorb> KorbAsync(Guid id, CancellationToken abbruch)
        => await speicher.WarenkorbAsync(id, abbruch) ?? throw new ShopFehler("warenkorb-unbekannt", "Warenkorb nicht gefunden.", 404);

    private async Task<WarenkorbAntwort> AntwortAsync(Warenkorb korb, CancellationToken abbruch)
    {
        var jetzt = uhr.GetUtcNow();
        var kurse = (await speicher.OeffentlicheKurseAsync(abbruch)).ToDictionary(k => k.Id);
        var lokal = uhr.GetLocalNow();
        var positionen = korb.Positionen.Select(p =>
        {
            var k = kurse.GetValueOrDefault(p.KursId);
            var cent = k?.EntgeltFuerBuchung(lokal)?.Cent;
            return new WarenkorbPositionAntwort(p.Kursnummer, k?.Titel ?? p.Kursnummer, p.Warteliste, cent,
                p.Warteliste ? "Warteliste – das Entgelt wird erst bei Nachrücken fällig." : "Platz reserviert.");
        }).ToList();
        var status = korb.Status == Warenkorbstatus.Offen && !korb.Haelt(jetzt) ? "verfallen" : korb.Status.ToString().ToLowerInvariant();
        return new WarenkorbAntwort(korb.Id, status, korb.ReserviertBis, korb.Haelt(jetzt) ? (int)(korb.ReserviertBis - jetzt).TotalSeconds : 0, positionen,
            positionen.Where(p => !p.Warteliste).Sum(p => p.EntgeltCent ?? 0));
    }

    /// <summary>Abgelaufene Reservierungen freigeben (Job, jede Minute) und dokumentieren.</summary>
    public async Task<int> VerfallenAsync(CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetUtcNow();
        await zwischenspeicher.Sperre.WaitAsync(abbruch);
        try
        {
            var verfallen = (await speicher.OffeneWarenkoerbeAsync(abbruch)).Where(w => w.Verfallen(jetzt)).ToList();
            if (verfallen.Count > 0)
            {
                foreach (var p in verfallen.SelectMany(w => w.Positionen))
                {
                    zwischenspeicher.Vergessen(p.Kursnummer);
                }

                await protokoll.ProtokollierenAsync("Shop.ReservierungVerfallen",
                    $"{verfallen.Count} Warenkorb/Warenkörbe nach {optionen.ReservierungMinuten} Minuten verfallen, {verfallen.Sum(w => w.Positionen.Count(p => !p.Warteliste))} Plätze freigegeben",
                    abbruch: abbruch);
                await speicher.SpeichernAsync(abbruch);
            }

            return verfallen.Count;
        }
        finally
        {
            zwischenspeicher.Sperre.Release();
        }
    }

    // ---------- Buchung ----------

    /// <summary>
    /// Bucht den Warenkorb (alles oder nichts). Mit demselben Idempotenzschlüssel und derselben Anfrage kommt dasselbe Ergebnis zurück;
    /// mit abweichender Anfrage wird abgelehnt.
    /// </summary>
    /// <param name="nachweisPruefen">Bot-Schutz; nur für neue Buchungen geprüft, nicht für Wiederholungen mit bekanntem Schlüssel.</param>
    public async Task<BuchungsAntwort> BuchenAsync(Guid warenkorbId, BuchungsAnfrage anfrage, string? idempotenzschluessel, Guid? kontoId,
        CancellationToken abbruch = default, Action? nachweisPruefen = null)
    {
        ArgumentNullException.ThrowIfNull(anfrage);
        if (idempotenzschluessel is not { Length: >= 8 and <= 100 } schluessel)
        {
            throw new ShopFehler("idempotenz-schluessel-fehlt", "Der Header Idempotency-Key (8–100 Zeichen, z. B. eine UUID) ist Pflicht.", 400);
        }

        var hash = Hash(warenkorbId + "|" + kontoId + "|" + JsonSerializer.Serialize(anfrage, Json));
        await zwischenspeicher.Sperre.WaitAsync(abbruch);
        try
        {
            if (await speicher.BuchungMitSchluesselAsync(schluessel, abbruch) is { } frueher)
            {
                return frueher.AnfrageHash == hash
                    ? await AntwortAsync(frueher, true, abbruch)
                    : throw new ShopFehler("idempotenz-konflikt", "Dieser Idempotency-Key wurde bereits für eine andere Anfrage verwendet.", 422);
            }

            nachweisPruefen?.Invoke();

            var korb = await KorbAsync(warenkorbId, abbruch);
            var jetzt = uhr.GetUtcNow();
            if (korb.Status == Warenkorbstatus.Gebucht)
            {
                throw new ShopFehler("warenkorb-gebucht", "Dieser Warenkorb ist bereits gebucht.", 409);
            }

            if (!korb.Haelt(jetzt))
            {
                throw new ShopFehler("reservierung-abgelaufen", "Die Reservierung ist abgelaufen; bitte die Kurse erneut in den Warenkorb legen.", 410);
            }

            if (korb.Positionen.Count == 0)
            {
                throw new ShopFehler("warenkorb-leer", "Der Warenkorb ist leer.", 422);
            }

            if (korb.KontoId is { } k && k != kontoId)
            {
                throw new ShopFehler("konto", "Dieser Warenkorb gehört zu einem Kundenkonto; bitte anmelden.", 403);
            }

            var einwilligungen = EinwilligungenPruefen(anfrage, jetzt);
            var zahlart = anfrage.Zahlart?.Trim().ToLowerInvariant() switch
            {
                "lastschrift" => Zahlart.Lastschrift,
                "rechnung" or "ueberweisung" => Zahlart.Ueberweisung,
                _ => throw new ShopFehler("validierung", "Zahlart „lastschrift“ oder „rechnung“ wählen.", 422, Feld("zahlart")),
            };
            if (zahlart == Zahlart.Lastschrift)
            {
                if (anfrage.Lastschrift is not { } l || string.IsNullOrWhiteSpace(l.Kontoinhaber))
                {
                    throw new ShopFehler("validierung", "Für die Lastschrift Kontoinhaber und IBAN angeben.", 422, Feld("lastschrift.kontoinhaber"));
                }

                if (Iban.Fehler(l.Iban) is { } f)
                {
                    throw new ShopFehler("validierung", f, 422, Feld("lastschrift.iban"));
                }
            }

            var konto = kontoId is { } kid
                ? await speicher.KontoAsync(kid, abbruch) is { Status: Kontostatus.Aktiv, PersonId: not null } a ? a
                    : throw new ShopFehler("konto", "Das Kundenkonto ist nicht bestätigt.", 403)
                : null;
            if (konto is null)
            {
                TeilnehmerPruefen(anfrage.Teilnehmer);
            }

            var antwort = await speicher.InTransaktionAsync(async () =>
            {
                var person = konto is not null
                    ? await personen.LadenAsync(konto.PersonId!.Value, abbruch) ?? throw new ShopFehler("konto", "Die Person zum Konto fehlt.", 409)
                    : await PersonAnlegenAsync(anfrage.Teilnehmer!, abbruch);
                if (konto is not null && anfrage.Teilnehmer is { } t && !string.IsNullOrWhiteSpace(t.Strasse)
                    && person.Adressen.Count == 0)
                {
                    person.AdresseHinzufuegen(null, t.Strasse.Trim(), t.Postleitzahl.Trim(), t.Ort.Trim(), Heute, Erfassung.Erst(uhr.GetLocalNow(), Von));
                }

                var werbung = einwilligungen.FirstOrDefault(e => e.Art == "werbung");
                if (werbung is not null)
                {
                    person.EinwilligungSetzenAb(Kontaktkanal.EMail, Kontaktzweck.Werbung, werbung.Erteilt, Heute, Erfassung.Erst(uhr.GetLocalNow(), Von, "Einwilligung im Webshop"));
                }

                await personen.SpeichernAsync(abbruch);

                SepaMandat? mandat = null;
                if (zahlart == Zahlart.Lastschrift)
                {
                    var l = anfrage.Lastschrift!;
                    mandat = await finanzen.MandatOnlineErteiltAsync(person.Id, l.Kontoinhaber.Trim(), l.Iban, string.IsNullOrWhiteSpace(l.Bic) ? null : l.Bic.Trim(), abbruch);
                }

                var ergebnisse = new List<Buchungsposition>();
                foreach (var p in korb.Positionen)
                {
                    try
                    {
                        var e = await anmeldung.AnmeldenAsync(new AnmeldeAuftrag(person.Id, p.KursId, zahlart, Weg: Buchungsweg.Online, Warenkorb: korb.Id), abbruch);
                        ergebnisse.Add(new Buchungsposition(p.Kursnummer, e.Belegung.Id, e.Belegung.Status == Belegungsstatus.Warteliste ? "warteliste" : "angemeldet",
                            e.Pruefung.Code == Buchungscode.Warteliste ? e.Pruefung.Erklaerung : null));
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new ShopFehler("nicht-buchbar", $"{p.Kursnummer}: {ex.Message}", 409, new Dictionary<string, object?> { ["kursnummer"] = p.Kursnummer });
                    }
                }

                korb.AlsGebuchtMarkieren(jetzt);
                var buchung = new Shopbuchung(schluessel, hash, korb.Id, person.Id, kontoId, zahlart == Zahlart.Lastschrift ? "lastschrift" : "rechnung", mandat?.Id,
                    einwilligungen, ergebnisse, jetzt);
                speicher.Hinzufuegen(buchung);
                await protokoll.ProtokollierenAsync("Shop.Buchung",
                    $"Webshop-Buchung {Pseudonym.Kurz(buchung.Id)} für {Pseudonym.Person(person.Id)}: {string.Join(", ", ergebnisse.Select(e => $"{e.Kursnummer} ({e.Status})"))}"
                    + $" · {(konto is null ? "Gast" : "Kundenkonto")} · Einwilligungen {string.Join(", ", einwilligungen.Select(e => $"{e.Art} {e.Version} {(e.Erteilt ? "ja" : "nein")}"))}",
                    nameof(Person), person.Id, abbruch: abbruch);
                await speicher.SpeichernAsync(abbruch);
                return new BuchungsAntwort(buchung.Id, korb.Id, jetzt, buchung.Zahlart, mandat?.Referenz, ergebnisse, false);
            }, abbruch);
            foreach (var p in korb.Positionen)
            {
                zwischenspeicher.Vergessen(p.Kursnummer);
            }

            return antwort;
        }
        finally
        {
            zwischenspeicher.Sperre.Release();
        }
    }

    private async Task<BuchungsAntwort> AntwortAsync(Shopbuchung b, bool wiederholt, CancellationToken abbruch)
    {
        var referenz = b.MandatId is { } mid ? await finanzen.MandatReferenzAsync(mid, abbruch) : null;
        return new BuchungsAntwort(b.Id, b.WarenkorbId, b.Zeitpunkt, b.Zahlart, referenz, b.Positionen, wiederholt);
    }

    private List<Einwilligungsnachweis> EinwilligungenPruefen(BuchungsAnfrage anfrage, DateTimeOffset jetzt)
    {
        var liste = (anfrage.Einwilligungen ?? []).Select(e => new Einwilligungsnachweis(e.Art.Trim().ToLowerInvariant(), e.Version.Trim(), e.Erteilt, jetzt)).ToList();
        var pflicht = new List<(string Art, string Version, string Text)>
        {
            ("agb", optionen.AgbVersion, "die Allgemeinen Geschäftsbedingungen"),
            ("datenschutz", optionen.DatenschutzVersion, "die Datenschutzhinweise"),
        };
        if (string.Equals(anfrage.Zahlart, "lastschrift", StringComparison.OrdinalIgnoreCase))
        {
            pflicht.Add(("sepa", optionen.MandatVersion, "das SEPA-Lastschriftmandat"));
        }

        foreach (var (art, version, text) in pflicht)
        {
            var e = liste.FirstOrDefault(x => x.Art == art);
            if (e is not { Erteilt: true })
            {
                throw new ShopFehler("einwilligung-fehlt", $"Bitte {text} bestätigen.", 422, Feld($"einwilligungen.{art}"));
            }

            if (e.Version != version)
            {
                throw new ShopFehler("einwilligung-veraltet", $"Es gilt eine neue Fassung ({version}) für {text}; bitte die Seite neu laden.", 422,
                    new Dictionary<string, object?> { ["feld"] = $"einwilligungen.{art}", ["version"] = version });
            }
        }

        return liste.Where(e => e.Art is "agb" or "datenschutz" or "sepa" or "werbung").ToList();
    }

    private static void TeilnehmerPruefen(TeilnehmerAngabe? t)
    {
        if (t is null)
        {
            throw new ShopFehler("validierung", "Angaben zur teilnehmenden Person fehlen.", 422, Feld("teilnehmer"));
        }

        foreach (var (wert, feld, text) in new[]
                 {
                     (t.Vorname, "vorname", "Vorname"), (t.Nachname, "nachname", "Nachname"), (t.Strasse, "strasse", "Straße und Hausnummer"),
                     (t.Postleitzahl, "postleitzahl", "Postleitzahl"), (t.Ort, "ort", "Ort"),
                 })
        {
            if (string.IsNullOrWhiteSpace(wert) || wert.Length > 200)
            {
                throw new ShopFehler("validierung", $"Bitte {text} angeben.", 422, Feld($"teilnehmer.{feld}"));
            }
        }

        if (!GueltigeEMail(t.EMail))
        {
            throw new ShopFehler("validierung", "Bitte eine gültige E-Mail-Adresse angeben.", 422, Feld("teilnehmer.email"));
        }
    }

    private async Task<Person> PersonAnlegenAsync(TeilnehmerAngabe t, CancellationToken abbruch)
    {
        // Gäste werden neu angelegt; mögliche Dubletten findet die Dublettenprüfung der Verwaltung.
        var jetzt = uhr.GetLocalNow();
        var erfassung = Erfassung.Erst(jetzt, Von);
        var person = Person.Anlegen(t.Vorname.Trim(), t.Nachname.Trim(), Heute, jetzt, t.Geburtsdatum, t.EMail.Trim(), Sprache(t.Sprache), Von);
        person.RolleHinzufuegen(Personenrolle.Teilnehmende, Heute, erfassung);
        person.AdresseHinzufuegen(null, t.Strasse.Trim(), t.Postleitzahl.Trim(), t.Ort.Trim(), Heute, erfassung);
        personen.Hinzufuegen(person);
        await protokoll.ProtokollierenAsync("Shop.PersonAngelegt", $"{Pseudonym.Person(person.Id)} über den Webshop angelegt (Gastbuchung)", nameof(Person), person.Id,
            abbruch: abbruch);
        return person;
    }

    // ---------- Kundenkonto ----------

    /// <summary>
    /// Registrierung mit Double-Opt-in. Antwortet immer gleich (keine Auskunft, ob ein Konto existiert); die Mail enthält den Bestätigungslink
    /// bzw. den Hinweis auf das bestehende Konto.
    /// </summary>
    public async Task RegistrierenAsync(string eMail, string vorname, string nachname, DateOnly? geburtsdatum, string? sprache, Func<string, Uri> bestaetigungslink,
        CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(bestaetigungslink);
        if (!GueltigeEMail(eMail))
        {
            throw new ShopFehler("validierung", "Bitte eine gültige E-Mail-Adresse angeben.", 422, Feld("email"));
        }

        if (string.IsNullOrWhiteSpace(vorname) || string.IsNullOrWhiteSpace(nachname))
        {
            throw new ShopFehler("validierung", "Bitte Vor- und Nachnamen angeben.", 422, Feld(string.IsNullOrWhiteSpace(vorname) ? "vorname" : "nachname"));
        }

        var jetzt = uhr.GetUtcNow();
        var gueltig = TimeSpan.FromHours(optionen.DoiStunden);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var konto = await speicher.KontoMitEMailAsync(Shopkonto.Normalisieren(eMail), abbruch);
        var sprach = Sprache(sprache);
        var einrichtung = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "vhs";
        if (konto is { Status: Kontostatus.Aktiv })
        {
            await post.KundenmailEinreihenAsync(konto.EMail, $"{konto.Vorname} {konto.Nachname}", "Ihr Kundenkonto besteht bereits",
                $"Guten Tag {konto.Vorname} {konto.Nachname},\n\njemand – vermutlich Sie – wollte mit dieser Adresse ein Kundenkonto anlegen. Sie haben bereits eines und können sich direkt anmelden.\n"
                + $"Wenn Sie das nicht waren, müssen Sie nichts tun.\n\nMit freundlichen Grüßen\n{einrichtung}", konto.Sprache, konto.PersonId, abbruch);
        }
        else
        {
            if (konto is null)
            {
                konto = new Shopkonto(eMail, vorname, nachname, geburtsdatum, sprach, token, jetzt, gueltig);
                speicher.Hinzufuegen(konto);
            }
            else
            {
                konto.NeuerToken(token, jetzt, gueltig);
            }

            await post.KundenmailEinreihenAsync(konto.EMail, $"{konto.Vorname} {konto.Nachname}", "Bitte bestätigen Sie Ihr Kundenkonto",
                $"Guten Tag {konto.Vorname} {konto.Nachname},\n\nbitte bestätigen Sie Ihre E-Mail-Adresse, damit wir Ihr Kundenkonto anlegen können:\n{bestaetigungslink(token)}\n\n"
                + $"Der Link gilt {optionen.DoiStunden} Stunden. Ohne Bestätigung löschen wir Ihre Angaben danach automatisch.\n"
                + $"Wenn Sie sich nicht registriert haben, ignorieren Sie diese Mail einfach.\n\nMit freundlichen Grüßen\n{einrichtung}", konto.Sprache, null, abbruch);
            await protokoll.ProtokollierenAsync("Shop.Registrierung", $"Kundenkonto #{Pseudonym.Kurz(konto.Id)} registriert, Bestätigungslink verschickt (Double-Opt-in)",
                abbruch: abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Öffnen des Bestätigungslinks: Konto aktiv, Person angelegt (Rolle Teilnehmende).</summary>
    public async Task<Shopkonto> BestaetigenAsync(string token, CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetUtcNow();
        var konto = await speicher.KontoMitTokenAsync(Shopkonto.Hash(token ?? ""), abbruch);
        if (konto is null || !konto.TokenPasst(token!, jetzt))
        {
            throw new ShopFehler("token-ungueltig", "Der Bestätigungslink ist ungültig oder abgelaufen. Bitte registrieren Sie sich erneut.", 410);
        }

        var lokal = uhr.GetLocalNow();
        var person = Person.Anlegen(konto.Vorname, konto.Nachname, Heute, lokal, konto.Geburtsdatum, konto.EMail, konto.Sprache, Von);
        person.RolleHinzufuegen(Personenrolle.Teilnehmende, Heute, Erfassung.Erst(lokal, Von));
        personen.Hinzufuegen(person);
        konto.Bestaetigen(person.Id, jetzt);
        await protokoll.ProtokollierenAsync("Shop.KontoBestaetigt", $"Kundenkonto #{Pseudonym.Kurz(konto.Id)} bestätigt, verknüpft mit {Pseudonym.Person(person.Id)}",
            nameof(Person), person.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return konto;
    }

    /// <summary>Anmeldung beim (Mock-)Identitätsdienst: nur bestätigte Konten.</summary>
    public async Task<Shopkonto?> AktivesKontoAsync(string eMail, CancellationToken abbruch = default)
        => await speicher.KontoMitEMailAsync(Shopkonto.Normalisieren(eMail ?? ""), abbruch) is { Status: Kontostatus.Aktiv } k ? k : null;

    public async Task<IReadOnlyList<KontoBuchung>> KontoBuchungenAsync(Guid kontoId, CancellationToken abbruch = default)
    {
        var konto = await AktivAsync(kontoId, abbruch);
        var jetzt = uhr.GetLocalNow();
        var liste = new List<KontoBuchung>();
        foreach (var pb in (await anmeldung.BelegungenDerPersonAsync(konto.PersonId!.Value, abbruch)).OrderByDescending(b => b.Beginn))
        {
            var b = pb.Belegung;
            Stornoregel? regel = null;
            if (b.Offen && await anmeldung.KursZurBelegungAsync(b, abbruch) is { } kurs)
            {
                regel = Buchungspruefung.Storno(b, kurs, jetzt);
            }

            liste.Add(new KontoBuchung(b.Id, b.Kursnummer, pb.Kurstitel, pb.Beginn, b.Status.ToString().ToLowerInvariant(), b.EntgeltCent, regel is not null,
                regel?.GebuehrCent, regel?.Erklaerung));
        }

        return liste;
    }

    /// <summary>Storno durch die Person selbst nach den AGB-Fristen der Richtlinie (Gebühr wie am Tresen).</summary>
    public async Task<Stornoregel> KontoStornierenAsync(Guid kontoId, Guid belegungId, string? grund, CancellationToken abbruch = default)
    {
        var konto = await AktivAsync(kontoId, abbruch);
        try
        {
            var regel = await anmeldung.SelbstStornierenAsync(belegungId, konto.PersonId!.Value, string.IsNullOrWhiteSpace(grund) ? "Abmeldung im Kundenkonto" : grund, abbruch);
            if (await anmeldung.BelegungLadenAsync(belegungId, abbruch) is { } b)
            {
                zwischenspeicher.Vergessen(b.Kursnummer);
            }

            return regel;
        }
        catch (InvalidOperationException ex)
        {
            throw new ShopFehler("storno", ex.Message, 409);
        }
    }

    private async Task<Shopkonto> AktivAsync(Guid kontoId, CancellationToken abbruch)
        => await speicher.KontoAsync(kontoId, abbruch) is { Status: Kontostatus.Aktiv, PersonId: not null } k
            ? k
            : throw new ShopFehler("konto", "Kein bestätigtes Kundenkonto.", 403);

    /// <summary>Unbestätigte Registrierungen nach Ablauf des Links löschen (Datensparsamkeit).</summary>
    public async Task<int> UnbestaetigteLoeschenAsync(CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetUtcNow();
        var alt = (await speicher.UnbestaetigteKontenAsync(abbruch)).Where(k => k.TokenGueltigBis <= jetzt).ToList();
        foreach (var k in alt)
        {
            speicher.Entfernen(k);
        }

        if (alt.Count > 0)
        {
            await protokoll.ProtokollierenAsync("Shop.RegistrierungGeloescht", $"{alt.Count} unbestätigte Registrierung(en) nach Ablauf des Bestätigungslinks gelöscht", abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
        }

        return alt.Count;
    }

    // ---------- Hilfen ----------

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    private static Dictionary<string, object?> Feld(string feld) => new() { ["feld"] = feld };

    private static bool Enthaelt(string text, string? suche)
        => string.IsNullOrWhiteSpace(suche)
           || suche.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => De.CompareInfo.IndexOf(text, t, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);

    private static string Sprache(string? s) => s is { Length: >= 2 and <= 5 } && s.All(c => char.IsAsciiLetter(c) || c == '-') ? s.ToLowerInvariant() : "de";

    public static bool GueltigeEMail(string? e)
        => e is { Length: >= 5 and <= 254 } && e.IndexOf('@', StringComparison.Ordinal) is var at && at > 0 && at < e.Length - 3
           && e.IndexOf('.', at, StringComparison.Ordinal) > at + 1 && !e.Any(char.IsWhiteSpace);

    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
