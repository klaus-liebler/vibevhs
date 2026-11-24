using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Postausgang;

/// <summary>
/// Einziger Weg, Nachrichten nach außen zu schicken. Legt sie in den Postausgang (Outbox),
/// ergänzt bei Kundenmails den Übersetzungslink und protokolliert den Vorgang.
/// </summary>
public sealed class PostausgangDienst(
    INachrichtenSpeicher speicher,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    IUebersetzungslinks uebersetzungslinks,
    TimeProvider uhr)
{
    /// <summary>
    /// Reiht eine Mail an eine Kundin oder einen Kunden ein. Ein Übersetzungslink wird immer angehängt.
    /// Gespeichert wird erst mit <see cref="INachrichtenSpeicher.SpeichernAsync"/>, damit Mail und
    /// fachliche Änderung gemeinsam festgeschrieben werden.
    /// </summary>
    public async Task<AusgehendeNachricht> KundenmailEinreihenAsync(string eMail, string? anzeigename, string betreff,
        string text, string sprache, Guid? personId = null, CancellationToken abbruch = default,
        (string Name, string Typ, string Inhalt)? anhang = null, IEnumerable<Mailanhang>? weitere = null, string? html = null)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var id = Ids.Neu();
        var link = uebersetzungslinks.FuerNachricht(id, sprache);
        var inhalt = text.TrimEnd() + Environment.NewLine + Environment.NewLine + MailBausteine.Uebersetzungshinweis(sprache, link);

        var nachricht = new AusgehendeNachricht(Kanal.EMail, eMail, anzeigename, betreff, inhalt, sprache,
            uhr.GetUtcNow(), wer.Anzeige, personId, link, id);
        if (anhang is { } a)
        {
            nachricht.MitAnhang(a.Name, a.Typ, a.Inhalt);
        }

        foreach (var w in weitere ?? [])
        {
            nachricht.MitWeiteremAnhang(w);
        }

        if (html is not null)
        {
            // Übersetzungslink auch im HTML-Teil (jede Mail an Kundinnen und Kunden, QA-21)
            var hinweis = System.Net.WebUtility.HtmlEncode(MailBausteine.Uebersetzungshinweis(sprache, link)).ReplaceLineEndings("<br>");
            nachricht.MitHtml($"{html}<p style=\"font-size: 12px; color: #6b7280; margin-top: 16px;\">{hinweis}</p>");
        }

        speicher.Hinzufuegen(nachricht);
        await protokoll.ProtokollierenAsync("Postausgang.MailEingereiht",
            $"Mail an {(personId is { } p ? Domain.Datenschutz.Pseudonym.Person(p) : "externe Adresse")} in den Postausgang gelegt", nameof(AusgehendeNachricht), nachricht.Id,
            abbruch: abbruch);
        return nachricht;
    }

    /// <summary>Reiht einen Schnittstellenaufruf ein (BAMF, Finanzsoftware, Statistik ...).</summary>
    public async Task<AusgehendeNachricht> SchnittstelleEinreihenAsync(string schnittstelle, string betreff, string inhalt,
        CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var nachricht = new AusgehendeNachricht(Kanal.Schnittstelle, schnittstelle, schnittstelle, betreff, inhalt, "de",
            uhr.GetUtcNow(), wer.Anzeige);
        speicher.Hinzufuegen(nachricht);
        await protokoll.ProtokollierenAsync("Postausgang.SchnittstelleEingereiht",
            $"Übergabe an {schnittstelle}: „{betreff}“ in den Postausgang gelegt", nameof(AusgehendeNachricht), nachricht.Id,
            abbruch: abbruch);
        return nachricht;
    }

    /// <summary>Fehlgeschlagene Nachricht nach Prüfung erneut einreihen.</summary>
    public async Task ErneutVersuchenAsync(Guid id, CancellationToken abbruch = default)
    {
        var nachricht = await speicher.LadenAsync(id, abbruch)
            ?? throw new InvalidOperationException("Nachricht nicht gefunden.");
        nachricht.ErneutVersuchen();
        await protokoll.ProtokollierenAsync("Postausgang.ErneutVersuchen",
            "Nachricht erneut zur Zustellung freigegeben", nameof(AusgehendeNachricht), id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }
}
