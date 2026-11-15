using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Portal;

namespace VibeVHS.Application.Portal;

/// <summary>
/// Anliegen aus dem Dozierendenportal in der Verwaltung: jedes offene Anliegen ist eine Aufgabe im passenden Korb
/// (Ausfall/Nachweis/Vorschläge → Fachbereichsleitung; Bankverbindung/Honorareinwand → Buchhaltung). Die Entscheidung schließt die Aufgabe;
/// angenommene Bankverbindungen und Nachweise werden erst dann wirksam – nie durch die antragstellende Person selbst (Vier-Augen).
/// </summary>
public sealed class PortalVerwaltung(
    IPortalSpeicher speicher,
    IAktuellerBenutzer benutzer,
    IBenutzerkonten konten,
    IPersonenSpeicher personen,
    AufgabenDienst aufgaben,
    FinanzDienst finanzen,
    QualifikationsDienst qualifikationen,
    Datenschutz.DatenschutzDienst datenschutz,
    Dokumente.DokumentDienst dokumente,
    Datenschutz.IAuskunftPdf auskunftPdf,
    IAuditProtokoll protokoll,
    TimeProvider uhr)
{
    public static readonly Rolle[] FachbereichRollen = [Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    public static readonly Rolle[] BuchhaltungRollen = [Rolle.Buchhaltung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    public static readonly Rolle[] DatenschutzRollen = [Rolle.Verwaltungsleitung, Rolle.Leitung];

    public static readonly Rolle[] LeseRollen = [Rolle.Fachbereichsleitung, Rolle.Buchhaltung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    public static Rolle[] EntscheiderFuer(Antragsart art) => art switch
    {
        Antragsart.Bankverbindung or Antragsart.Honorareinwand => BuchhaltungRollen,
        Antragsart.Auskunft or Antragsart.Loeschung or Antragsart.EMailAenderung => DatenschutzRollen,
        _ => FachbereichRollen,
    };

    public static string? Aufgabentyp(Antragsart art) => art switch
    {
        Antragsart.EMailAenderung => null, // bestätigt die Person selbst über den Link
        Antragsart.Auskunft or Antragsart.Loeschung => Aufgabentypen.PortalDatenschutz,
        Antragsart.Ausfall => Aufgabentypen.PortalAusfall,
        Antragsart.Bankverbindung or Antragsart.Honorareinwand => Aufgabentypen.PortalBuchhaltung,
        _ => Aufgabentypen.PortalFachbereich,
    };

    /// <summary>Neues Anliegen speichern, protokollieren und die Aufgaben abgleichen.</summary>
    public async Task EinreichenAsync(Portalantrag antrag, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(antrag);
        speicher.Hinzufuegen(antrag);
        await protokoll.ProtokollierenAsync("Portal.Anliegen", $"{AntragAnzeige.Art(antrag.Art)} von {Pseudonym.Person(antrag.PersonId)} eingereicht: {antrag.Titel}",
            "Person", antrag.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        await AufgabenAbgleichenAsync(abbruch);
    }

    public async Task<IReadOnlyList<Portalantrag>> AntraegeAsync(bool nurOffene, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !LeseRollen.Contains(r)))
        {
            throw new PortalFehler(Freigabe.NurFuer(wer.Rolle, LeseRollen).Begruendung ?? "Keine Berechtigung.", 403);
        }

        var alle = await speicher.AntraegeAsync(null, nurOffene, abbruch);
        return wer.Rolle == Rolle.Fachbereichsleitung && wer.Fachbereiche is { Count: > 0 } fb
            ? alle.Where(a => EntscheiderFuer(a.Art).Contains(Rolle.Fachbereichsleitung) && (a.Fachbereich is null || fb.Contains(a.Fachbereich.Value))).ToList()
            : alle;
    }

    public async Task<Portalantrag> AntragAsync(Guid id, CancellationToken abbruch = default)
        => (await AntraegeAsync(false, abbruch)).FirstOrDefault(a => a.Id == id) ?? throw new PortalFehler("Anliegen nicht gefunden.", 404);

    public Task<Datei?> DateiAsync(Guid id, CancellationToken abbruch = default) => speicher.DateiAsync(id, abbruch);

    /// <summary>Darf der aktuelle Benutzer entscheiden? Begründung, wenn nicht (für den gesperrten Knopf).</summary>
    public async Task<Freigabe> DarfEntscheidenAsync(Portalantrag antrag, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(antrag);
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (antrag.Status != Antragsstatus.Offen)
        {
            return Freigabe.Nein($"Bereits {AntragAnzeige.Status(antrag.Status)} ({antrag.EntschiedenVon}).");
        }

        var rollen = EntscheiderFuer(antrag.Art);
        if (wer.Rolle is not { } r || !rollen.Contains(r))
        {
            return Freigabe.NurFuer(wer.Rolle, rollen);
        }

        return (await konten.FindenAsync(wer.Kennung, abbruch))?.PersonId == antrag.PersonId
            ? Freigabe.Nein("Eigene Anliegen entscheidet eine andere Person (Vier-Augen-Prinzip).")
            : Freigabe.Ja;
    }

    /// <summary>Entscheiden. Angenommen: Bankverbindung setzen bzw. Qualifikation eintragen (mit Nachweis aus dem Upload).</summary>
    public async Task EntscheidenAsync(Guid id, bool angenommen, string? vermerk, CancellationToken abbruch = default)
    {
        var antrag = await speicher.AntragAsync(id, abbruch) ?? throw new PortalFehler("Anliegen nicht gefunden.", 404);
        if (await DarfEntscheidenAsync(antrag, abbruch) is { Erlaubt: false } nein)
        {
            throw new PortalFehler(nein.Begruendung ?? "Keine Berechtigung.", 403);
        }

        if (!angenommen && string.IsNullOrWhiteSpace(vermerk))
        {
            throw new PortalFehler("Bitte die Ablehnung kurz begründen; die Kursleitung sieht den Vermerk im Portal.");
        }

        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (angenommen)
        {
            switch (antrag.Art)
            {
                case Antragsart.Bankverbindung:
                    await finanzen.BankverbindungSetzenAsync(antrag.PersonId, antrag.Daten["kontoinhaber"], antrag.Daten["iban"],
                        string.IsNullOrWhiteSpace(antrag.Daten.GetValueOrDefault("bic")) ? null : antrag.Daten["bic"], abbruch);
                    break;
                case Antragsart.Qualifikationsnachweis:
                    var merkmal = (await qualifikationen.PersonenmerkmaleAsync(abbruch))
                                      .FirstOrDefault(m => m.Schluessel == antrag.Daten.GetValueOrDefault("merkmal") || m.Name == antrag.Daten.GetValueOrDefault("merkmal"))
                                  ?? throw new PortalFehler("Das Merkmal steht nicht im Katalog. Bitte die Qualifikation auf der Personenseite eintragen und dann annehmen.");
                    var datei = antrag.DateiId is { } did ? await speicher.DateiAsync(did, abbruch) : null;
                    var bis = DateOnly.TryParse(antrag.Daten.GetValueOrDefault("bis"), System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : (DateOnly?)null;
                    await qualifikationen.QualifikationSetzenAsync(antrag.PersonId, merkmal.Schluessel,
                        string.IsNullOrWhiteSpace(antrag.Daten.GetValueOrDefault("wert")) ? null : antrag.Daten["wert"],
                        $"Portal-Upload {datei?.Name} (SHA-256 {datei?.Sha256[..12]}…), geprüft von {wer.Name}", null, DateOnly.FromDateTime(uhr.GetLocalNow().Date), bis, abbruch);
                    break;
                case Antragsart.Auskunft:
                    // Wie in der Verwaltung: Auskunft erstellen und als Brief archivieren – die Person findet sie unter „Dokumente“.
                    var auskunft = await datenschutz.AuskunftAsync(antrag.PersonId, abbruch);
                    await dokumente.AuskunftArchivierenAsync(auskunft, link => auskunftPdf.Erzeugen(auskunft, link), abbruch);
                    break;
                case Antragsart.Loeschung:
                    // Prüfung wie in der Verwaltung: löschen, soweit keine Aufbewahrungspflicht besteht, sonst sperren; Antwortschreiben per Mail.
                    await datenschutz.LoeschverlangenAsync(antrag.PersonId, abbruch);
                    break;
            }
        }

        antrag.Entscheiden(angenommen, wer.Anzeige, vermerk, uhr.GetUtcNow());
        await protokoll.ProtokollierenAsync("Portal.Entschieden",
            $"{AntragAnzeige.Art(antrag.Art)} von {Pseudonym.Person(antrag.PersonId)} {(angenommen ? "angenommen" : "abgelehnt")}{(vermerk is null ? "" : $": {vermerk}")}",
            "Person", antrag.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        await AufgabenAbgleichenAsync(abbruch);
    }

    /// <summary>Offene Anliegen ↔ Aufgaben (anlegen; nach der Entscheidung automatisch schließen).</summary>
    public async Task AufgabenAbgleichenAsync(CancellationToken abbruch = default)
    {
        var offen = await speicher.AntraegeAsync(null, true, abbruch);
        var namen = await personen.AnzeigenamenAsync(offen.Select(a => a.PersonId).Distinct(), abbruch);
        foreach (var gruppe in new[] { Aufgabentypen.PortalAusfall, Aufgabentypen.PortalBuchhaltung, Aufgabentypen.PortalFachbereich, Aufgabentypen.PortalDatenschutz })
        {
            var anlaesse = offen.Where(a => Aufgabentyp(a.Art) == gruppe)
                .Select(a => new AufgabenAnlass($"portal:{a.Id:N}", $"{AntragAnzeige.Art(a.Art)}: {namen.GetValueOrDefault(a.PersonId) ?? "Kursleitung"} – {a.Titel}",
                    a.Text.Length > 300 ? a.Text[..300] + " …" : a.Text, DateOnly.FromDateTime(a.EingereichtAm.ToLocalTime().Date).AddDays(a.Art == Antragsart.Ausfall ? 0 : 7),
                    $"portal-anliegen/{a.Id}", a.Fachbereich, a.Art == Antragsart.Ausfall ? Dringlichkeit.Hoch : Dringlichkeit.Normal))
                .ToList();
            await aufgaben.AbgleichenAsync(gruppe, anlaesse, grund: _ => "Anliegen entschieden", abbruch: abbruch);
        }
    }
}
