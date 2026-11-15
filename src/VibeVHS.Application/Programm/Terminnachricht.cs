using System.Globalization;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Programm;

/// <summary>
/// Nachricht der Verwaltung an die Teilnehmenden eines Termins, z. B. nach einem Ausfall: über den Postausgang, mit
/// Übersetzungslink in der gewünschten Sprache (wie die Nachricht der Kursleitung im Dozierendenportal). Teilnehmende ohne
/// E-Mail-Adresse werden gezählt, damit die Verwaltung sie anrufen oder anschreiben kann.
/// </summary>
public sealed class Terminnachricht(IProgrammSpeicher programm, IAnmeldungSpeicher anmeldungen, IPersonenSpeicher personen, PostausgangDienst post,
    IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static readonly Rolle[] Rollen = [Rolle.Fachbereichsleitung, Rolle.Leitung, Rolle.Verwaltungsleitung, Rolle.Teilnehmerservice];

    /// <summary>Vorschlag für den Text nach einem Ausfall.</summary>
    public static string AusfallText(Kurs kurs, Veranstaltung v)
        => $"der Termin Ihres Kurses „{kurs.Titel}“ ({kurs.Kursnummer}) am {v.Beginn.ToString("dddd, dd.MM.yyyy, HH:mm", De)} Uhr fällt leider aus."
           + (string.IsNullOrWhiteSpace(v.Bemerkung) || v.Bemerkung == "ohne Angabe" ? "" : $" Grund: {v.Bemerkung}.")
           + "\n\nÜber einen Ersatztermin informieren wir Sie, sobald er feststeht. Bei Fragen erreichen Sie uns telefonisch oder per E-Mail.";

    /// <returns>Anzahl versandter Mails und Anzahl Teilnehmender ohne E-Mail-Adresse.</returns>
    public async Task<(int Versandt, int OhneMail)> SendenAsync(Guid kursId, Guid veranstaltungId, string betreff, string text, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (wer.Rolle is not { } r || !Rollen.Contains(r))
        {
            throw new InvalidOperationException("Nachrichten an Teilnehmende verschicken Fachbereichsleitung, Leitung, Verwaltungsleitung und Teilnehmerservice.");
        }

        if (string.IsNullOrWhiteSpace(betreff) || string.IsNullOrWhiteSpace(text) || text.Length > 4000)
        {
            throw new ArgumentException("Bitte Betreff und Text (höchstens 4000 Zeichen) eingeben.");
        }

        var kurs = await programm.KursLadenAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        var v = kurs.Veranstaltungen.SingleOrDefault(x => x.Id == veranstaltungId) ?? throw new InvalidOperationException("Termin nicht gefunden.");
        var belegungen = (await anmeldungen.BelegungenDesKursesAsync(kursId, abbruch))
            .Where(b => b.Status == Belegungsstatus.Angemeldet && (!b.IstKontingent || b.Einbuchungen.ContainsKey(v.Id)))
            .ToList();
        int versandt = 0, ohneMail = 0;
        foreach (var b in belegungen)
        {
            var p = await personen.LadenAsync(b.PersonId, abbruch);
            if (p is null || p.IstAnonymisiert || p.EMail is not { Length: > 0 } mail || mail == "–")
            {
                ohneMail++;
                continue;
            }

            var name = p.AktuellerName(uhr.GetLocalNow());
            await post.KundenmailEinreihenAsync(mail, p.Anzeigename, $"{kurs.Titel}: {betreff.Trim()}",
                $"Guten Tag {name.Vorname} {name.Nachname},\n\n{text.Trim()}\n\nMit freundlichen Grüßen\n{wer.Name}", p.Sprache, p.Id, abbruch);
            versandt++;
        }

        await protokoll.ProtokollierenAsync("Kurs.TerminNachricht",
            $"{kurs.Kursnummer}, Termin {v.Nummer} am {v.Beginn:dd.MM.yyyy}: Nachricht „{betreff.Trim()}“ an {versandt} Teilnehmende per Mail{(ohneMail > 0 ? $", {ohneMail} ohne E-Mail-Adresse" : "")}",
            nameof(Kurs), kursId, abbruch: abbruch);
        await anmeldungen.SpeichernAsync(abbruch);
        return (versandt, ohneMail);
    }
}
