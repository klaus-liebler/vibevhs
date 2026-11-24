using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

public sealed record RundschreibenVorschau(int Treffer, int MitEMail, int Empfaenger, int OhneEinwilligung);

public sealed record RundschreibenErgebnis(int Eingereiht, int Uebersprungen);

/// <summary>
/// "Alle n Treffer anschreiben": legt je Person eine Mail in den Postausgang. Für Werbung nur an Personen mit
/// Einwilligung (E-Mail · Werbung); für Kursorganisation an alle mit E-Mail-Adresse.
/// </summary>
public sealed class Rundschreiben(
    IPersonenAbfrage abfrage,
    PostausgangDienst postausgang,
    INachrichtenSpeicher speicher,
    IAuditProtokoll protokoll,
    TimeProvider uhr,
    Stammdaten.Briefkopf? briefkopf = null,
    Sicherheit.IAktuellerBenutzer? benutzer = null)
{
    /// <summary>Beispielwerte für die Vorschau (keine echte Person).</summary>
    public static readonly IReadOnlyDictionary<string, string> Musterwerte = new Dictionary<string, string>
    {
        ["Anrede"] = "Sehr geehrte Frau Musterfrau", ["Vorname"] = "Erika", ["Nachname"] = "Musterfrau", ["Name"] = "Erika Musterfrau",
    };

    private Domain.Vorlagen.Kopfdaten Kopf => new(briefkopf?.Name ?? "Volkshochschule", null, briefkopf?.Website);

    /// <summary>Vorschau mit Musterwerten in der gewählten Gestaltung (so sieht es in der Mail aus).</summary>
    public string VorschauHtml(string? html, Domain.Vorlagen.Gestaltung gestaltung)
        => Domain.Vorlagen.Briefgestaltung.Rahmen(Domain.Vorlagen.Rundschreibenplatzhalter.Ersetzen(Domain.Vorlagen.HtmlBereinigung.Bereinigen(html), Musterwerte, html: true),
            gestaltung, Kopf);

    /// <summary>Archiv gesendeter Rundschreiben, neueste zuerst (zum Ansehen und als Vorlage).</summary>
    public Task<IReadOnlyList<Domain.Vorlagen.GesendetesRundschreiben>> ArchivAsync(int anzahl = 50, CancellationToken abbruch = default)
        => speicher.RundschreibenAsync(anzahl, abbruch);

    /// <summary>
    /// Rundschreiben mit formatiertem Text (QA-66): HTML wird bereinigt, Platzhalter je Person ersetzt ({Anrede} nach Anredestil bzw. der
    /// gespeicherten Ansprache der Person), Mail mit HTML- und Textteil in der Gestaltung; danach ein Eintrag im Archiv.
    /// </summary>
    public async Task<RundschreibenErgebnis> SendenGestaltetAsync(PersonenFilter filter, Kontaktzweck zweck, string betreff, string html,
        Domain.Vorlagen.Gestaltung gestaltung, Anredestil stil, string zielgruppe, CancellationToken abbruch = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(betreff);
        var sauber = Domain.Vorlagen.HtmlBereinigung.Bereinigen(html);
        if (string.IsNullOrWhiteSpace(Domain.Vorlagen.HtmlBereinigung.AlsText(sauber)))
        {
            throw new ArgumentException("Bitte einen Text eingeben.", nameof(html));
        }

        int eingereiht = 0, uebersprungen;
        var zahlen = await abfrage.RundschreibenZaehlenAsync(filter, zweck, abbruch);
        uebersprungen = zahlen.Treffer - zahlen.Empfaenger;
        var jetzt = uhr.GetLocalNow();
        await foreach (var person in abfrage.RundschreibenEmpfaengerAsync(filter, zweck, abbruch))
        {
            if (person.EMail is not { } mail)
            {
                uebersprungen++;
                continue;
            }

            var stand = person.StandAm(DateOnly.FromDateTime(jetzt.Date), jetzt);
            var werte = new Dictionary<string, string>
            {
                ["Anrede"] = person.Briefanrede(jetzt, stil), ["Vorname"] = stand.Name?.Vorname ?? string.Empty,
                ["Nachname"] = stand.Name?.NachnameMitVorsatz ?? string.Empty, ["Name"] = person.Anzeigename,
            };
            var inhalt = Domain.Vorlagen.Rundschreibenplatzhalter.Ersetzen(sauber, werte, html: true);
            await postausgang.KundenmailEinreihenAsync(mail, person.Anzeigename, Domain.Vorlagen.Rundschreibenplatzhalter.Ersetzen(betreff, werte, html: false),
                Domain.Vorlagen.HtmlBereinigung.AlsText(inhalt), person.Sprache, person.Id, abbruch, html: Domain.Vorlagen.Briefgestaltung.Rahmen(inhalt, gestaltung, Kopf));
            eingereiht++;
        }

        var wer = benutzer is null ? "Automatik" : (await benutzer.ErmittelnAsync(abbruch)).Anzeige;
        speicher.RundschreibenArchivieren(new Domain.Vorlagen.GesendetesRundschreiben(betreff, sauber, gestaltung, stil, zweck, zielgruppe, eingereiht, uebersprungen,
            uhr.GetLocalNow(), wer));
        await protokoll.ProtokollierenAsync("Personen.Rundschreiben",
            $"Rundschreiben „{betreff}“ ({Einwilligung.ZweckAnzeige(zweck)}, {Domain.Vorlagen.Briefgestaltung.Anzeige(gestaltung)}): {eingereiht} Mails in den Postausgang, {uebersprungen} übersprungen",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new RundschreibenErgebnis(eingereiht, uebersprungen);
    }

    public Task<RundschreibenVorschau> VorschauAsync(PersonenFilter filter, Kontaktzweck zweck, CancellationToken abbruch = default)
        => abfrage.RundschreibenZaehlenAsync(filter, zweck, abbruch);

    public async Task<RundschreibenErgebnis> SendenAsync(PersonenFilter filter, Kontaktzweck zweck, string betreff, string text,
        CancellationToken abbruch = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(betreff);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        int eingereiht = 0, uebersprungen = 0;
        var zahlen = await abfrage.RundschreibenZaehlenAsync(filter, zweck, abbruch);
        uebersprungen = zahlen.Treffer - zahlen.Empfaenger;
        await foreach (var person in abfrage.RundschreibenEmpfaengerAsync(filter, zweck, abbruch))
        {
            if (person.EMail is not { } mail)
            {
                uebersprungen++;
                continue;
            }

            await postausgang.KundenmailEinreihenAsync(mail, person.Anzeigename, betreff,
                $"{person.Briefanrede(uhr.GetLocalNow())},\n\n{text.Trim()}", person.Sprache, person.Id, abbruch);
            eingereiht++;
        }

        await protokoll.ProtokollierenAsync("Personen.Rundschreiben",
            $"Rundschreiben „{betreff}“ ({Einwilligung.ZweckAnzeige(zweck)}): {eingereiht} Mails in den Postausgang, {uebersprungen} übersprungen",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new RundschreibenErgebnis(eingereiht, uebersprungen);
    }
}
