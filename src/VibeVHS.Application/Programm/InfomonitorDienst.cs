using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

public interface IInfomonitorSpeicher
{
    Task<IReadOnlyList<Infomonitor>> AlleAsync(CancellationToken abbruch = default);

    Task<Infomonitor?> LadenAsync(Guid id, CancellationToken abbruch = default);

    /// <summary>Monitor zum Prüfwert der Geräteadresse oder der Vorschau.</summary>
    Task<Infomonitor?> ZumPruefwertAsync(string pruefwert, CancellationToken abbruch = default);

    void Hinzufuegen(Infomonitor monitor);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Eine Zeile auf dem Bildschirm: nur Kurs, Zeit und Raum – nie Teilnehmende.</summary>
public sealed record Monitorzeile(DateTime Beginn, DateTime Ende, string Kursnummer, string Titel, string Raum, bool Ausgefallen);

public sealed record Monitorinhalt(string Ueberschrift, string Ort, DateTimeOffset Stand, IReadOnlyList<Monitorzeile> Jetzt, IReadOnlyList<Monitorzeile> Spaeter,
    bool Vorschau);

/// <summary>
/// Info-Monitore im Foyer (QA-65, aus SchoolCraft FA-412): „Heute im Haus“ je Standort, Ausfälle sichtbar markiert. Pflege durch
/// Verwaltungsleitung, Leitung und Systemverwaltung; der Bildschirm selbst ruft ohne Anmeldung eine geheime Adresse auf.
/// </summary>
public sealed class InfomonitorDienst(IInfomonitorSpeicher speicher, IProgrammSpeicher programm, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    public static readonly Rolle[] PflegeRollen = [Rolle.Verwaltungsleitung, Rolle.Leitung, Rolle.Systemverwaltung];

    /// <summary>So viele kommende Termine zeigt der Bildschirm höchstens.</summary>
    public const int HoechstensSpaeter = 14;

    private const string Objekt = nameof(Infomonitor);

    private async Task RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var f = Freigabe.NurFuer(wer.Rolle, PflegeRollen);
        if (!f.Erlaubt)
        {
            throw new InvalidOperationException(f.Begruendung);
        }
    }

    public async Task<IReadOnlyList<Infomonitor>> ListeAsync(CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        return (await speicher.AlleAsync(abbruch)).OrderBy(m => m.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <returns>Monitor und Geheimnis der Geräteadresse (nur jetzt sichtbar).</returns>
    public async Task<(Infomonitor Monitor, string Geheimnis)> AnlegenAsync(string name, Guid? ortId, string? ueberschrift, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Bitte einen Namen angeben, z. B. „Foyer Emsdetten“.", nameof(name));
        }

        var (m, geheimnis) = Infomonitor.Anlegen(name, ortId, ueberschrift, uhr.GetLocalNow());
        speicher.Hinzufuegen(m);
        await protokoll.ProtokollierenAsync("Infomonitor.Angelegt", $"Info-Monitor „{m.Name}“ angelegt", Objekt, m.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return (m, geheimnis);
    }

    /// <summary>Neue Geräteadresse; die alte gilt sofort nicht mehr.</summary>
    public async Task<string> NeueAdresseAsync(Guid id, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var m = await speicher.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Monitor nicht gefunden.");
        var geheimnis = m.NeuesGeheimnis();
        await protokoll.ProtokollierenAsync("Infomonitor.NeueAdresse", $"Info-Monitor „{m.Name}“: neue Adresse, alte gesperrt", Objekt, m.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return geheimnis;
    }

    /// <summary>Vorschau-Adresse (30 Minuten in echter Zeit), die Adresse des Geräts ändert sich nicht.</summary>
    public async Task<string> VorschauAsync(Guid id, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var m = await speicher.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Monitor nicht gefunden.");
        var geheimnis = m.Vorschau(uhr.GetUtcNow());
        await speicher.SpeichernAsync(abbruch);
        return geheimnis;
    }

    public async Task SperrenAsync(Guid id, bool sperren, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var m = await speicher.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Monitor nicht gefunden.");
        if (sperren)
        {
            m.Sperren(uhr.GetLocalNow());
        }
        else
        {
            m.Entsperren();
        }

        await protokoll.ProtokollierenAsync(sperren ? "Infomonitor.Gesperrt" : "Infomonitor.Entsperrt", $"Info-Monitor „{m.Name}“ {(sperren ? "gesperrt" : "entsperrt")}",
            Objekt, m.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Inhalt für den Bildschirm (ohne Anmeldung); <c>null</c>, wenn die Adresse nicht (mehr) gilt.</summary>
    public async Task<Monitorinhalt?> InhaltAsync(string geheimnis, CancellationToken abbruch = default)
    {
        if (string.IsNullOrWhiteSpace(geheimnis) || geheimnis.Length > 100)
        {
            return null;
        }

        var pruefwert = Infomonitor.HashVon(geheimnis);
        var m = await speicher.ZumPruefwertAsync(pruefwert, abbruch);
        if (m is null || !m.Passt(pruefwert, uhr.GetUtcNow()))
        {
            return null;
        }

        var vorschau = pruefwert != m.Pruefwert;
        if (!vorschau)
        {
            m.Abgerufen(uhr.GetLocalNow());
            await speicher.SpeichernAsync(abbruch);
        }

        var jetzt = uhr.GetLocalNow();
        var heute = jetzt.Date;
        var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
        var standort = m.OrtId is { } o ? verzeichnis.Finden(o) : null;
        var orte = standort is null ? null : Unterorte(verzeichnis, standort.Id);
        var termine = (await programm.TermineAsync(heute, heute.AddDays(1), abbruch))
            .Where(t => orte is null || t.Termin.OrtIds.Any(orte.Contains))
            .Select(t => new Monitorzeile(t.Termin.Beginn, t.Termin.Ende, t.Kursnummer, t.Termin.Kurs, string.Join(", ", t.OrtNamen), t.Termin.Ausgefallen))
            .ToList();
        var lokal = jetzt.LocalDateTime;
        return new Monitorinhalt(m.Ueberschrift ?? "Heute im Haus", standort?.Name ?? "alle Standorte", jetzt,
            termine.Where(t => t.Beginn <= lokal && t.Ende > lokal).ToList(),
            termine.Where(t => t.Beginn > lokal).OrderBy(t => t.Beginn).Take(HoechstensSpaeter).ToList(), vorschau);
    }

    private static HashSet<Guid> Unterorte(Ortsverzeichnis verzeichnis, Guid wurzel)
    {
        var ergebnis = new HashSet<Guid> { wurzel };
        var offen = new Queue<Guid>([wurzel]);
        while (offen.TryDequeue(out var id))
        {
            foreach (var kind in verzeichnis.Kinder(id).Where(k => ergebnis.Add(k.Id)))
            {
                offen.Enqueue(kind.Id);
            }
        }

        return ergebnis;
    }
}
