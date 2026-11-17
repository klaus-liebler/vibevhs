using System.Text;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Dokumentation;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Dokumentation;

/// <summary>Kommentare zu Spezifikation, Hilfe und Doku. Liegen in einer eigenen Datei, die das Neuanlegen der Hauptdatenbank übersteht.</summary>
public interface IKommentarSpeicher
{
    Task<IReadOnlyList<Kommentar>> ZuDokumentAsync(string dokument, CancellationToken abbruch = default);

    /// <summary>Alle Kommentare, nach Dokument, Abschnitt und Zeit sortiert.</summary>
    Task<IReadOnlyList<Kommentar>> AlleAsync(CancellationToken abbruch = default);

    Task<Kommentar?> LadenAsync(Guid id, CancellationToken abbruch = default);

    void Hinzufuegen(Kommentar kommentar);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Ein Faden: erster Kommentar und seine Antworten (älteste zuerst).</summary>
public sealed record Kommentarfaden(Kommentar Start, IReadOnlyList<Kommentar> Antworten);

/// <summary>
/// Zugang zur Spezifikation und Kommentarfunktion für Leitung, Verwaltungsleitung und Systemverwaltung.
/// Rechte werden hier geprüft, nicht nur in der Oberfläche.
/// </summary>
public sealed class KommentarDienst(IKommentarSpeicher speicher, IAktuellerBenutzer benutzer, IAuditProtokoll protokoll, TimeProvider uhr)
{
    public static readonly Rolle[] Rollen = [Rolle.Leitung, Rolle.Verwaltungsleitung, Rolle.Systemverwaltung];

    public async Task<Freigabe> RechtAsync(CancellationToken abbruch = default)
        => Freigabe.NurFuer((await benutzer.ErmittelnAsync(abbruch)).Rolle, Rollen);

