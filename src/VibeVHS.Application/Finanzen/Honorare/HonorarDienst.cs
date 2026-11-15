using System.Text.Json;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Finanzen.Honorare;

/// <summary>Berechnung je Vertrag (oder Termine ohne Vertrag).</summary>
public sealed record Honorarberechnung(Honorarvertrag Vertrag, IHonorarModell Modell, HonorarErgebnis Ergebnis);

public sealed record Honorarvorschau(Guid PersonId, DateOnly Von, DateOnly Bis, IReadOnlyList<Honorarberechnung> Berechnungen, IReadOnlyList<HonorarTermin> OhneVertrag,
    IReadOnlyList<string> Hinweise)
{
    public decimal Summe => Berechnungen.Sum(b => b.Ergebnis.Summe);
}

public sealed record Honorarlauf(int Entwuerfe, decimal Summe, IReadOnlyList<string> OhneVertrag);

/// <summary>
/// Honorare über austauschbare Honorarmodelle (Plugins). Der Kern stellt den Kontext zusammen (Termine, anonyme Teilnahme,
/// frühere Abrechnungen), prüft Doppelabrechnungen und hält Modell, Version und Parameter am Beleg fest (GoBD).
/// </summary>
public sealed class HonorarDienst(
    IFinanzSpeicher speicher,
    IFinanzprotokoll finanzprotokoll,
    FinanzDienst finanzen,
    HonorarModelle modelle,
    IPersonenSpeicher personen,
    IAuditProtokoll protokoll,
    TimeProvider uhr)
{
    /// <summary>Wer Honorarverträge pflegt.</summary>
    public static readonly Rolle[] VertragsRollen = [Rolle.Buchhaltung, Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    /// <summary>Absagen, die weniger als so viele Stunden vor Beginn erfasst wurden, gelten als kurzfristig.</summary>
    public const int KurzfristigStunden = 48;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public IReadOnlyList<IHonorarModell> Modelle => modelle.Alle;

    public Task<IReadOnlyList<Honorarvertrag>> VertraegeAsync(Guid personId, CancellationToken abbruch = default) => speicher.HonorarvertraegeAsync(personId, abbruch);

    /// <summary>Neuer Vertrag bzw. neue Vertragsversion. Ein laufender Vertrag gleichen Umfangs endet am Vortag.</summary>
    public async Task<Honorarvertrag> VertragSpeichernAsync(Guid personId, Guid? kursId, string bezeichnung, string modellKennung, IReadOnlyDictionary<string, string?> eingabe,
        DateOnly ab, DateOnly? bis, CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(VertragsRollen, abbruch);
        var modell = modelle.Holen(modellKennung);
        var (werte, fehler) = Parameterpruefung.Pruefen(modell, eingabe);
        if (fehler.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", fehler.Values));
        }

        var person = await personen.LadenAsync(personId, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
        if (person.BeschaeftigungAm(ab, uhr.GetLocalNow()) == Beschaeftigungsart.Festangestellt)
        {
            throw new InvalidOperationException($"{person.Anzeigename} ist ab {ab:dd.MM.yyyy} festangestellt – Einsätze laufen über die Arbeitszeit, nicht über ein Honorar.");
        }

        if (bis < ab)
        {
            throw new InvalidOperationException("Das Vertragsende liegt vor dem Beginn.");
        }

        foreach (var alt in (await speicher.HonorarvertraegeAsync(personId, abbruch)).Where(v => v.KursId == kursId && (v.GueltigBis is null || v.GueltigBis >= ab)))
        {
            if (alt.GueltigAb >= ab)
            {
                throw new InvalidOperationException($"Es gibt bereits einen Vertrag ab {alt.GueltigAb:dd.MM.yyyy}. Bitte den neuen Vertrag später beginnen lassen oder den vorhandenen beenden.");
            }

            var v = await speicher.HonorarvertragAsync(alt.Id, abbruch);
            v!.Beenden(ab.AddDays(-1));
            await finanzprotokoll.SchreibenAsync("Honorarvertrag.Beendet", "Honorarvertrag", v.Id, "offen", $"endet {ab.AddDays(-1):yyyy-MM-dd} (neue Version)", abbruch);
        }

        var vertrag = new Honorarvertrag(personId, kursId, bezeichnung, modell.Kennung, werte, ab, bis, uhr.GetLocalNow(), wer.Anzeige);
        speicher.Hinzufuegen(vertrag);
        await finanzprotokoll.SchreibenAsync("Honorarvertrag.Angelegt", "Honorarvertrag", vertrag.Id, null,
            $"{Pseudonym.Person(personId)}; Kurs {kursId?.ToString()[..8] ?? "alle"}; Modell {modell.Kennung} {modell.Version}; {JsonSerializer.Serialize(werte, Json)}; ab {ab:yyyy-MM-dd} bis {bis?.ToString("yyyy-MM-dd") ?? "offen"}",
            abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Honorarvertrag", $"Honorarvertrag „{modell.Anzeigename}“ für {Pseudonym.Person(personId)} ab {ab:dd.MM.yyyy}", nameof(Person), personId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return vertrag;
    }

    public async Task VertragBeendenAsync(Guid vertragId, DateOnly bis, CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(VertragsRollen, abbruch);
        var v = await speicher.HonorarvertragAsync(vertragId, abbruch) ?? throw new InvalidOperationException("Vertrag nicht gefunden.");
        var vorher = v.GueltigBis?.ToString("yyyy-MM-dd") ?? "offen";
        try
        {
            v.Beenden(bis);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        await finanzprotokoll.SchreibenAsync("Honorarvertrag.Beendet", "Honorarvertrag", v.Id, vorher, bis.ToString("yyyy-MM-dd"), abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    // ---------- Berechnung ----------

    /// <summary>Vorschau für eine Person und einen Zeitraum – ohne zu speichern.</summary>
    public async Task<Honorarvorschau> VorschauAsync(Guid personId, DateOnly von, DateOnly bis, CancellationToken abbruch = default)
    {
        var termine = await speicher.DozententermineAsync(von.ToDateTime(TimeOnly.MinValue), bis.AddDays(1).ToDateTime(TimeOnly.MinValue), personId, abbruch);
        return await BerechnenAsync(personId, von, bis, termine, abbruch);
    }

    private async Task<Honorarvorschau> BerechnenAsync(Guid personId, DateOnly von, DateOnly bis, IReadOnlyList<Dozententermin> roh, CancellationToken abbruch)
    {
        var jetzt = uhr.GetLocalNow();
        var vertraege = await speicher.HonorarvertraegeAsync(personId, abbruch);
        var frueher = await FruehereAsync(personId, abbruch);
        var schonAbgerechnet = frueher.Where(f => f.TerminId is not null).Select(f => f.TerminId!.Value).ToHashSet();
        var termine = roh.Where(t => t.Ausgefallen || t.Ende <= jetzt.DateTime).Select(t => Abbilden(t)).ToList();
        var hinweise = new List<string>();
        var ohne = new List<HonorarTermin>();
        var berechnungen = new List<Honorarberechnung>();
        foreach (var gruppe in termine.GroupBy(t => Honorarvertrag.Passend(vertraege, t.Datum, t.KursId)?.Id))
        {
            var vertrag = vertraege.FirstOrDefault(v => v.Id == gruppe.Key);
            if (vertrag is null)
            {
                ohne.AddRange(gruppe);
                continue;
            }

            var modell = modelle.Finden(vertrag.Modell);
            if (modell is null)
            {
                hinweise.Add($"Vertrag „{vertrag.Bezeichnung}“: Honorarmodell „{vertrag.Modell}“ ist nicht installiert.");
                continue;
            }

            var kontext = new HonorarKontext(von, bis, vertrag.Parameter, vertrag.GueltigAb, vertrag.GueltigBis, gruppe.ToList(), frueher);
            var ergebnis = modell.Berechne(kontext);

            // Kern-Prüfung: nichts doppelt abrechnen, auch wenn ein Plugin die Vorabrechnungen übersieht.
            var doppelt = ergebnis.Positionen.Where(p => p.TerminId is { } id && schonAbgerechnet.Contains(id)).ToList();
            var fremd = ergebnis.Positionen.Where(p => p.TerminId is { } id && gruppe.All(t => t.TerminId != id)).ToList();
            var negativ = ergebnis.Positionen.Where(p => p.Betrag < 0).ToList();
            if (doppelt.Count + fremd.Count + negativ.Count > 0)
            {
                hinweise.Add($"{modell.Anzeigename}: {doppelt.Count} bereits abgerechnete, {fremd.Count} fremde und {negativ.Count} negative Position(en) vom Kern entfernt.");
                ergebnis = ergebnis with { Positionen = ergebnis.Positionen.Except(doppelt).Except(fremd).Except(negativ).ToList() };
            }

            berechnungen.Add(new(vertrag, modell, ergebnis with { Positionen = ergebnis.Positionen.Select(p => p with { Betrag = Honorarbausteine.Runden(p.Betrag) }).ToList() }));
        }

        if (ohne.Count > 0)
        {
            hinweise.Add($"{ohne.Count} Termin(e) ohne gültigen Honorarvertrag – nicht berechnet.");
        }

        return new(personId, von, bis, berechnungen, ohne, hinweise);
    }

    private static HonorarTermin Abbilden(Dozententermin t)
    {
        var status = !t.Ausgefallen ? Terminstatus.Stattgefunden
            : t.AusgefallenAm is { } am && am.LocalDateTime >= t.Beginn.AddHours(-KurzfristigStunden) ? Terminstatus.KurzfristigAbgesagt
            : Terminstatus.Ausgefallen;
        var ue = Math.Round((decimal)(t.Ende - t.Beginn).TotalMinutes / 45m, 2);
        return new HonorarTermin(t.Id, t.KursId, t.Kursnummer, t.Kursart, t.Fachbereich, DateOnly.FromDateTime(t.Beginn), t.Beginn, t.Ende, ue, status,
            t.Ausgefallen ? t.Bemerkung : null, t.Ort, null, t.Angemeldet, t.Anwesend, t.Ermaessigt);
    }

    private async Task<IReadOnlyList<FruehereHonorarposition>> FruehereAsync(Guid personId, CancellationToken abbruch)
    {
        var liste = new List<FruehereHonorarposition>();
        foreach (var b in (await speicher.BelegeAsync(personId, abbruch)).Where(b => b.Art == Belegart.Honorarabrechnung && !b.Storniert))
        {
            var (modell, version, von, bis) = Grundlage(b.Grundlage);
            liste.AddRange(b.Positionen.Select(p => new FruehereHonorarposition(b.Nummer ?? "Entwurf", b.Belegdatum, von, bis, p.VeranstaltungId, p.Text,
                p.BetragCent / 100m, modell, version)));
        }

        return liste;
    }

    private static (string Modell, string Version, DateOnly? Von, DateOnly? Bis) Grundlage(string? json)
    {
        if (json is null)
        {
            return ("?", "?", null, null);
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var erstes = root.TryGetProperty("Vertraege", out var v) && v.GetArrayLength() > 0 ? v[0] : default;
        return (erstes.ValueKind == JsonValueKind.Object ? erstes.GetProperty("Modell").GetString() ?? "?" : "?",
            erstes.ValueKind == JsonValueKind.Object ? erstes.GetProperty("Version").GetString() ?? "?" : "?",
            root.TryGetProperty("Von", out var a) ? DateOnly.Parse(a.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : null,
            root.TryGetProperty("Bis", out var e) ? DateOnly.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    // ---------- Abrechnung ----------

    /// <summary>Erstellt je Kursleitung einen Entwurf (ohne Nummer) für den Zeitraum; freigegeben wird einzeln.</summary>
    public async Task<Honorarlauf> AbrechnungenErstellenAsync(DateOnly von, DateOnly bis, Guid? nurPerson = null, CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var alle = await speicher.DozententermineAsync(von.ToDateTime(TimeOnly.MinValue), bis.AddDays(1).ToDateTime(TimeOnly.MinValue), nurPerson, abbruch);
        var jetzt = uhr.GetLocalNow();
        var entwuerfe = 0;
        var summe = 0m;
        var ohneVertrag = new List<string>();
        foreach (var gruppe in alle.GroupBy(t => t.DozentId))
        {
            var person = await personen.LadenAsync(gruppe.Key, abbruch);
            if (person is null || person.IstAnonymisiert || person.BeschaeftigungAm(von, jetzt) == Beschaeftigungsart.Festangestellt)
            {
                continue;
            }

            var vorschau = await BerechnenAsync(person.Id, von, bis, gruppe.ToList(), abbruch);
            if (vorschau.OhneVertrag.Count > 0)
            {
                ohneVertrag.Add($"{person.Anzeigename}: {vorschau.OhneVertrag.Count} Termin(e)");
            }

            var positionen = vorschau.Berechnungen.SelectMany(b => b.Ergebnis.Positionen).Where(p => p.Betrag > 0).ToList();
            if (positionen.Count == 0)
            {
                continue;
            }

            var empf = FinanzDienst.Personenempfaenger(person, jetzt);
            var beleg = new Beleg(Belegart.Honorarabrechnung, DateOnly.FromDateTime(jetzt.Date), person.Id, null, empf.Name, string.Join('\n', empf.Anschrift),
                positionen.Select(p => new Belegposition(p.Text, (long)Math.Round(p.Betrag * 100m), p.Menge ?? 1, p.Einzelpreis is { } e ? (long)Math.Round(e * 100m) : null,
                    kursId: null, veranstaltungId: p.TerminId, art: Positionsart.Honorar)),
                jetzt, wer.Anzeige, DateOnly.FromDateTime(jetzt.Date).AddDays(14), text: $"Abrechnungszeitraum {von:dd.MM.yyyy} bis {bis:dd.MM.yyyy}");
            beleg.GrundlageSetzen(JsonSerializer.Serialize(new
            {
                Von = von.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Bis = bis.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Vertraege = vorschau.Berechnungen.Select(b => new
                {
                    VertragId = b.Vertrag.Id,
                    b.Vertrag.Bezeichnung,
                    Modell = b.Modell.Kennung,
                    b.Modell.Version,
                    b.Vertrag.Parameter,
                    Positionen = b.Ergebnis.Positionen.Count,
                    b.Ergebnis.Hinweise,
                }),
                vorschau.Hinweise,
            }, Json));
            speicher.Hinzufuegen(beleg);
            entwuerfe++;
            summe += positionen.Sum(p => p.Betrag);
        }

        await protokoll.ProtokollierenAsync("Finanzen.Honorarlauf", $"Honorarabrechnung {von:dd.MM.yyyy}–{bis:dd.MM.yyyy}: {entwuerfe} Entwürfe, {summe:N2} €"
            + (ohneVertrag.Count > 0 ? $"; {ohneVertrag.Count} Kursleitung(en) mit Terminen ohne Vertrag" : ""), erfolgreich: ohneVertrag.Count == 0, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new(entwuerfe, summe, ohneVertrag);
    }

    /// <summary>Freigabe: Festschreibung mit Nummer und Snapshot, Druckstück und Mail an die Kursleitung.</summary>
    public async Task FreigebenAsync(Guid belegId, CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var b = await speicher.BelegAsync(belegId, abbruch) ?? throw new InvalidOperationException("Beleg nicht gefunden.");
        if (b.Art != Belegart.Honorarabrechnung || b.Festgeschrieben)
        {
            throw new InvalidOperationException("Freigegeben werden nur Entwürfe von Honorarabrechnungen.");
        }

        await finanzen.FestschreibenAsync(b, await finanzen.EmpfaengerZuAsync(b, abbruch), true, abbruch: abbruch);
        await finanzprotokoll.SchreibenAsync("Honorar.Freigegeben", "Honorarabrechnung", b.Id, "Entwurf", $"{b.Nummer}; Grundlage {b.Grundlage}", abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public static string Betrag(decimal betrag) => new Geld((long)Math.Round(betrag * 100m)).ToString();
}
