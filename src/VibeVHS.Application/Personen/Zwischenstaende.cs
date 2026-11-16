using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

/// <summary>Speicherzugriff für das Fortschreiben der Personen-Caches.</summary>
public interface IZwischenstandSpeicher
{
    /// <summary>Personen, bei denen eine Version (Name, Merkmal, Adresse, Adresskennzeichen, E-Mail) im Zeitraum (nach, bis] beginnt oder endet.</summary>
    Task<IReadOnlyList<Guid>> PersonenMitGrenzeAsync(DateOnly nach, DateOnly bis, CancellationToken abbruch = default);

    /// <summary>Personen zum Ändern (verfolgt).</summary>
    Task<IReadOnlyList<Person>> LadenAsync(IReadOnlyCollection<Guid> ids, CancellationToken abbruch = default);

    /// <summary>Tag des letzten Fortschreibens (aus dem Protokoll), sonst <c>null</c>.</summary>
    Task<DateOnly?> LetzterLaufAsync(CancellationToken abbruch = default);

    Task SpeichernAsync(CancellationToken abbruch = default);

    /// <summary>
    /// Namenskopien abgleichen (Lese-Caches): Lehrkraft-Namen in Kursteams und künftigen Terminen, Auftraggeber-Namen in Kursen,
    /// Kontonamen von Konten mit Person. Liefert die Zahl der geänderten Datensätze.
    /// </summary>
    Task<int> NamenskopienAbgleichenAsync(DateTimeOffset jetzt, CancellationToken abbruch = default);
}

public sealed record Zwischenstandlauf(int Geprueft, int Geaendert, DateOnly Bis, int Namenskopien = 0);

/// <summary>
/// Täglich (00:05 und im Startabgleich): Lese-Caches der Personentabelle fortschreiben. Die führenden Daten sind bitemporal; die Caches
/// werden beim Schreiben gepflegt – Änderungen mit Datum in der Zukunft werden aber erst an ihrem Tag wirksam. Geprüft werden nur Personen,
/// bei denen seit dem letzten Lauf eine Version begonnen oder geendet hat (nach einer Pause werden die verpassten Tage nachgeholt).
/// </summary>
public sealed class Zwischenstaende(IZwischenstandSpeicher speicher, IAuditProtokoll protokoll, TimeProvider uhr)
{
    public const string Aktion = "Personen.ZwischenstandFortgeschrieben";
    private const int Paket = 200;

    public async Task<Zwischenstandlauf> FortschreibenAsync(CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var letzter = await speicher.LetzterLaufAsync(abbruch);
        if (letzter == heute)
        {
            return new Zwischenstandlauf(0, 0, heute);
        }

        var ids = await speicher.PersonenMitGrenzeAsync(letzter ?? DateOnly.MinValue, heute, abbruch);
        var geaendert = 0;
        foreach (var paket in ids.Chunk(Paket))
        {
            foreach (var person in await speicher.LadenAsync(paket, abbruch))
            {
                if (person.ZwischenstandFortschreiben(jetzt))
                {
                    geaendert++;
                }
            }

            await speicher.SpeichernAsync(abbruch);
        }

        var kopien = await speicher.NamenskopienAbgleichenAsync(jetzt, abbruch);
        await protokoll.ProtokollierenAsync(Aktion,
            $"Zwischenstände der Personen fortgeschrieben (Stand {heute:dd.MM.yyyy}): {ids.Count} geprüft, {geaendert} aktualisiert, {kopien} Namenskopien nachgezogen",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new Zwischenstandlauf(ids.Count, geaendert, heute, kopien);
    }
}
