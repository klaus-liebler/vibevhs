using Microsoft.Extensions.Configuration;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Durchfuehrung;

namespace VibeVHS.Application.Durchfuehrung;

public interface IUnterrichtsappSpeicher
{
    Task<IReadOnlyList<Unterrichtsapp>> AppsAsync(CancellationToken abbruch = default);

    Task<Unterrichtsapp?> AppAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Appeinsatz>> EinsaetzeAsync(IReadOnlyCollection<Guid> kursIds, CancellationToken abbruch = default);

    Task<Appeinsatz?> EinsatzAsync(Guid id, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Ein aktiver Einsatz mit seiner App (für Portale).</summary>
public sealed record Appeinsatzzeile(Appeinsatz Einsatz, Unterrichtsapp App);

/// <summary>
/// Unterrichts-Apps (QA-69, aus SchoolCraft FK-15): Katalog der vhs (eingebaut oder angebunden), Einsatz durch die Kursleitung im
/// Dozierendenportal, Anzeige für Teilnehmende im Teilnehmendenportal. Podium ist eine eingebaute App unter mehreren (Podium:Adresse).
/// </summary>
public sealed class UnterrichtsappDienst(IUnterrichtsappSpeicher speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, IConfiguration konfiguration,
    TimeProvider uhr)
{
    public static readonly Rolle[] PflegeRollen = [Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung, Rolle.Systemverwaltung];

    /// <summary>Freigabe angebundener Apps (Datenschutz, Auftragsverarbeitung).</summary>
    public static readonly Rolle[] FreigabeRollen = [Rolle.Verwaltungsleitung, Rolle.Leitung];

    private const string Objekt = nameof(Unterrichtsapp);

    private async Task<Benutzer> RechtAsync(Rolle[] rollen, CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var f = Freigabe.NurFuer(wer.Rolle, rollen);
        if (!wer.IstAutomatik && !f.Erlaubt)
        {
            throw new InvalidOperationException(f.Begruendung);
        }

        return wer;
    }

    public async Task<IReadOnlyList<Unterrichtsapp>> KatalogAsync(CancellationToken abbruch = default)
        => (await speicher.AppsAsync(abbruch)).OrderBy(a => a.Eingebaut ? 0 : 1).ThenBy(a => a.Name, StringComparer.CurrentCulture).ToList();

    /// <summary>Podium als eingebaute App anlegen, wenn eine Adresse konfiguriert ist und es noch keinen Eintrag gibt (Grundeinstellung).</summary>
    public async Task GrundeinstellungenAsync(CancellationToken abbruch = default)
    {
        if (konfiguration["Podium:Adresse"] is not { Length: > 0 } adresse || (await speicher.AppsAsync(abbruch)).Any(a => a.Einbindung == Einbindung.Podium))
        {
            return;
        }

        speicher.Hinzufuegen(new Unterrichtsapp("Podium (Live-Quiz)", Einbindung.Podium, adresse,
            "Quiz und Abstimmung mit Leinwand: Teilnehmende spielen mit dem Handy per QR-Code oder PIN mit, ohne App und ohne Konto.",
            "Läuft auf dem Server der vhs; Teilnehmende nur mit selbst gewähltem Spitznamen, keine Daten an Dritte.", uhr.GetLocalNow()));
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task<Unterrichtsapp> AnlegenAsync(string name, Einbindung einbindung, string adresse, string beschreibung, string datenhinweis,
        CancellationToken abbruch = default)
    {
        await RechtAsync(PflegeRollen, abbruch);
        if (string.IsNullOrWhiteSpace(datenhinweis))
        {
            throw new ArgumentException("Bitte angeben, wohin Daten gehen (z. B. „Anbieter in der EU, nur Pseudonym“).", nameof(datenhinweis));
        }

        var app = new Unterrichtsapp(name, einbindung, adresse, beschreibung, datenhinweis, uhr.GetLocalNow());
        speicher.Hinzufuegen(app);
        await protokoll.ProtokollierenAsync("Unterrichtsapp.Angelegt", $"Unterrichts-App „{app.Name}“ ({(app.Eingebaut ? "eingebaut" : "angebunden")}) angelegt",
            Objekt, app.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return app;
    }

    /// <summary>Angebundene App freigeben bzw. Freigabe zurücknehmen (laufende Einsätze bleiben sichtbar, neue sind dann gesperrt).</summary>
    public async Task FreigebenAsync(Guid appId, bool freigeben, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(FreigabeRollen, abbruch);
        var app = await speicher.AppAsync(appId, abbruch) ?? throw new InvalidOperationException("App nicht gefunden.");
        if (app.Eingebaut)
        {
            throw new InvalidOperationException("Eingebaute Apps laufen auf dem Server der vhs und brauchen keine Freigabe.");
        }

        if (freigeben)
        {
            app.Freigeben(uhr.GetLocalNow(), wer.Anzeige);
        }
        else
        {
            app.FreigabeZuruecknehmen();
        }

        await protokoll.ProtokollierenAsync(freigeben ? "Unterrichtsapp.Freigegeben" : "Unterrichtsapp.FreigabeZurueck",
            $"Unterrichts-App „{app.Name}“ {(freigeben ? "freigegeben" : "nicht mehr freigegeben")}", Objekt, app.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Einsätze in den angegebenen Kursen (aktiv), mit App.</summary>
    public async Task<IReadOnlyList<Appeinsatzzeile>> EinsaetzeAsync(IReadOnlyCollection<Guid> kursIds, CancellationToken abbruch = default)
    {
        var apps = (await speicher.AppsAsync(abbruch)).ToDictionary(a => a.Id);
        return (await speicher.EinsaetzeAsync(kursIds, abbruch)).Where(e => e.Aktiv && apps.ContainsKey(e.AppId))
            .Select(e => new Appeinsatzzeile(e, apps[e.AppId])).OrderByDescending(z => z.Einsatz.AngelegtAm).ToList();
    }

    /// <summary>Einsatz durch die Kursleitung (Recht auf den Kurs prüft das Dozierendenportal vorher).</summary>
    public async Task<Appeinsatz> EinsetzenAsync(Guid kursId, Guid personId, Guid appId, string titel, string? auftrag, CancellationToken abbruch = default)
    {
        var app = await speicher.AppAsync(appId, abbruch) ?? throw new InvalidOperationException("App nicht gefunden.");
        if (!app.Nutzbar)
        {
            throw new InvalidOperationException($"„{app.Name}“ ist (noch) nicht von der vhs freigegeben. Die Freigabe erteilen Verwaltungsleitung oder Leitung.");
        }

        var einsatz = new Appeinsatz(app.Id, kursId, personId, titel, auftrag, uhr.GetLocalNow());
        speicher.Hinzufuegen(einsatz);
        await protokoll.ProtokollierenAsync("Unterrichtsapp.Eingesetzt", $"„{app.Name}“ im Kurs eingesetzt: {einsatz.Titel}", Objekt, app.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return einsatz;
    }

    public async Task BeendenAsync(Guid einsatzId, Guid kursId, CancellationToken abbruch = default)
    {
        var e = await speicher.EinsatzAsync(einsatzId, abbruch);
        if (e is null || e.KursId != kursId)
        {
            throw new InvalidOperationException("Einsatz nicht gefunden.");
        }

        e.Beenden(uhr.GetLocalNow());
        await speicher.SpeichernAsync(abbruch);
    }
}
