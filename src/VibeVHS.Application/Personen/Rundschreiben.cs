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
    IPersonenSpeicher personen,
    PostausgangDienst postausgang,
    INachrichtenSpeicher speicher,
    IAuditProtokoll protokoll,
    TimeProvider uhr)
{
    public async Task<RundschreibenVorschau> VorschauAsync(PersonenFilter filter, Kontaktzweck zweck, CancellationToken abbruch = default)
    {
        int treffer = 0, mitEMail = 0, empfaenger = 0, ohne = 0;
        await foreach (var (zeile, person) in MitPersonenAsync(filter, abbruch))
        {
            treffer++;
            if (zeile.EMail is null)
            {
                continue;
            }

            mitEMail++;
            if (Darf(person, zweck))
            {
                empfaenger++;
            }
            else
            {
                ohne++;
            }
        }

        return new RundschreibenVorschau(treffer, mitEMail, empfaenger, ohne);
    }

    public async Task<RundschreibenErgebnis> SendenAsync(PersonenFilter filter, Kontaktzweck zweck, string betreff, string text,
        CancellationToken abbruch = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(betreff);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        int eingereiht = 0, uebersprungen = 0;
        await foreach (var (zeile, person) in MitPersonenAsync(filter, abbruch))
        {
            if (zeile.EMail is null || !Darf(person, zweck))
            {
                uebersprungen++;
                continue;
            }

            await postausgang.KundenmailEinreihenAsync(zeile.EMail, zeile.Anzeigename, betreff,
                $"{person.Briefanrede(uhr.GetLocalNow())},\n\n{text.Trim()}", person.Sprache, person.Id, abbruch);
            eingereiht++;
        }

        await protokoll.ProtokollierenAsync("Personen.Rundschreiben",
            $"Rundschreiben „{betreff}“ ({Einwilligung.ZweckAnzeige(zweck)}): {eingereiht} Mails in den Postausgang, {uebersprungen} übersprungen",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new RundschreibenErgebnis(eingereiht, uebersprungen);
    }

    private bool Darf(Person person, Kontaktzweck zweck)
    {
        if (zweck == Kontaktzweck.Kursorganisation)
        {
            return true;
        }

        var jetzt = uhr.GetLocalNow();
        return person.EinwilligungAm(Kontaktkanal.EMail, zweck, DateOnly.FromDateTime(jetzt.Date), jetzt);
    }

    private async IAsyncEnumerable<(PersonZeile Zeile, Person Person)> MitPersonenAsync(PersonenFilter filter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken abbruch)
    {
        var block = new List<PersonZeile>(200);
        await foreach (var zeile in abfrage.AlleAsync(filter, abbruch))
        {
            block.Add(zeile);
            if (block.Count == 200)
            {
                foreach (var paar in await LadenAsync(block, abbruch))
                {
                    yield return paar;
                }

                block.Clear();
            }
        }

        foreach (var paar in await LadenAsync(block, abbruch))
        {
            yield return paar;
        }
    }

    private async Task<IReadOnlyList<(PersonZeile, Person)>> LadenAsync(List<PersonZeile> block, CancellationToken abbruch)
    {
        if (block.Count == 0)
        {
            return [];
        }

        var geladen = (await personen.LadenAsync(block.Select(z => z.Id), abbruch)).ToDictionary(p => p.Id);
        return block.Where(z => geladen.ContainsKey(z.Id)).Select(z => (z, geladen[z.Id])).ToList();
    }
}
