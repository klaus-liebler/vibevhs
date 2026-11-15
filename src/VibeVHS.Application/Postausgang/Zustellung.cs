using VibeVHS.Application.Protokoll;

namespace VibeVHS.Application.Postausgang;

/// <summary>
/// Arbeitet den Postausgang ab: wartende Nachrichten über den passenden Kanal zustellen,
/// Ergebnis an der Nachricht vermerken und protokollieren. Wird vom Hintergrundjob regelmäßig aufgerufen.
/// </summary>
public sealed class Zustellung(
    INachrichtenSpeicher speicher,
    IEnumerable<IZustellKanal> kanaele,
    IAuditProtokoll protokoll,
    TimeProvider uhr)
{
    public async Task<ZustellErgebnis> WartendeZustellenAsync(int maximal = 50, CancellationToken abbruch = default)
    {
        var wartend = await speicher.WartendeAsync(maximal, abbruch);
        int zugestellt = 0, fehler = 0;
        foreach (var nachricht in wartend)
        {
            var kanal = kanaele.FirstOrDefault(k => k.Kanal == nachricht.Kanal);
            try
            {
                if (kanal is null)
                {
                    throw new InvalidOperationException($"Kein Zustellkanal für {nachricht.Kanal} eingerichtet.");
                }

                var rueckmeldung = await kanal.ZustellenAsync(nachricht, abbruch);
                nachricht.AlsZugestelltMarkieren(uhr.GetUtcNow(), rueckmeldung);
                zugestellt++;
                await protokoll.ProtokollierenAsync("Postausgang.Zugestellt",
                    $"{Bezeichnung(nachricht)} zugestellt" +
                    (rueckmeldung is null ? string.Empty : $" ({rueckmeldung})"),
                    nameof(AusgehendeNachricht), nachricht.Id, abbruch: abbruch);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                nachricht.FehlversuchVermerken(ex.Message);
                fehler++;
                await protokoll.ProtokollierenAsync("Postausgang.Fehler",
                    $"{Bezeichnung(nachricht)} konnte nicht zugestellt werden (Versuch {nachricht.Versuche}): {ex.Message}",
                    nameof(AusgehendeNachricht), nachricht.Id, erfolgreich: false, abbruch: abbruch);
            }
        }

        if (wartend.Count > 0)
        {
            await speicher.SpeichernAsync(abbruch);
        }

        return new ZustellErgebnis(zugestellt, fehler);
    }

    /// <summary>Protokolltext ohne Klarnamen: Mails mit Pseudonym, Schnittstellen mit Name und Betreff.</summary>
    private static string Bezeichnung(AusgehendeNachricht n) => n.Kanal == Kanal.EMail
        ? $"Mail an {(n.PersonId is { } p ? Domain.Datenschutz.Pseudonym.Person(p) : "externe Adresse")}"
        : $"„{n.Betreff}“ an {n.Empfaenger}";
}

public readonly record struct ZustellErgebnis(int Zugestellt, int Fehler);
