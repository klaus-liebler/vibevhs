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
    TimeProvider uhr)
{
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
