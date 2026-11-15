using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Finanzen;

/// <summary>
/// Mahnwesen (FA-32): Stufen mit Fristen und Gebühren. Die Automatik stellt einen Mahnvorschlag zusammen;
/// erst die Freigabe durch die Buchhaltung erzeugt Mahnungen (QA-32).
/// </summary>
public sealed class MahnDienst(
    IFinanzSpeicher speicher,
    IFinanzprotokoll finanzprotokoll,
    FinanzDienst finanzen,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    /// <summary>Rechnungen, die nach den Stufenfristen fällig für die nächste Mahnstufe sind.</summary>
    public async Task<IReadOnlyList<Mahnposten>> FaelligeAsync(CancellationToken abbruch = default)
    {
        var einst = await finanzen.EinstellungenAsync(abbruch);
        var belege = await speicher.BelegeAsync(abbruch: abbruch);
        var offen = Offenposten.Berechnen(belege);
        var laeufe = await speicher.ZahlungslaeufeAsync(abbruch);
        var imEinzug = laeufe.Where(l => l.Art == Zahlungslaufart.Lastschrift && l.Status is Zahlungslaufstatus.Vorschlag or Zahlungslaufstatus.Freigegeben
                                         || (l.Status == Zahlungslaufstatus.Exportiert && l.Ausfuehrung.AddDays(10) >= Heute))
            .SelectMany(l => l.Aktiv).Select(p => p.BelegId).ToHashSet();
        var stufen = einst.Mahnstufen.OrderBy(s => s.Stufe).ToList();
        var ergebnis = new List<Mahnposten>();
        foreach (var r in belege.Where(b => b.Art == Belegart.Rechnung && b.Festgeschrieben && !b.Storniert && b.Faellig is not null && offen.GetValueOrDefault(b.Id) > 0
                                          && !imEinzug.Contains(b.Id)))
        {
            var mahnungen = belege.Where(m => m.Art == Belegart.Mahnung && m.BezugBelegId == r.Id && !m.Storniert && m.Festgeschrieben).OrderBy(m => m.Belegdatum).ToList();
            if (mahnungen.Count >= stufen.Count)
            {
                continue;
            }

            var naechste = stufen[mahnungen.Count];
            var seit = mahnungen.LastOrDefault()?.Belegdatum ?? r.Faellig!.Value;
            if (Heute < seit.AddDays(naechste.TageNachVorstufe))
            {
                continue;
            }

            ergebnis.Add(new Mahnposten(r.Id, r.Nummer!, r.PersonId, r.OrganisationId, r.EmpfaengerName, naechste.Stufe, naechste.Bezeichnung, offen[r.Id], naechste.GebuehrCent,
                r.Faellig!.Value));
        }

        return ergebnis;
    }

    /// <summary>Automatik: Vorschlag anlegen, wenn etwas fällig ist und kein Vorschlag offen ist.</summary>
    public async Task<Mahnlauf?> VorschlagAsync(CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        if ((await speicher.MahnlaeufeAsync(abbruch)).FirstOrDefault(l => l.Status == Mahnlaufstatus.Vorschlag) is { } offen)
        {
            return offen;
        }

        var faellig = await FaelligeAsync(abbruch);
        if (faellig.Count == 0)
        {
            return null;
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        var lauf = new Mahnlauf(faellig, uhr.GetLocalNow(), wer.Anzeige);
        speicher.Hinzufuegen(lauf);
        await finanzprotokoll.SchreibenAsync("Mahnlauf.Vorschlag", "Mahnlauf", lauf.Id, null, $"{faellig.Count} Posten; offen {new Geld(faellig.Sum(p => p.OffenCent))}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Mahnvorschlag", $"Mahnvorschlag mit {faellig.Count} Posten erstellt – wartet auf Freigabe", erfolgreich: false, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return lauf;
    }

    public async Task AusschliessenAsync(Guid laufId, Guid rechnungId, bool ausgeschlossen, CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var lauf = await speicher.MahnlaufAsync(laufId, abbruch) ?? throw new InvalidOperationException("Mahnlauf nicht gefunden.");
        lauf.Ausschliessen(rechnungId, ausgeschlossen);
        await finanzprotokoll.SchreibenAsync("Mahnlauf.Posten", "Mahnlauf", lauf.Id, null, $"{rechnungId.ToString()[..8]} {(ausgeschlossen ? "ausgeschlossen" : "aufgenommen")}", abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Freigabe: je Posten eine Mahnung (festgeschrieben, Brief mit Übersetzungs-QR, Mail mit Übersetzungslink).</summary>
    public async Task<int> FreigebenAsync(Guid laufId, bool mailen = true, CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var lauf = await speicher.MahnlaufAsync(laufId, abbruch) ?? throw new InvalidOperationException("Mahnlauf nicht gefunden.");
        if (lauf.Status != Mahnlaufstatus.Vorschlag)
        {
            throw new InvalidOperationException("Der Mahnlauf ist bereits bearbeitet.");
        }

        var einst = await finanzen.EinstellungenAsync(abbruch);
        var offen = Offenposten.Berechnen(await speicher.BelegeAsync(abbruch: abbruch));
        var erzeugt = new Dictionary<Guid, Guid>();
        foreach (var p in lauf.Aktiv)
        {
            var rechnung = await speicher.BelegAsync(p.RechnungId, abbruch);
            var rest = rechnung is null ? 0 : offen.GetValueOrDefault(rechnung.Id);
            if (rechnung is null || rechnung.Storniert || rest <= 0)
            {
                continue; // inzwischen bezahlt oder storniert
            }

            var empf = await finanzen.EmpfaengerZuAsync(rechnung, abbruch);
            var frist = Heute.AddDays(14);
            var text = $"{p.Stufenname}: Zu unserer Rechnung {rechnung.Nummer} vom {rechnung.Belegdatum:dd.MM.yyyy} (fällig am {rechnung.Faellig:dd.MM.yyyy}) ist noch {new Geld(rest)} offen. "
                       + $"Bitte überweisen Sie den offenen Betrag{(p.GebuehrCent > 0 ? " zuzüglich der Mahngebühr" : "")} bis zum {frist:dd.MM.yyyy} unter Angabe der Belegnummer {rechnung.Nummer}. "
                       + "Sollten Sie inzwischen gezahlt haben, betrachten Sie dieses Schreiben bitte als gegenstandslos.";
            var mahnung = new Beleg(Belegart.Mahnung, Heute, rechnung.PersonId, rechnung.OrganisationId, empf?.Name ?? rechnung.EmpfaengerName,
                empf is null ? rechnung.EmpfaengerAnschrift : string.Join('\n', empf.Anschrift),
                [new Belegposition($"Mahngebühr {p.Stufenname} zu {rechnung.Nummer} (offen {new Geld(rest)})", p.GebuehrCent, art: Positionsart.Mahngebuehr)],
                uhr.GetLocalNow(), wer.Anzeige, frist, rechnung.Id, text);
            speicher.Hinzufuegen(mahnung);
            await finanzen.FestschreibenAsync(mahnung, empf, mailen, abbruch: abbruch);
            erzeugt[p.RechnungId] = mahnung.Id;
        }

        lauf.Freigeben(erzeugt, uhr.GetLocalNow(), wer.Anzeige);
        await finanzprotokoll.SchreibenAsync("Mahnlauf.Freigegeben", "Mahnlauf", lauf.Id, "Vorschlag", $"{erzeugt.Count} Mahnungen erzeugt", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Mahnlauf", $"Mahnlauf freigegeben: {erzeugt.Count} Mahnungen verschickt", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return erzeugt.Count;
    }

    public async Task VerwerfenAsync(Guid laufId, CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var lauf = await speicher.MahnlaufAsync(laufId, abbruch) ?? throw new InvalidOperationException("Mahnlauf nicht gefunden.");
        lauf.Verwerfen();
        await finanzprotokoll.SchreibenAsync("Mahnlauf.Verworfen", "Mahnlauf", lauf.Id, "Vorschlag", "verworfen", abbruch);
        await speicher.SpeichernAsync(abbruch);
    }
}