    /// <summary>Fäden eines Dokuments, je Abschnitt (Schlüssel = Anker, leer = ganzes Dokument).</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Kommentarfaden>>> ZuDokumentAsync(string dokument, CancellationToken abbruch = default)
    {
        await PruefenAsync(abbruch);
        return Faeden(await speicher.ZuDokumentAsync(dokument, abbruch))
            .GroupBy(f => f.Start.Abschnitt)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Kommentarfaden>)g.ToList());
    }

    public async Task<IReadOnlyList<Kommentarfaden>> AlleAsync(CancellationToken abbruch = default)
    {
        await PruefenAsync(abbruch);
        return Faeden(await speicher.AlleAsync(abbruch));
    }

    public async Task<Kommentar> SchreibenAsync(string dokument, string abschnitt, string abschnittTitel, string text,
        Guid? antwortAuf = null, CancellationToken abbruch = default)
    {
        var wer = await PruefenAsync(abbruch);
        if (antwortAuf is { } startId)
        {
            var start = await speicher.LadenAsync(startId, abbruch) ?? throw new InvalidOperationException("Der Kommentar, auf den Sie antworten, existiert nicht mehr.");
            if (start.AntwortAuf is { } oberster)
            {
                startId = oberster; // Antworten hängen immer am ersten Kommentar des Fadens.
                start = await speicher.LadenAsync(startId, abbruch) ?? start;
            }

            (dokument, abschnitt, abschnittTitel) = (start.Dokument, start.Abschnitt, start.AbschnittTitel);
            antwortAuf = startId;
        }

        var k = new Kommentar(dokument, abschnitt, abschnittTitel, text, wer.Kennung, wer.Name, wer.Rolle?.Anzeigename(), uhr.GetUtcNow(), antwortAuf);
        speicher.Hinzufuegen(k);
        await speicher.SpeichernAsync(abbruch);
        await protokoll.ProtokollierenAsync("Dokumentation.Kommentar",
            $"{(antwortAuf is null ? "Kommentar" : "Antwort")} zu {k.Dokument}{(k.Abschnitt.Length > 0 ? "#" + k.Abschnitt : "")}", "Kommentar", k.Id, abbruch: abbruch);
        return k;
    }

    public async Task ErledigtSetzenAsync(Guid id, bool erledigt, CancellationToken abbruch = default)
    {
        var wer = await PruefenAsync(abbruch);
        var k = await speicher.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Der Kommentar existiert nicht mehr.");
        k.ErledigtSetzen(erledigt, wer.Name, uhr.GetUtcNow());
        await speicher.SpeichernAsync(abbruch);
        await protokoll.ProtokollierenAsync("Dokumentation.Kommentar", $"Kommentar zu {k.Dokument} {(erledigt ? "erledigt" : "wieder geöffnet")}",
            "Kommentar", k.Id, abbruch: abbruch);
    }

    /// <summary>Alle Kommentare als Markdown (zum Einarbeiten in die Spezifikation), offene zuerst.</summary>
    public async Task<string> AlsMarkdownAsync(CancellationToken abbruch = default)
    {
        var faeden = await AlleAsync(abbruch);
        var sb = new StringBuilder();
        sb.Append("# Kommentare zu Spezifikation und Doku\n\n");
        sb.Append($"Stand: {Zeitzone.Ortszeit(uhr.GetUtcNow()):dd.MM.yyyy HH:mm} · {faeden.Count(f => !f.Start.Erledigt)} offen, {faeden.Count(f => f.Start.Erledigt)} erledigt\n");
        foreach (var gruppe in faeden.OrderBy(f => f.Start.Erledigt).GroupBy(f => f.Start.Erledigt))
        {
            sb.Append($"\n## {(gruppe.Key ? "Erledigt" : "Offen")}\n");
            foreach (var dokument in gruppe.GroupBy(f => f.Start.Dokument).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.Append($"\n### {dokument.Key}\n");
                foreach (var f in dokument)
                {
                    var wo = f.Start.AbschnittTitel.Length > 0 ? $"„{f.Start.AbschnittTitel}“ (#{f.Start.Abschnitt})" : "ganzes Dokument";
                    sb.Append($"\n- **{wo}** – {Zeile(f.Start)}");
                    if (f.Start.Erledigt)
                    {
                        sb.Append($" _(erledigt von {f.Start.ErledigtVonName}, {Zeitzone.Ortszeit(f.Start.ErledigtAm!.Value):dd.MM.yyyy})_");
                    }

                    sb.Append('\n').Append(Eingerueckt(f.Start.Text, "  "));
                    foreach (var a in f.Antworten)
                    {
                        sb.Append($"  - Antwort: {Zeile(a)}\n").Append(Eingerueckt(a.Text, "    "));
                    }
                }
            }
        }

        return sb.ToString();

        static string Zeile(Kommentar k) => $"{k.VerfasstVonName}{(k.VerfasstVonRolle is { } r ? $" ({r})" : "")}, {Zeitzone.Ortszeit(k.Am):dd.MM.yyyy HH:mm}";
        static string Eingerueckt(string text, string einzug) => string.Concat(text.Split('\n').Select(z => $"{einzug}> {z}\n"));
    }

    private async Task<Benutzer> PruefenAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var recht = Freigabe.NurFuer(wer.Rolle, Rollen);
        return recht.Erlaubt ? wer : throw new InvalidOperationException("Spezifikation und Kommentare: " + recht.Begruendung);
    }

    private static List<Kommentarfaden> Faeden(IReadOnlyList<Kommentar> alle)
    {
        var antworten = alle.Where(k => k.AntwortAuf is not null).ToLookup(k => k.AntwortAuf!.Value);
        return alle.Where(k => k.AntwortAuf is null)
            .OrderBy(k => k.Dokument, StringComparer.Ordinal).ThenBy(k => k.Am).ThenBy(k => k.Id)
            .Select(k => new Kommentarfaden(k, antworten[k.Id].OrderBy(a => a.Am).ThenBy(a => a.Id).ToList()))
            .ToList();
    }
}
