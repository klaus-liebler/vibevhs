using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

/// <summary>
/// Pflege des gemeinsamen Merkmalskatalogs (Orte und Personen): aufnehmen, Werttyp festlegen (nach der ersten Verwendung
/// gesperrt), Auswahlwerte ergänzen oder deaktivieren, Raumbedingungen der Kursarten. Jede Änderung wird protokolliert.
/// </summary>
public sealed class MerkmalKatalogDienst(IQualifikationsSpeicher speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer)
{
    public static readonly Rolle[] KatalogRollen = [Rolle.Systemverwaltung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    public static readonly Rolle[] BedingungsRollen = [Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    public async Task<IReadOnlyList<Merkmal>> KatalogAsync(MerkmalBezug bezug, CancellationToken abbruch = default)
        => (await speicher.KatalogAsync(abbruch)).Values.Where(m => m.Bezug == bezug).OrderBy(m => m.Gruppe).ThenBy(m => m.Name, StringComparer.CurrentCulture).ToList();

    public Task<IReadOnlySet<string>> VerwendeteAsync(CancellationToken abbruch = default) => speicher.VerwendeteMerkmaleAsync(abbruch);

    public async Task AufnehmenAsync(Merkmal merkmal, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(merkmal);
        await RechtAsync(KatalogRollen, abbruch);
        if ((await speicher.KatalogAsync(abbruch)).ContainsKey(merkmal.Schluessel))
        {
            throw new InvalidOperationException("Diesen Schlüssel gibt es schon.");
        }

        speicher.Hinzufuegen(merkmal);
        await protokoll.ProtokollierenAsync("Merkmal.Aufgenommen",
            $"Merkmal „{merkmal.Name}“ ({(merkmal.Bezug == MerkmalBezug.Ort ? "Ort" : "Person")}, {MerkmalstypAnzeige.Text(merkmal.Typ)}{Typdetails(merkmal)}) aufgenommen", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task TypFestlegenAsync(string schluessel, Merkmalstyp typ, IReadOnlyList<string>? werte, bool mehrfach, string? einheit, int? min, int? max,
        CancellationToken abbruch = default)
    {
        await RechtAsync(KatalogRollen, abbruch);
        var m = await LadenAsync(schluessel, abbruch);
        var verwendet = (await speicher.VerwendeteMerkmaleAsync(abbruch)).Contains(m.Schluessel);
        var vorher = $"{MerkmalstypAnzeige.Text(m.Typ)}{Typdetails(m)}";
        m.TypFestlegen(typ, verwendet, werte, mehrfach, einheit, min, max);
        await protokoll.ProtokollierenAsync("Merkmal.Typ", $"„{m.Name}“: {vorher} → {MerkmalstypAnzeige.Text(m.Typ)}{Typdetails(m)}", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task WertErgaenzenAsync(string schluessel, string wert, CancellationToken abbruch = default)
    {
        await RechtAsync(KatalogRollen, abbruch);
        var m = await LadenAsync(schluessel, abbruch);
        m.WertErgaenzen(wert);
        await protokoll.ProtokollierenAsync("Merkmal.Wert", $"„{m.Name}“: Auswahlwert „{wert.Trim()}“ ergänzt bzw. wieder aktiviert", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task WertDeaktivierenAsync(string schluessel, string wert, CancellationToken abbruch = default)
    {
        await RechtAsync(KatalogRollen, abbruch);
        var m = await LadenAsync(schluessel, abbruch);
        m.WertDeaktivieren(wert);
        await protokoll.ProtokollierenAsync("Merkmal.Wert", $"„{m.Name}“: Auswahlwert „{wert.Trim()}“ deaktiviert (bestehende Einträge bleiben gültig)", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Raumbedingungen einer Kursart (z. B. „pc&gt;=12“, „tafel=Smartboard“); gelten auch für die Kurse dieser Art.</summary>
    public async Task KursartOrtsanforderungenSetzenAsync(Guid kursartId, IReadOnlyList<string> bedingungen, CancellationToken abbruch = default)
    {
        await RechtAsync(BedingungsRollen, abbruch);
        var katalog = await speicher.KatalogAsync(abbruch);
        var geprueft = bedingungen.Select(b => Anforderung.Pruefen(b, katalog)).ToList();
        if (geprueft.Any(b => katalog[Anforderung.Basis(b)].Bezug != MerkmalBezug.Ort))
        {
            throw new ArgumentException("Raumbedingungen können nur Merkmale für Orte verwenden.");
        }

        var art = await speicher.KursartLadenAsync(kursartId, abbruch) ?? throw new InvalidOperationException("Kursart nicht gefunden.");
        art.MitOrtsanforderungen(geprueft);
        foreach (var kurs in await speicher.KurseDerKursartAsync(kursartId, abbruch))
        {
            kurs.MitOrtsanforderungen(art.BenoetigteMerkmale);
        }

        await protokoll.ProtokollierenAsync("Kursart.Ortsanforderungen",
            $"Kursart „{art.Name}“ verlangt vom Ort: {(geprueft.Count == 0 ? "nichts" : string.Join(", ", art.BenoetigteMerkmale.Select(b => Anforderung.Anzeige(b, katalog))))}",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    private static string Typdetails(Merkmal m) => m.Typ switch
    {
        Merkmalstyp.Auswahl => $": {string.Join(" / ", m.Werte)}{(m.Mehrfach ? ", Mehrfachauswahl" : "")}",
        Merkmalstyp.Ganzzahl => $"{(m.Einheit is null ? "" : $" in {m.Einheit}")}{(m.Min is null && m.Max is null ? "" : $" ({m.Min?.ToString() ?? "…"}–{m.Max?.ToString() ?? "…"})")}",
        _ => "",
    };

    private async Task<Merkmal> LadenAsync(string schluessel, CancellationToken abbruch)
        => await speicher.MerkmalLadenAsync(schluessel, abbruch) ?? throw new InvalidOperationException("Merkmal nicht im Katalog.");

    private async Task RechtAsync(Rolle[] rollen, CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !rollen.Contains(r)))
        {
            throw new InvalidOperationException($"Das dürfen: {string.Join(", ", rollen.Select(x => x.Anzeigename()))}.");
        }
    }
}
