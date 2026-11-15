using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Vorlagen;

namespace VibeVHS.Application.Anmeldung;

/// <summary>Zentrale Pflege der Mail- und Briefvorlagen (QA-24) mit Platzhaltern und Vorschau.</summary>
public sealed class VorlagenDienst(IVorlagenSpeicher speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    public static readonly Rolle[] PflegeRollen = [Rolle.Verwaltungsleitung, Rolle.Leitung];

    /// <summary>Legt fehlende Vorlagen mit den mitgelieferten Vorschlägen an (bestehende bleiben unverändert).</summary>
    public async Task GrundeinstellungenAsync(CancellationToken abbruch = default)
    {
        var vorhanden = (await speicher.VorlagenAsync(abbruch)).Select(v => v.Schluessel).ToHashSet(StringComparer.Ordinal);
        var neu = 0;
        foreach (var (schluessel, bezeichnung, betreff, text) in Textvorlagen.Vorschlaege.Where(v => !vorhanden.Contains(v.Schluessel)))
        {
            speicher.Hinzufuegen(new Textvorlage(schluessel, bezeichnung, betreff, text, uhr.GetUtcNow(), "Vorschlag"));
            neu++;
        }

        if (neu > 0)
        {
            await speicher.SpeichernAsync(abbruch);
        }
    }

    public Task<IReadOnlyList<Textvorlage>> AlleAsync(CancellationToken abbruch = default) => speicher.VorlagenAsync(abbruch);

    public async Task AendernAsync(string schluessel, string betreff, string text, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (wer.Rolle is not { } r || !PflegeRollen.Contains(r))
        {
            throw new InvalidOperationException("Vorlagen pflegen nur Verwaltungsleitung und Leitung.");
        }

        var vorlage = await speicher.VorlageAsync(schluessel, abbruch) ?? throw new InvalidOperationException("Vorlage nicht gefunden.");
        vorlage.Aendern(betreff, text, uhr.GetUtcNow(), wer.Anzeige);
        await protokoll.ProtokollierenAsync("Vorlage.Geaendert", $"Vorlage „{vorlage.Bezeichnung}“ geändert", "Textvorlage", null, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Vorschau mit Beispielwerten (ohne zu speichern).</summary>
    public static (string Betreff, string Text) Vorschau(string betreff, string text)
        => new Textvorlage("vorschau", "Vorschau", betreff, text, DateTimeOffset.MinValue, "").Fuellen(Textvorlagen.Beispielwerte);
}
