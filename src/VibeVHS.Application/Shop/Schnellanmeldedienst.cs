using System.Globalization;
using System.Security.Cryptography;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Shop;

namespace VibeVHS.Application.Shop;

/// <summary>Bestätigungsseite der Schnellanmeldung.</summary>
public sealed record SchnellVorschau(
    string Kursnummer,
    string Titel,
    DateTime? Beginn,
    string? Zeiten,
    string? Ort,
    int Termine,
    long? EntgeltCent,
    bool Ermaessigt,
    Zahlart Zahlart,
    string ZahlartText,
    bool Buchbar,
    bool Warteliste,
    string Status,
    string Vorname,
    string AgbVersion,
    string AgbUrl,
    string DatenschutzVersion,
    string DatenschutzUrl);

public sealed record SchnellErgebnis(string Kursnummer, string Status, string Text, bool StornoLinkVerschickt);

public sealed record StornoVorschau(string Kursnummer, string Titel, string Status, bool Moeglich, long GebuehrCent, string Erklaerung);

/// <summary>
/// Schnellanmeldung: Wer über den persönlichen Link (oder ein gemerktes Gerät) kommt und einen Kurs-QR scannt, meldet sich mit
/// einem Klick an – ohne Namen und Adresse erneut einzugeben. Gleiche Buchungsprüfung, Reservierungen und Warteliste wie am Tresen
/// und im Webshop. Bei kostenpflichtigen Kursen geht sofort eine Mail mit Storno-Link (24 h kostenlos, danach nach AGB).
/// </summary>
public sealed class Schnellanmeldedienst(
    IShopSpeicher speicher,
    IAnmeldungSpeicher anmeldungen,
    IPersonenSpeicher personen,
    AnmeldungDienst anmeldung,
    IFinanzSpeicher finanzen,
    PostausgangDienst post,
    IAuditProtokoll protokoll,
    ShopOptionen optionen,
    ShopZwischenspeicher zwischenspeicher,
    TimeProvider uhr)
{
    /// <summary>So lange ist die Abmeldung über den Storno-Link kostenlos.</summary>
    public static readonly TimeSpan KostenfreieFrist = TimeSpan.FromHours(24);

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <param name="bevollmaechtigterId">Buchung für eine andere Person mit Buchungsvollmacht (Teilnehmendenportal).</param>
    public async Task<SchnellVorschau> VorschauAsync(Guid personId, string kursnummer, CancellationToken abbruch = default, Guid? bevollmaechtigterId = null)
    {
        var (kurs, person) = await LadenAsync(personId, kursnummer, abbruch);
        var jetzt = uhr.GetLocalNow();
        var pruefung = await PruefenAsync(kurs, person, abbruch, bevollmaechtigterId);
        var entgelt = kurs.EntgeltFuerBuchung(jetzt);
        var ermaessigt = (await anmeldung.ErmaessigungHeuteAsync(personId, abbruch)) is not null && entgelt?.ErmaessigtCent is not null;
        var zahlart = await ZahlartAsync(personId, abbruch);
        var termine = kurs.Veranstaltungen.Where(v => !v.Ausgefallen).OrderBy(v => v.Beginn).ToList();
        var status = pruefung.Code switch
        {
            Buchungscode.Buchbar => "Es ist ein Platz frei.",
            Buchungscode.Warteliste => "Der Kurs ist ausgebucht – Sie kommen auf die Warteliste.",
            _ => pruefung.Erklaerung,
        };
        return new SchnellVorschau(kurs.Kursnummer, kurs.Titel, termine.FirstOrDefault()?.Beginn, ShopText.Zeiten(termine),
            termine.FirstOrDefault()?.Orte.FirstOrDefault()?.OrtName, termine.Count, ermaessigt ? entgelt!.ErmaessigtCent : entgelt?.Cent, ermaessigt,
            zahlart, zahlart == Zahlart.Lastschrift ? "SEPA-Lastschrift (Ihr bestehendes Mandat)" : "Rechnung (Überweisung)",
            pruefung.Moeglich, pruefung.Code == Buchungscode.Warteliste, status, person.AktuellerName(jetzt).Vorname,
            optionen.AgbVersion, optionen.AgbUrl, optionen.DatenschutzVersion, optionen.DatenschutzUrl);
    }

    /// <param name="weg">„Link“ oder „Gerät“ (fürs Protokoll).</param>
    /// <param name="stornoLink">Baut aus dem Storno-Token den Link für die Mail.</param>
    public async Task<SchnellErgebnis> AnmeldenAsync(Guid personId, string kursnummer, string agbVersion, string datenschutzVersion, string weg,
        Func<string, Uri> stornoLink, CancellationToken abbruch = default, Guid? bevollmaechtigterId = null)
    {
        ArgumentNullException.ThrowIfNull(stornoLink);
        if (agbVersion != optionen.AgbVersion || datenschutzVersion != optionen.DatenschutzVersion)
        {
            throw new ShopFehler("einwilligung-veraltet", "Es gilt eine neue Fassung der AGB oder Datenschutzhinweise; bitte die Seite neu laden.", 422);
        }

        await zwischenspeicher.Sperre.WaitAsync(abbruch);
        try
        {
            var (kurs, person) = await LadenAsync(personId, kursnummer, abbruch);
            var zahlart = await ZahlartAsync(personId, abbruch);
            var jetzt = uhr.GetUtcNow();
            AnmeldeErgebnis e;
            try
            {
                e = await anmeldung.AnmeldenAsync(new AnmeldeAuftrag(personId, kurs.Id, zahlart, bevollmaechtigterId, Weg: Buchungsweg.Online), abbruch);
            }
            catch (InvalidOperationException ex)
            {
                throw new ShopFehler("nicht-buchbar", ex.Message, 409);
            }

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var eintrag = new Schnellanmeldung(personId, e.Belegung.Id, kurs.Kursnummer, weg, token,
                [new("agb", agbVersion, true, jetzt), new("datenschutz", datenschutzVersion, true, jetzt)], jetzt, KostenfreieFrist);
            speicher.Hinzufuegen(eintrag);
            var kostenpflichtig = e.Belegung.EntgeltCent > 0 && e.Belegung.Status != Belegungsstatus.Warteliste;
            if (kostenpflichtig && person.EMail is { Length: > 0 } mail && mail != "–" && !person.IstAnonymisiert)
            {
                var name = person.AktuellerName(uhr.GetLocalNow());
                await post.KundenmailEinreihenAsync(mail, person.Anzeigename, $"Ihre Schnellanmeldung: {kurs.Titel}",
                    $"Guten Tag {name.Vorname} {name.Nachname},\n\nSie haben sich soeben über Ihren persönlichen Link für „{kurs.Titel}“ ({kurs.Kursnummer}) angemeldet "
                    + $"(Entgelt {e.Belegung.Entgelt}).\n\nWaren Sie das nicht, oder möchten Sie es sich anders überlegen? Bis {(jetzt + KostenfreieFrist).ToLocalTime():dd.MM.yyyy HH:mm} Uhr "
                    + $"können Sie sich hier kostenlos wieder abmelden, danach gelten die Abmeldefristen der AGB:\n{stornoLink(token)}\n\n"
                    + "Ihr persönlicher Link ist nur für Sie bestimmt. Bitte geben Sie ihn nicht weiter; bei Verlust sperrt der Teilnehmerservice ihn sofort.",
                    person.Sprache, personId, abbruch);
            }

            await protokoll.ProtokollierenAsync("Shop.Schnellanmeldung",
                $"Schnellanmeldung {Pseudonym.Person(personId)} für {kurs.Kursnummer} über {(weg == "Gerät" ? "gemerktes Gerät" : "persönlichen Link")} und Kurs-QR "
                + $"({(e.Belegung.Status == Belegungsstatus.Warteliste ? "Warteliste" : "angemeldet")}) · AGB {agbVersion}, Datenschutz {datenschutzVersion}"
                + (kostenpflichtig ? " · Storno-Link verschickt" : ""), "Person", personId, abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
            zwischenspeicher.Vergessen(kurs.Kursnummer);
            var text = e.Belegung.Status == Belegungsstatus.Warteliste
                ? "Sie stehen auf der Warteliste. Wir melden uns, sobald ein Platz frei wird."
                : "Sie sind angemeldet. Die Bestätigung kommt per E-Mail" + (kostenpflichtig ? ", dazu eine Mail mit Storno-Link (24 Stunden kostenlos)." : ".");
            return new SchnellErgebnis(kurs.Kursnummer, e.Belegung.Status == Belegungsstatus.Warteliste ? "warteliste" : "angemeldet", text, kostenpflichtig);
        }
        finally
        {
            zwischenspeicher.Sperre.Release();
        }
    }

    public async Task<StornoVorschau> StornoVorschauAsync(string token, CancellationToken abbruch = default)
    {
        var (s, b, kurs) = await StornoLadenAsync(token, abbruch);
        if (s.StorniertAm is not null || !b.Offen)
        {
            return new StornoVorschau(s.Kursnummer, kurs.Titel, "storniert", false, 0, "Diese Anmeldung ist bereits abgemeldet.");
        }

        var regel = Regel(s, b, kurs);
        return new StornoVorschau(s.Kursnummer, kurs.Titel, b.Status.ToString().ToLowerInvariant(), true, regel.GebuehrCent, regel.Erklaerung);
    }

    public async Task<Stornoregel> StornierenAsync(string token, CancellationToken abbruch = default)
    {
        var (s, b, kurs) = await StornoLadenAsync(token, abbruch);
        if (s.StorniertAm is not null || !b.Offen)
        {
            throw new ShopFehler("storno", "Diese Anmeldung ist bereits abgemeldet.", 409);
        }

        var kostenfrei = s.Kostenfrei(uhr.GetUtcNow());
        var regel = await anmeldung.SelbstStornierenAsync(b.Id, s.PersonId, "Storno-Link der Schnellanmeldung", abbruch, kostenfrei, "Storno-Link (Schnellanmeldung)");
        s.Storniert(uhr.GetUtcNow());
        await protokoll.ProtokollierenAsync("Shop.SchnellanmeldungStorniert",
            $"Schnellanmeldung {Pseudonym.Person(s.PersonId)} für {s.Kursnummer} über den Storno-Link abgemeldet ({(kostenfrei ? "innerhalb 24 h, kostenlos" : regel.Erklaerung)})",
            "Person", s.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        zwischenspeicher.Vergessen(s.Kursnummer);
        return regel;
    }

    private Stornoregel Regel(Schnellanmeldung s, Belegung b, Kurs kurs)
        => s.Kostenfrei(uhr.GetUtcNow())
            ? new Stornoregel(0, $"Bis {s.KostenfreiBis.ToLocalTime():dd.MM.yyyy HH:mm} Uhr (24 Stunden nach der Anmeldung) ist die Abmeldung kostenlos.")
            : anmeldung.StornoregelFuer(b, kurs);

    private async Task<(Schnellanmeldung S, Belegung B, Kurs Kurs)> StornoLadenAsync(string token, CancellationToken abbruch)
    {
        var s = await speicher.SchnellanmeldungAsync(Shopkonto.Hash(token ?? ""), abbruch)
                ?? throw new ShopFehler("token-ungueltig", "Dieser Storno-Link ist ungültig.", 404);
        var b = await anmeldung.BelegungLadenAsync(s.BelegungId, abbruch) ?? throw new ShopFehler("token-ungueltig", "Die Anmeldung gibt es nicht mehr.", 404);
        var kurs = await anmeldung.KursZurBelegungAsync(b, abbruch) ?? throw new ShopFehler("token-ungueltig", "Der Kurs gibt es nicht mehr.", 404);
        return (s, b, kurs);
    }

    private async Task<(Kurs Kurs, Domain.Personen.Person Person)> LadenAsync(Guid personId, string kursnummer, CancellationToken abbruch)
    {
        var kurs = (await speicher.OeffentlicheKurseAsync(abbruch)).FirstOrDefault(k => string.Equals(k.Kursnummer, kursnummer, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ShopFehler("kurs-unbekannt", $"Kurs {kursnummer} ist nicht (mehr) öffentlich buchbar.", 404);
        var person = await personen.LadenAsync(personId, abbruch) ?? throw new ShopFehler("person", "Ihr persönlicher Zugang ist nicht mehr gültig.", 403);
        return (kurs, person);
    }

    private async Task<Buchungsergebnis> PruefenAsync(Kurs kurs, Domain.Personen.Person person, CancellationToken abbruch, Guid? bevollmaechtigterId = null)
    {
        var belegungen = await anmeldungen.BelegungenDesKursesAsync(kurs.Id, abbruch);
        var reserviert = (await speicher.OffeneWarenkoerbeAsync(abbruch)).Where(w => w.Haelt(uhr.GetUtcNow()))
            .Sum(w => w.Positionen.Count(p => p.KursId == kurs.Id && !p.Warteliste));
        var bestehend = belegungen.Where(b => b.PersonId == person.Id && b.Offen).Select(b => (Belegungsstatus?)b.Status).FirstOrDefault();
        return Buchungspruefung.Pruefen(kurs, person, Buchungsweg.Online, bevollmaechtigterId,
            new Buchungslage(belegungen.Count(b => b.HaeltPlatz) + reserviert, belegungen.Count(b => b.Status == Belegungsstatus.Warteliste), bestehend), uhr.GetLocalNow());
    }

    /// <summary>Bisherige Zahlart: Lastschrift nur mit aktivem Mandat, sonst Rechnung.</summary>
    private async Task<Zahlart> ZahlartAsync(Guid personId, CancellationToken abbruch)
    {
        var mandat = (await finanzen.MandateAsync(personId, abbruch)).Any(m => m.Status == Mandatsstatus.Aktiv);
        var letzte = (await anmeldungen.BelegungenDerPersonAsync(personId, abbruch)).OrderByDescending(b => b.Belegung.GebuchtAm).FirstOrDefault()?.Belegung.Zahlart;
        return mandat && letzte is null or Zahlart.Lastschrift ? Zahlart.Lastschrift : Zahlart.Ueberweisung;
    }
}
