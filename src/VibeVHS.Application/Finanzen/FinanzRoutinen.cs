using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Finanzen.Honorare;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Finanzen;

public sealed record Finanzlauf(Rechnungslaufergebnis Rechnungen, Zahlungslauf? Lastschrift, Mahnlauf? Mahnungen, Honorarlauf? Honorare, Abgleichergebnis Aufgaben)
{
    public override string ToString()
        => $"{Rechnungen}; Lastschrift: {(Lastschrift is null ? "–" : $"{Lastschrift.Aktiv.Count()} Posten ({Lastschrift.Status})")}; Mahnungen: {(Mahnungen is null ? "–" : $"{Mahnungen.Posten.Count} ({Mahnungen.Status})")}; "
           + $"Honorare: {(Honorare is null ? "–" : $"{Honorare.Entwuerfe} Entwürfe")}; Aufgaben: {Aufgaben}";
}

/// <summary>
/// Tägliche Finanzroutinen (QA-30): Rechnungslauf, Lastschriftvorbereitung, Mahnvorschlag, Honorarentwürfe zum Monatsanfang
/// und Abgleich der Finanzaufgaben. Kritische Schritte laufen als Vorschlag, solange die Aufgabenregel „Mensch gibt frei“ sagt (QA-32).
/// </summary>
public sealed class FinanzRoutinen(
    FinanzDienst finanzen,
    ZahlungsDienst zahlungen,
    MahnDienst mahnungen,
    HonorarDienst honorare,
    IFinanzSpeicher speicher,
    AufgabenDienst aufgaben,
    TimeProvider uhr)
{
    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    public async Task<Finanzlauf> TaeglichAsync(bool mailen = true, CancellationToken abbruch = default)
    {
        var rechnungen = await finanzen.RechnungslaufAsync(mailen, abbruch);

        var lastschrift = await zahlungen.LastschriftVorschlagAsync(abbruch);
        if (lastschrift is { Status: Zahlungslaufstatus.Vorschlag } && (await aufgaben.TypAsync(Aufgabentypen.LastschriftFreigabe, abbruch)).MenschGibtFrei == false)
        {
            await zahlungen.FreigebenAsync(lastschrift.Id, abbruch);
        }

        var mahnlauf = await mahnungen.VorschlagAsync(abbruch);
        if (mahnlauf is { Status: Mahnlaufstatus.Vorschlag } && (await aufgaben.TypAsync(Aufgabentypen.MahnlaufFreigabe, abbruch)).MenschGibtFrei == false)
        {
            await mahnungen.FreigebenAsync(mahnlauf.Id, mailen, abbruch);
        }

        Honorarlauf? honorar = null;
        if (Heute.Day <= 3)
        {
            var vormonat = new DateOnly(Heute.Year, Heute.Month, 1).AddMonths(-1);
            honorar = await honorare.AbrechnungenErstellenAsync(vormonat, vormonat.AddMonths(1).AddDays(-1), abbruch: abbruch);
        }

        return new(rechnungen, lastschrift, mahnlauf, honorar, await AufgabenAbgleichenAsync(abbruch));
    }

    /// <summary>Finanzaufgaben aus dem aktuellen Stand ableiten; erledigte Anlässe schließen sich selbst.</summary>
    public async Task<Abgleichergebnis> AufgabenAbgleichenAsync(CancellationToken abbruch = default)
    {
        var heute = Heute;
        var laeufe = await speicher.ZahlungslaeufeAsync(abbruch);
        var lastschrift = laeufe.Where(l => l.Art == Zahlungslaufart.Lastschrift && l.Status is Zahlungslaufstatus.Vorschlag or Zahlungslaufstatus.Freigegeben)
            .Select(l => new AufgabenAnlass($"{Aufgabentypen.LastschriftFreigabe}:{l.Id:N}",
                l.Status == Zahlungslaufstatus.Vorschlag
                    ? $"Lastschriftlauf freigeben: {l.Aktiv.Count()} Posten, {new Geld(l.SummeCent)}"
                    : $"SEPA-Datei erzeugen und zur Bank geben: Einzug am {l.Ausfuehrung:dd.MM.yyyy}",
                l.Status == Zahlungslaufstatus.Vorschlag ? "Posten prüfen, ggf. ausschließen, freigeben – dann gehen die Vorabankündigungen raus." : "Die Datei muss vor dem Einzugstag bei der Bank sein.",
                l.Status == Zahlungslaufstatus.Vorschlag ? heute.AddDays(1) : l.Ausfuehrung.AddDays(-2), "finanzen?tab=zahlungen"))
            .ToList();
        var mahn = (await speicher.MahnlaeufeAsync(abbruch)).Where(l => l.Status == Mahnlaufstatus.Vorschlag)
            .Select(l => new AufgabenAnlass($"{Aufgabentypen.MahnlaufFreigabe}:{l.Id:N}", $"Mahnvorschlag freigeben: {l.Aktiv.Count()} Rechnung(en)",
                "Einzelne Posten ausschließen (z. B. Ratenzahlung vereinbart), dann freigeben.", DateOnly.FromDateTime(l.ErstelltAm.Date).AddDays(2), "finanzen?tab=mahnwesen"))
            .ToList();
        var belege = await speicher.BelegeAsync(abbruch: abbruch);
        var entwuerfe = belege.Where(b => b.Art == Belegart.Honorarabrechnung && !b.Festgeschrieben).ToList();
        AufgabenAnlass[] honorar = entwuerfe.Count == 0 ? [] :
        [
            new($"{Aufgabentypen.HonorarFreigabe}:entwuerfe", $"{entwuerfe.Count} Honorarabrechnung(en) prüfen und freigeben ({new Geld(entwuerfe.Sum(b => b.BetragCent))})",
                "Positionen und Hinweise des Honorarmodells prüfen, dann freigeben.", DateOnly.FromDateTime(entwuerfe.Min(b => b.ErfasstAm).Date).AddDays(5), "finanzen?tab=honorare"),
        ];
        var offeneUmsaetze = await speicher.UmsaetzeAsync(nurOffene: true, abbruch: abbruch);
        var auszuege = (await speicher.KontoauszuegeAsync(abbruch)).ToDictionary(k => k.Id);
        var konto = offeneUmsaetze.GroupBy(u => u.KontoauszugId).Where(g => auszuege.ContainsKey(g.Key))
            .Select(g => new AufgabenAnlass($"{Aufgabentypen.KontoauszugZuordnung}:{g.Key:N}", $"Kontoauszug {auszuege[g.Key].AuszugId}: {g.Count()} Umsatz/Umsätze zuordnen",
                $"{g.Count(u => u.Sicherheit == Zuordnungssicherheit.Sicher)} davon sicher – mit einem Klick übernehmen.", DateOnly.FromDateTime(auszuege[g.Key].EingelesenAm.Date).AddDays(2),
                $"finanzen?tab=konto&auszug={g.Key}"))
            .ToList();
        var offen = Offenposten.Berechnen(belege);
        var nachId = belege.ToDictionary(b => b.Id);
        var rueck = belege.Where(s => s.Art == Belegart.Storno && s.Text?.StartsWith("Rücklastschrift", StringComparison.Ordinal) == true && s.BezugBelegId is not null)
            .Select(s => nachId.TryGetValue(s.BezugBelegId!.Value, out var zahlung) && zahlung.BezugBelegId is { } rid && nachId.TryGetValue(rid, out var r) ? r : null)
            .Where(r => r is not null && offen.GetValueOrDefault(r.Id) > 0).DistinctBy(r => r!.Id)
            .Select(r => new AufgabenAnlass($"{Aufgabentypen.Ruecklastschrift}:{r!.Id:N}", $"Rücklastschrift zu {r.Nummer}: {new Geld(offen[r.Id])} wieder offen",
                "Person kontaktieren, Bankverbindung prüfen, ggf. neues Mandat oder Überweisung vereinbaren.", heute.AddDays(7), r.PersonId is { } p ? $"personen/{p}" : "finanzen"))
            .ToList();

        return await aufgaben.AbgleichenAsync(Aufgabentypen.LastschriftFreigabe, lastschrift, grund: _ => "Lauf freigegeben und exportiert oder verworfen", abbruch: abbruch)
               + await aufgaben.AbgleichenAsync(Aufgabentypen.MahnlaufFreigabe, mahn, grund: _ => "Mahnlauf freigegeben oder verworfen", abbruch: abbruch)
               + await aufgaben.AbgleichenAsync(Aufgabentypen.HonorarFreigabe, honorar, grund: _ => "alle Entwürfe freigegeben oder verworfen", abbruch: abbruch)
               + await aufgaben.AbgleichenAsync(Aufgabentypen.KontoauszugZuordnung, konto, grund: _ => "alle Umsätze zugeordnet oder ignoriert", abbruch: abbruch)
               + await aufgaben.AbgleichenAsync(Aufgabentypen.Ruecklastschrift, rueck, grund: _ => "Rechnung wieder ausgeglichen", abbruch: abbruch);
    }
}
