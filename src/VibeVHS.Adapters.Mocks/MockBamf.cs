using System.Globalization;
using System.Text;
using VibeVHS.Application.Integration;
using VibeVHS.Application.Postausgang;

namespace VibeVHS.Adapters.Mocks;

/// <summary>
/// BAMF-Mock (InGe-Online, Abrechnung, Prüfungsanmeldung): Jede Meldung landet im Postausgang (Kanal Schnittstelle) und
/// bekommt eine simulierte Vorgangsnummer. Ein echter Adapter ersetzt nur diese Klasse.
/// </summary>
public sealed class MockBamf(PostausgangDienst post, INachrichtenSpeicher nachrichten, TimeProvider uhr) : IBamfSchnittstelle
{
    private const string Kanal = "BAMF (Mock)";

    public async Task<string> KursbeginnMeldenAsync(BamfKursmeldung meldung, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(meldung);
        var vorgang = Vorgang("KB");
        var text = new StringBuilder()
            .AppendLine("Meldung: Kursbeginn (InGe-Online)")
            .AppendLine(CultureInfo.InvariantCulture, $"Vorgang: {vorgang}")
            .AppendLine(CultureInfo.InvariantCulture, $"BAMF-Kursnummer: {meldung.BamfKursnummer} (intern {meldung.Kursnummer})")
            .AppendLine(CultureInfo.InvariantCulture, $"Kursart: {meldung.Kursart}")
            .AppendLine(CultureInfo.InvariantCulture, $"Beginn: {meldung.Beginn:dd.MM.yyyy}, Ort: {meldung.Ort}")
            .AppendLine(CultureInfo.InvariantCulture, $"Teilnehmende ({meldung.Teilnehmernummern.Count}): {string.Join(", ", meldung.Teilnehmernummern)}")
            .ToString();
        await SendenAsync($"Kursbeginnmeldung {meldung.BamfKursnummer}", text, abbruch);
        return vorgang;
    }

    public async Task<string> AbrechnungUebermittelnAsync(BamfAbrechnung abrechnung, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(abrechnung);
        var vorgang = Vorgang("AB");
        await SendenAsync($"Abrechnung {abrechnung.BamfKursnummer} {abrechnung.Abschnitt}",
            $"Meldung: Kostenerstattung Integrationskurs\nVorgang: {vorgang}\nAbschnitt: {abrechnung.Abschnitt} ({abrechnung.Von:dd.MM.yyyy}–{abrechnung.Bis:dd.MM.yyyy})\n"
            + $"Summe: {(abrechnung.SummeCent / 100m).ToString("N2", CultureInfo.GetCultureInfo("de-DE"))} €\n\n{abrechnung.Csv}", abbruch);
        return vorgang;
    }

    public async Task<string> PruefungAnmeldenAsync(BamfPruefungsmeldung meldung, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(meldung);
        var vorgang = Vorgang("PR");
        await SendenAsync($"Prüfungsanmeldung {meldung.Art} {meldung.Beginn:dd.MM.yyyy}",
            $"Meldung: Prüfungsanmeldung {meldung.Art}\nVorgang: {vorgang}\nTermin: {meldung.Beginn:dd.MM.yyyy HH:mm}, {meldung.Ort}\nTeilnehmende: {string.Join(", ", meldung.Teilnehmernummern)}", abbruch);
        return vorgang;
    }

    private async Task SendenAsync(string betreff, string inhalt, CancellationToken abbruch)
    {
        await post.SchnittstelleEinreihenAsync(Kanal, betreff, inhalt, abbruch);
        await nachrichten.SpeichernAsync(abbruch);
    }

    private string Vorgang(string art) => $"BAMF-MOCK-{art}-{uhr.GetLocalNow():yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
}
