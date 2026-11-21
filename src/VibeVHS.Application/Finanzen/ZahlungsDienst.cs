using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Finanzen;

public sealed record Einleseergebnis(Kontoauszug Auszug, int Umsaetze, int Sicher, int Vorschlaege);

/// <summary>
/// Zahlungsverkehr: Lastschriftläufe (pain.008) mit Vorabankündigung, Überweisungsläufe (pain.001) für Honorare,
/// Einlesen von Kontoauszügen (camt.053) mit Zuordnungsvorschlag, Rücklastschriften.
/// </summary>
public sealed partial class ZahlungsDienst(
    IFinanzSpeicher speicher,
    IFinanzprotokoll finanzprotokoll,
    FinanzDienst finanzen,
    IPersonenSpeicher personen,
    IStammdaten stammdaten,
    PostausgangDienst post,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    [GeneratedRegex(@"\b(RE|MA)-\d{4}-\d{5}\b")]
    private static partial Regex BelegnummerMuster();

    // ---------- Lastschrift ----------

    /// <summary>
    /// Lastschriftvorbereitung (QA-30): offene Rechnungen von Personen mit gültigem Mandat, die noch in keinem Lauf stehen.
    /// Ergebnis ist ein Vorschlag, den die Buchhaltung freigibt (QA-32). Gibt es schon einen offenen Vorschlag, wird er ergänzt.
    /// </summary>
    public async Task<Zahlungslauf?> LastschriftVorschlagAsync(CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var einst = await finanzen.EinstellungenAsync(abbruch);
        var belege = await speicher.BelegeAsync(abbruch: abbruch);
        var offen = Offenposten.Berechnen(belege);
        var laeufe = await speicher.ZahlungslaeufeAsync(abbruch);
        var inLauf = laeufe.Where(l => l.Art == Zahlungslaufart.Lastschrift && l.Status != Zahlungslaufstatus.Verworfen)
            .SelectMany(l => l.Aktiv.Select(p => (l, p)))
            .Where(x => x.l.Status != Zahlungslaufstatus.Exportiert || offen.GetValueOrDefault(x.p.BelegId) > 0)
            .Select(x => x.p.BelegId).ToHashSet();
        var mandate = (await speicher.MandateAsync(abbruch: abbruch)).Where(m => m.NutzbarAm(Heute)).GroupBy(m => m.PersonId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.UnterschriebenAm).First());
        var ausfuehrung = Heute.AddDays(einst.VorabankuendigungTage + 1);
        var neu = new List<Zahlungsposten>();
        foreach (var r in belege.Where(b => b.Art == Belegart.Rechnung && b.Festgeschrieben && !b.Storniert && b.PersonId is not null
                                          && b.Text?.Contains("Lastschrift", StringComparison.Ordinal) == true && offen.GetValueOrDefault(b.Id) > 0 && !inLauf.Contains(b.Id)))
        {
            if (!mandate.TryGetValue(r.PersonId!.Value, out var m))
            {
                continue;
            }

            var lauf6 = Guid.NewGuid().ToString("N")[..4];
            neu.Add(new Zahlungsposten(r.Id, r.Nummer!, m.PersonId, m.Kontoinhaber, m.Iban, m.Bic, offen[r.Id], $"{r.Nummer} {Ausschnitt(r.Positionen[0].Text, 90)}",
                $"{r.Nummer}-{lauf6}", m.Referenz, m.UnterschriebenAm, m.Sequenz, m.Id));
        }

        var vorschlag = laeufe.FirstOrDefault(l => l.Art == Zahlungslaufart.Lastschrift && l.Status == Zahlungslaufstatus.Vorschlag);
        if (neu.Count == 0)
        {
            return vorschlag;
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        Zahlungslauf lauf;
        if (vorschlag is not null && await speicher.ZahlungslaufAsync(vorschlag.Id, abbruch) is { } bestehend)
        {
            // Neuer Vorschlag ersetzt den alten (gleicher Inhalt plus neue Posten), damit nur ein Vorschlag wartet.
            bestehend.Verwerfen();
            lauf = new Zahlungslauf(Zahlungslaufart.Lastschrift, ausfuehrung, [.. bestehend.Posten, .. neu], uhr.GetLocalNow(), wer.Anzeige);
        }
        else
        {
            lauf = new Zahlungslauf(Zahlungslaufart.Lastschrift, ausfuehrung, neu, uhr.GetLocalNow(), wer.Anzeige);
        }

        speicher.Hinzufuegen(lauf);
        await finanzprotokoll.SchreibenAsync("Zahlungslauf.Vorschlag", "Lastschrift", lauf.Id, null, $"{lauf.NachrichtId}; {lauf.Aktiv.Count()} Posten; {new Geld(lauf.SummeCent)}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Lastschriftvorschlag", $"Lastschriftlauf vorgeschlagen: {lauf.Aktiv.Count()} Posten, {new Geld(lauf.SummeCent)} – wartet auf Freigabe",
            erfolgreich: false, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return lauf;
    }

    /// <summary>Überweisungsvorschlag: festgeschriebene, offene Honorarabrechnungen mit hinterlegter Bankverbindung.</summary>
    public async Task<(Zahlungslauf? Lauf, int OhneKonto)> UeberweisungVorschlagAsync(CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var belege = await speicher.BelegeAsync(abbruch: abbruch);
        var offen = Offenposten.Berechnen(belege);
        var laeufe = await speicher.ZahlungslaeufeAsync(abbruch);
        var inLauf = laeufe.Where(l => l.Art == Zahlungslaufart.Ueberweisung && l.Status != Zahlungslaufstatus.Verworfen)
            .SelectMany(l => l.Aktiv.Select(p => (l, p)))
            .Where(x => x.l.Status != Zahlungslaufstatus.Exportiert || offen.GetValueOrDefault(x.p.BelegId) > 0)
            .Select(x => x.p.BelegId).ToHashSet();
        var konten = (await speicher.BankverbindungenAsync(abbruch)).ToDictionary(b => b.PersonId);
        var posten = new List<Zahlungsposten>();
        var ohneKonto = 0;
        foreach (var h in belege.Where(b => (b.Art == Belegart.Honorarabrechnung || (b.Art == Belegart.Gutschrift && b.BezugBelegId is null))
                                          && b.Festgeschrieben && !b.Storniert && offen.GetValueOrDefault(b.Id) > 0 && !inLauf.Contains(b.Id)))
        {
            if (h.PersonId is not { } pid || !konten.TryGetValue(pid, out var k))
            {
                ohneKonto++;
                continue;
            }

            posten.Add(new Zahlungsposten(h.Id, h.Nummer!, pid, k.Kontoinhaber, k.Iban, k.Bic, offen[h.Id], $"{(h.Art == Belegart.Gutschrift ? "Erstattung" : "Honorar")} {h.Nummer}",
                $"{h.Nummer}-{Guid.NewGuid().ToString("N")[..4]}"));
        }

        if (posten.Count == 0)
        {
            return (null, ohneKonto);
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        var lauf = new Zahlungslauf(Zahlungslaufart.Ueberweisung, Heute.AddDays(1), posten, uhr.GetLocalNow(), wer.Anzeige);
        speicher.Hinzufuegen(lauf);
        await finanzprotokoll.SchreibenAsync("Zahlungslauf.Vorschlag", "Ueberweisung", lauf.Id, null, $"{lauf.NachrichtId}; {posten.Count} Posten; {new Geld(lauf.SummeCent)}", abbruch);
        await speicher.SpeichernAsync(abbruch);
        return (lauf, ohneKonto);
    }

    public async Task PostenAusschliessenAsync(Guid laufId, Guid belegId, bool ausgeschlossen, CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var lauf = await speicher.ZahlungslaufAsync(laufId, abbruch) ?? throw new InvalidOperationException("Zahlungslauf nicht gefunden.");
        lauf.Ausschliessen(belegId, ausgeschlossen);
        await finanzprotokoll.SchreibenAsync("Zahlungslauf.Posten", lauf.Art.ToString(), lauf.Id, null, $"{belegId.ToString()[..8]} {(ausgeschlossen ? "ausgeschlossen" : "wieder aufgenommen")}", abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>
    /// Freigabe. Bei Lastschriften gehen jetzt die Vorabankündigungen (Pre-Notification) mit Betrag, Fälligkeit,
    /// Mandatsreferenz und Gläubiger-ID an die Zahlungspflichtigen; die Fälligkeit liegt mindestens die eingestellte Frist später.
    /// </summary>
    public async Task FreigebenAsync(Guid laufId, CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var lauf = await speicher.ZahlungslaufAsync(laufId, abbruch) ?? throw new InvalidOperationException("Zahlungslauf nicht gefunden.");
        var einst = await finanzen.EinstellungenAsync(abbruch);
        var ausfuehrung = lauf.Art == Zahlungslaufart.Lastschrift
            ? new[] { lauf.Ausfuehrung, Heute.AddDays(einst.VorabankuendigungTage) }.Max()
            : new[] { lauf.Ausfuehrung, Heute.AddDays(1) }.Max();
        lauf.Freigeben(ausfuehrung, uhr.GetLocalNow(), wer.Anzeige);
        if (lauf.Art == Zahlungslaufart.Lastschrift)
        {
            var einrichtung = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Volkshochschule";
            foreach (var p in lauf.Aktiv)
            {
                if (await personen.LadenAsync(p.PersonId, abbruch) is { IstAnonymisiert: false, EMail: { Length: > 0 } mail } person && mail != "–")
                {
                    await post.KundenmailEinreihenAsync(mail, person.Anzeigename, $"Ankündigung SEPA-Lastschrift zu {p.BelegNummer}",
                        $"{person.Briefanrede(uhr.GetLocalNow())},\n\nwir ziehen am {ausfuehrung:dd.MM.yyyy} den Betrag von {new Geld(p.BetragCent)} für {p.BelegNummer} von Ihrem Konto {Iban.Maskiert(p.Iban)} ein.\n\n"
                        + $"Mandatsreferenz: {p.MandatReferenz}\nGläubiger-Identifikationsnummer: {einst.GlaeubigerId}\n\nBitte sorgen Sie für ausreichende Deckung. Bei Fragen erreichen Sie uns gern.\n\nMit freundlichen Grüßen\n{einrichtung}",
                        person.Sprache, person.Id, abbruch);
                }
            }
        }

        await finanzprotokoll.SchreibenAsync("Zahlungslauf.Freigegeben", lauf.Art.ToString(), lauf.Id, "Vorschlag",
            $"{lauf.NachrichtId}; {lauf.Aktiv.Count()} Posten; {new Geld(lauf.SummeCent)}; Ausführung {ausfuehrung:yyyy-MM-dd}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Zahlungslauf", $"{(lauf.Art == Zahlungslaufart.Lastschrift ? "Lastschriftlauf" : "Überweisungslauf")} freigegeben: {lauf.Aktiv.Count()} Posten, {new Geld(lauf.SummeCent)}"
            + (lauf.Art == Zahlungslaufart.Lastschrift ? $", Vorabankündigungen verschickt, Einzug am {ausfuehrung:dd.MM.yyyy}" : ""), abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Erzeugt die SEPA-Datei zur Übergabe an die Bank.</summary>
    public async Task<(string Datei, string Xml)> ExportierenAsync(Guid laufId, CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var lauf = await speicher.ZahlungslaufAsync(laufId, abbruch) ?? throw new InvalidOperationException("Zahlungslauf nicht gefunden.");
        var einst = await finanzen.EinstellungenAsync(abbruch);
        var xml = lauf.Art == Zahlungslaufart.Lastschrift ? SepaXml.Lastschrift(lauf, einst, uhr.GetLocalNow()) : SepaXml.Ueberweisung(lauf, einst, uhr.GetLocalNow());
        lauf.Exportieren(xml, uhr.GetLocalNow());
        foreach (var p in lauf.Aktiv.Where(p => p.MandatId is not null))
        {
            if (await speicher.MandatAsync(p.MandatId!.Value, abbruch) is { } m)
            {
                m.Genutzt(lauf.Ausfuehrung);
            }
        }

        await finanzprotokoll.SchreibenAsync("Zahlungslauf.Exportiert", lauf.Art.ToString(), lauf.Id, "Freigegeben",
            $"{lauf.NachrichtId}; SHA-256 {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml)))}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.SepaExport", $"SEPA-Datei {lauf.NachrichtId} erzeugt ({lauf.Aktiv.Count()} Posten, {new Geld(lauf.SummeCent)})", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return (Dateiname(lauf), xml);
    }

    public static string Dateiname(Zahlungslauf lauf)
    {
        ArgumentNullException.ThrowIfNull(lauf);
        return $"{lauf.NachrichtId}.{(lauf.Art == Zahlungslaufart.Lastschrift ? "pain.008" : "pain.001")}.xml";
    }

    public async Task VerwerfenAsync(Guid laufId, CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var lauf = await speicher.ZahlungslaufAsync(laufId, abbruch) ?? throw new InvalidOperationException("Zahlungslauf nicht gefunden.");
        lauf.Verwerfen();
        await finanzprotokoll.SchreibenAsync("Zahlungslauf.Verworfen", lauf.Art.ToString(), lauf.Id, null, lauf.NachrichtId, abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    // ---------- Kontoauszug ----------

    public async Task<Einleseergebnis> EinlesenAsync(string datei, byte[] inhalt, CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        ArgumentNullException.ThrowIfNull(inhalt);
        var sha = Convert.ToHexStringLower(SHA256.HashData(inhalt));
        var auszug = Camt053.Lesen(Encoding.UTF8.GetString(inhalt));
        if (await speicher.KontoauszugVorhandenAsync(sha, auszug.AuszugId, abbruch))
        {
            throw new InvalidOperationException($"Dieser Kontoauszug ({auszug.AuszugId}) wurde bereits eingelesen. Doppelte Buchungen werden so verhindert.");
        }

        var einst = await finanzen.EinstellungenAsync(abbruch);
        if (auszug.Iban is { } kontoIban && Iban.Normalisieren(kontoIban) != einst.Iban)
        {
            throw new InvalidOperationException($"Der Auszug gehört zum Konto {Iban.Maskiert(kontoIban)}, eingestellt ist {Iban.Maskiert(einst.Iban)}.");
        }

        var k = new Kontoauszug(datei, sha, auszug.AuszugId, auszug.Iban, auszug.Von, auszug.Bis, auszug.EndsaldoCent, uhr.GetLocalNow(), wer.Anzeige);
        speicher.Hinzufuegen(k);
        var umsaetze = auszug.Umsaetze.Select((u, i) => new Umsatz(k.Id, i + 1, u.Buchungstag, u.Valuta, u.BetragCent, u.GegenName, u.GegenIban, u.Verwendungszweck,
            u.EndToEndId, u.Rueckgabegrund)).ToList();
        foreach (var u in umsaetze)
        {
            speicher.Hinzufuegen(u);
        }

        await VorschlagenAsync(umsaetze, abbruch);
        await finanzprotokoll.SchreibenAsync("Kontoauszug.Eingelesen", "Kontoauszug", k.Id, null,
            $"{auszug.AuszugId}; {umsaetze.Count} Umsätze; Summe {new Geld(umsaetze.Sum(u => u.BetragCent))}; SHA-256 {sha}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Kontoauszug", $"Kontoauszug {auszug.AuszugId} eingelesen: {umsaetze.Count} Umsätze, {umsaetze.Count(u => u.Sicherheit == Zuordnungssicherheit.Sicher)} sicher zuordenbar",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new(k, umsaetze.Count, umsaetze.Count(u => u.Sicherheit == Zuordnungssicherheit.Sicher), umsaetze.Count(u => u.VorschlagBelegId is not null));
    }

    private async Task VorschlagenAsync(IReadOnlyList<Umsatz> umsaetze, CancellationToken abbruch)
    {
        var belege = await speicher.BelegeAsync(abbruch: abbruch);
        var offen = Offenposten.Berechnen(belege);
        var nachNummer = belege.Where(b => b.Nummer is not null).ToDictionary(b => b.Nummer!);
        var posten = (await speicher.ZahlungslaeufeAsync(abbruch)).Where(l => l.Status == Zahlungslaufstatus.Exportiert)
            .SelectMany(l => l.Aktiv.Select(p => (l.Art, p))).ToDictionary(x => x.p.EndToEndId, x => x);
        var bereitsGebucht = (await speicher.UmsaetzeAsync(abbruch: abbruch)).Where(u => u.Status == Umsatzstatus.Zugeordnet && u.EndToEndId is not null && u.BetragCent > 0)
            .ToDictionary(u => u.EndToEndId!, u => u.ErzeugterBelegId);
        foreach (var u in umsaetze)
        {
            if (u.EndToEndId is { } e2e && posten.TryGetValue(e2e, out var p))
            {
                if (u.BetragCent < 0 && p.Art == Zahlungslaufart.Lastschrift)
                {
                    u.Vorschlagen(bereitsGebucht.GetValueOrDefault(e2e) ?? p.p.BelegId, Zuordnungssicherheit.Sicher,
                        $"Rücklastschrift zu {p.p.BelegNummer} ({u.Rueckgabegrund ?? "ohne Grund"}: {Rueckgabetext(u.Rueckgabegrund)})");
                }
                else
                {
                    u.Vorschlagen(p.p.BelegId, Zuordnungssicherheit.Sicher,
                        $"{(p.Art == Zahlungslaufart.Lastschrift ? "Lastschrifteinzug" : "Überweisung")} {p.p.BelegNummer} (Referenz aus dem eigenen Lauf)");
                }

                continue;
            }

            if (u.BetragCent > 0 && BelegnummerMuster().Match(u.Verwendungszweck) is { Success: true } treffer && nachNummer.TryGetValue(treffer.Value, out var r)
                && !r.Storniert)
            {
                var rest = offen.GetValueOrDefault(r.Id);
                u.Vorschlagen(r.Id, rest == u.BetragCent ? Zuordnungssicherheit.Sicher : Zuordnungssicherheit.Mittel,
                    rest == u.BetragCent ? $"{r.Nummer}: Belegnummer und Betrag passen"
                    : rest <= 0 ? $"{r.Nummer} ist bereits ausgeglichen – Doppelzahlung? (wird Guthaben)"
                    : $"{r.Nummer}: Belegnummer passt, Betrag weicht ab (offen {new Geld(rest)})");
                continue;
            }

            if (u.BetragCent > 0)
            {
                var kandidaten = belege.Where(b => b.Art is Belegart.Rechnung or Belegart.Mahnung && offen.GetValueOrDefault(b.Id) == u.BetragCent).ToList();
                var mitName = kandidaten.Where(b => NamePasst(b.EmpfaengerName, u.GegenName)).ToList();
                if (mitName.Count == 1)
                {
                    u.Vorschlagen(mitName[0].Id, Zuordnungssicherheit.Mittel, $"{mitName[0].Nummer}: Betrag und Name passen, keine Belegnummer im Verwendungszweck");
                }
                else if (kandidaten.Count == 1)
                {
                    u.Vorschlagen(kandidaten[0].Id, Zuordnungssicherheit.Niedrig, $"{kandidaten[0].Nummer}: nur der Betrag passt – bitte prüfen");
                }
                else
                {
                    u.Vorschlagen(null, Zuordnungssicherheit.Keine, kandidaten.Count == 0 ? "Kein offener Posten passt" : $"{kandidaten.Count} offene Posten mit diesem Betrag – bitte auswählen");
                }

                continue;
            }

            u.Vorschlagen(null, Zuordnungssicherheit.Keine, "Belastung ohne Bezug zu einem eigenen Lauf (z. B. Kontoführung) – ignorieren oder klären");
        }
    }

    private static bool NamePasst(string beleg, string? gegen)
    {
        if (string.IsNullOrWhiteSpace(gegen))
        {
            return false;
        }

        var teile = Suchtext.Normalisieren(beleg).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 2).ToList();
        var g = Suchtext.Normalisieren(gegen);
        return teile.Count > 0 && teile.Count(t => g.Contains(t, StringComparison.Ordinal)) >= Math.Min(2, teile.Count);
    }

    public static string Rueckgabetext(string? code) => code switch
    {
        "AC04" => "Konto erloschen",
        "AC06" => "Konto gesperrt",
        "AM04" => "Deckung unzureichend",
        "MD01" => "kein Mandat",
        "MD06" => "Erstattung auf Verlangen des Zahlers",
        "MS02" => "Grund nicht angegeben (Zahler)",
        "MS03" => "Grund nicht angegeben (Bank)",
        null => "nicht angegeben",
        _ => "siehe Bankhinweis",
    };

    /// <summary>
    /// Ordnet einen Umsatz zu und bucht ihn: Eingang → Zahlungseingang zur Rechnung; Belastung zu eigenem Überweisungslauf →
    /// Zahlungsausgang zur Honorarabrechnung; Rücklastschrift → Storno des Zahlungseingangs plus Gebührenrechnung.
    /// </summary>
    public async Task ZuordnenAsync(Guid umsatzId, Guid? zielBelegId = null, CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var u = await speicher.UmsatzAsync(umsatzId, abbruch) ?? throw new InvalidOperationException("Umsatz nicht gefunden.");
        var zielId = zielBelegId ?? u.VorschlagBelegId ?? throw new InvalidOperationException("Bitte einen Beleg auswählen – es gibt keinen Vorschlag.");
        var ziel = await speicher.BelegAsync(zielId, abbruch) ?? throw new InvalidOperationException("Beleg nicht gefunden.");
        var jetzt = uhr.GetLocalNow();
        Beleg erzeugt;
        if (u.BetragCent > 0)
        {
            if (ziel.Art is not (Belegart.Rechnung or Belegart.Mahnung) || ziel.Storniert)
            {
                throw new InvalidOperationException("Zahlungseingänge werden einer gültigen Rechnung oder Mahnung zugeordnet.");
            }

            erzeugt = new Beleg(Belegart.Zahlungseingang, u.Buchungstag, ziel.PersonId, ziel.OrganisationId, ziel.EmpfaengerName, null,
                [new Belegposition($"Zahlung zu {ziel.Nummer}", u.BetragCent, art: Positionsart.Zahlung)], jetzt, wer.Anzeige, bezugBelegId: ziel.Id,
                text: $"Kontoauszug, Buchungstag {u.Buchungstag:dd.MM.yyyy}{(u.EndToEndId is null ? "" : $", Referenz {u.EndToEndId}")}");
        }
        else if (u.Rueckgabegrund is not null || ziel.Art == Belegart.Zahlungseingang)
        {
            await RuecklastschriftAsync(u, ziel, abbruch);
            return;
        }
        else
        {
            if (ziel.Art is not (Belegart.Honorarabrechnung or Belegart.Gutschrift) || ziel.Storniert)
            {
                throw new InvalidOperationException("Belastungen werden einer Honorarabrechnung oder Gutschrift (Auszahlung) oder einer Rücklastschrift zugeordnet.");
            }

            erzeugt = new Beleg(Belegart.Zahlungsausgang, u.Buchungstag, ziel.PersonId, null, ziel.EmpfaengerName, null,
                [new Belegposition($"Auszahlung zu {ziel.Nummer}", -u.BetragCent, art: Positionsart.Zahlung)], jetzt, wer.Anzeige, bezugBelegId: ziel.Id,
                text: $"Kontoauszug, Buchungstag {u.Buchungstag:dd.MM.yyyy}");
        }

        speicher.Hinzufuegen(erzeugt);
        await finanzen.FestschreibenAsync(erzeugt, null, false, abbruch: abbruch);
        u.Zuordnen(ziel.Id, erzeugt.Id, jetzt, wer.Anzeige);
        await finanzprotokoll.SchreibenAsync("Umsatz.Zugeordnet", "Umsatz", u.Id, "offen", $"{new Geld(u.BetragCent)} → {ziel.Nummer}; gebucht als {erzeugt.Nummer}", abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    private async Task RuecklastschriftAsync(Umsatz u, Beleg ziel, CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var zahlung = ziel.Art == Belegart.Zahlungseingang ? ziel : null;
        var rechnung = zahlung?.BezugBelegId is { } rid ? await speicher.BelegAsync(rid, abbruch) : ziel;
        if (rechnung is null)
        {
            throw new InvalidOperationException("Zur Rücklastschrift gehört keine Rechnung.");
        }

        // Einzug und Rückgabe im selben Auszug: Die Zahlung wurde eben erst gebucht – sie über die Referenz finden.
        if (zahlung is null && u.EndToEndId is { } e2e
            && (await speicher.BelegeAsync(rechnung.PersonId ?? rechnung.OrganisationId, abbruch))
                .FirstOrDefault(b => b.Art == Belegart.Zahlungseingang && b.BezugBelegId == rechnung.Id && !b.Storniert && b.Text?.Contains(e2e, StringComparison.Ordinal) == true) is { } gebucht)
        {
            zahlung = await speicher.BelegAsync(gebucht.Id, abbruch);
        }

        Beleg erzeugt;
        if (zahlung is { Storniert: false })
        {
            erzeugt = await finanzen.StornoIntern(zahlung, $"Rücklastschrift {u.Rueckgabegrund}: {Rueckgabetext(u.Rueckgabegrund)}", false, abbruch);
        }
        else
        {
            // Einzug war noch nicht gebucht: Eingang und Rückgabe heben sich auf – nur die Bankgebühr bleibt.
            erzeugt = rechnung;
        }

        var einst = await finanzen.EinstellungenAsync(abbruch);
        if (einst.RuecklastschriftGebuehrCent > 0 && rechnung.PersonId is not null && await finanzen.EmpfaengerZuAsync(rechnung, abbruch) is { } empf)
        {
            var gebuehr = new Beleg(Belegart.Rechnung, u.Buchungstag, rechnung.PersonId, null, empf.Name, string.Join('\n', empf.Anschrift),
                [new Belegposition($"Bankgebühr Rücklastschrift zu {rechnung.Nummer}", einst.RuecklastschriftGebuehrCent, art: Positionsart.Gebuehr)], uhr.GetLocalNow(), wer.Anzeige,
                Heute.AddDays(einst.ZahlungszielTage), text: "Rücklastschrift – bitte überweisen");
            speicher.Hinzufuegen(gebuehr);
            await finanzen.FestschreibenAsync(gebuehr, empf, true, abbruch: abbruch);
        }

        if (u.Rueckgabegrund is "AC04" or "AC06" or "MD01" && rechnung.PersonId is { } pid)
        {
            foreach (var m in (await speicher.MandateAsync(pid, abbruch)).Where(m => m.Status == Mandatsstatus.Aktiv))
            {
                await finanzen.MandatWiderrufenAsync(m.Id, abbruch);
            }
        }

        u.Zuordnen(rechnung.Id, erzeugt.Id, uhr.GetLocalNow(), wer.Anzeige);
        await finanzprotokoll.SchreibenAsync("Umsatz.Ruecklastschrift", "Umsatz", u.Id, "offen",
            $"{new Geld(u.BetragCent)} Rücklastschrift {u.Rueckgabegrund} zu {rechnung.Nummer}; {(erzeugt.Art == Belegart.Storno ? $"Zahlung storniert durch {erzeugt.Nummer}" : "Einzug noch nicht gebucht")}",
            abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Ruecklastschrift", $"Rücklastschrift zu {rechnung.Nummer} ({Rueckgabetext(u.Rueckgabegrund)}) für {(rechnung.PersonId is { } p ? Pseudonym.Person(p) : "–")}",
            erfolgreich: false, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task IgnorierenAsync(Guid umsatzId, string grund, CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        if (string.IsNullOrWhiteSpace(grund))
        {
            throw new InvalidOperationException("Bitte kurz begründen, warum der Umsatz nicht gebucht wird.");
        }

        var u = await speicher.UmsatzAsync(umsatzId, abbruch) ?? throw new InvalidOperationException("Umsatz nicht gefunden.");
        u.Ignorieren(grund.Trim(), uhr.GetLocalNow(), wer.Anzeige);
        await finanzprotokoll.SchreibenAsync("Umsatz.Ignoriert", "Umsatz", u.Id, "offen", $"{new Geld(u.BetragCent)}: {grund.Trim()}", abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Alle sicheren Vorschläge (Belegnummer und Betrag oder eigene Referenz) in einem Schritt buchen.</summary>
    public async Task<int> SichereZuordnenAsync(Guid? kontoauszugId = null, CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var n = 0;
        foreach (var u in (await speicher.UmsaetzeAsync(kontoauszugId, nurOffene: true, abbruch)).Where(u => u.Sicherheit == Zuordnungssicherheit.Sicher).ToList())
        {
            await ZuordnenAsync(u.Id, null, abbruch);
            n++;
        }

        return n;
    }

    /// <summary>Nur zum Testen: Kontoauszug aus den offenen Posten simulieren (ein Teil zahlt, einer mit falschem Betrag, eine Rücklastschrift).</summary>
    public async Task<(string Datei, byte[] Inhalt)> DemoAuszugAsync(CancellationToken abbruch = default)
    {
        var einst = await finanzen.EinstellungenAsync(abbruch);
        var belege = await speicher.BelegeAsync(abbruch: abbruch);
        var offen = Offenposten.Berechnen(belege);
        var rechnungen = belege.Where(b => b.Art == Belegart.Rechnung && offen.GetValueOrDefault(b.Id) > 0 && b.Text?.Contains("Lastschrift", StringComparison.Ordinal) != true)
            .OrderBy(b => b.Nummer).Take(6).ToList();
        var umsaetze = new List<CamtUmsatz>();
        for (var i = 0; i < rechnungen.Count; i++)
        {
            var r = rechnungen[i];
            var betrag = i == 2 ? offen[r.Id] - 500 : offen[r.Id];
            var zweck = i == 3 ? "Kursgebuehr" : $"{r.Nummer} {r.EmpfaengerName}";
            umsaetze.Add(new(Heute, Heute, betrag, r.EmpfaengerName, Iban.AusBlzUndKonto("40050150", (1000000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)), zweck, null, null));
        }

        foreach (var lauf in (await speicher.ZahlungslaeufeAsync(abbruch)).Where(l => l.Status == Zahlungslaufstatus.Exportiert))
        {
            var liste = lauf.Aktiv.Where(p => offen.GetValueOrDefault(p.BelegId) > 0).ToList();
            for (var i = 0; i < liste.Count; i++)
            {
                var p = liste[i];
                // Simulation: Buchungstag heute (auch wenn der Einzug für später angekündigt war), damit keine Belege in der Zukunft entstehen.
                umsaetze.Add(new(Heute, lauf.Ausfuehrung, lauf.Art == Zahlungslaufart.Lastschrift ? p.BetragCent : -p.BetragCent, p.Name, p.Iban, p.Verwendungszweck,
                    p.EndToEndId, null));
                if (lauf.Art == Zahlungslaufart.Lastschrift && i == 0)
                {
                    umsaetze.Add(new(Heute, lauf.Ausfuehrung, -p.BetragCent, p.Name, p.Iban, $"RUECKLASTSCHRIFT {p.Verwendungszweck}", p.EndToEndId, "AM04"));
                }
            }
        }

        umsaetze.Add(new(Heute, Heute, -1250, "Sparkasse", null, "Kontofuehrung Monat", null, null));
        var id = $"DEMO-{uhr.GetLocalNow():yyyyMMddHHmmss}";
        return ($"{id}.camt.053.xml", Encoding.UTF8.GetBytes(Camt053.Erzeugen(id, einst.Iban, Heute, umsaetze)));
    }

    private static string Ausschnitt(string text, int max) => text.Length > max ? text[..max] : text;
}
