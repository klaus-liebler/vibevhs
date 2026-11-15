namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Abfragen und Fortschreibungen für Listen bitemporaler Versionen. Enthält eine Liste mehrere Gegenstände
/// (z. B. mehrere Adressen), grenzt <c>betrifft</c> die Operation auf einen Gegenstand ein.
/// </summary>
public static class ZeitlicheHistorie
{
    /// <summary>
    /// Stichtagsabfrage: Welche Version galt am <paramref name="gueltigAm"/>
    /// nach dem Wissensstand vom <paramref name="bekanntAm"/>? Endemarken zählen als "kein Wert".
    /// </summary>
    /// <returns>Die Version oder <c>null</c>, wenn zu diesem Stichtag nichts bekannt war.</returns>
    public static T? AlsStichtag<T>(this IEnumerable<T> versionen, DateOnly gueltigAm, DateTimeOffset bekanntAm)
        where T : BitemporaleVersion<T>
    {
        ArgumentNullException.ThrowIfNull(versionen);
        var treffer = versionen.Where(v => v.GiltAm(gueltigAm) && v.BekanntAm(bekanntAm)).Take(2).ToList();
        return treffer.Count switch
        {
            0 => null,
            1 => treffer[0].IstEndemarke ? null : treffer[0],
            _ => throw new InvalidOperationException(
                $"Inkonsistente Historie: mehrere Versionen gültig am {gueltigAm:dd.MM.yyyy}, bekannt am {bekanntAm:dd.MM.yyyy HH:mm}."),
        };
    }

    /// <summary>Stichtagsabfrage je Gegenstand (z. B. alle Adressen, die am Stichtag galten).</summary>
    public static IReadOnlyList<T> AlleAmStichtag<T>(this IEnumerable<T> versionen, DateOnly gueltigAm, DateTimeOffset bekanntAm)
        where T : BitemporaleVersion<T>
        => versionen.GroupBy(v => v.Gegenstand)
            .Select(g => g.AlsStichtag(gueltigAm, bekanntAm))
            .OfType<T>()
            .ToList();

    /// <summary>Alle Versionen des aktuellen Wissensstands, nach Gültigkeit sortiert.</summary>
    public static IReadOnlyList<T> AktuellerVerlauf<T>(this IEnumerable<T> versionen)
        where T : BitemporaleVersion<T>
        => versionen.Where(v => v.IstAktuellerWissensstand && !v.IstEndemarke).OrderBy(v => v.GueltigVon).ToList();

    /// <summary>
    /// Änderung ab Datum: Der neue Wert gilt ab <c>neu.GueltigVon</c>. Was vorher galt, bleibt für die
    /// Vergangenheit unverändert (z. B. Heirat: bis zur Heirat hieß die Person anders).
    /// Betroffen sind nur Versionen desselben Gegenstands wie <paramref name="neu"/>.
    /// </summary>
    public static void AendernAb<T>(ICollection<T> versionen, T neu)
        where T : BitemporaleVersion<T>
    {
        ArgumentNullException.ThrowIfNull(versionen);
        ArgumentNullException.ThrowIfNull(neu);
        AbgrenzenAb(versionen, neu.GueltigVon, neu.ErfasstAm, neu.ErfasstVon, neu.Grund, v => v.Gegenstand == neu.Gegenstand);
        versionen.Add(neu);
    }

    /// <summary>Beendet einen Gegenstand ab einem Datum; danach gilt für ihn kein Wert mehr.</summary>
    /// <param name="endemarke">Version mit <see cref="Erfassungsart.Beendet"/>, gültig ab dem Endedatum.</param>
    public static void BeendenAb<T>(ICollection<T> versionen, T endemarke)
        where T : BitemporaleVersion<T>
    {
        ArgumentNullException.ThrowIfNull(versionen);
        ArgumentNullException.ThrowIfNull(endemarke);
        if (!endemarke.IstEndemarke)
        {
            throw new ArgumentException("Zum Beenden wird eine Endemarke (Erfassungsart Beendet) erwartet.", nameof(endemarke));
        }

        var eigene = versionen.Where(v => v.Gegenstand == endemarke.Gegenstand).ToList();
        if (eigene.AlsStichtag(endemarke.GueltigVon.AddDays(-1), endemarke.ErfasstAm) is null
            && eigene.AlsStichtag(endemarke.GueltigVon, endemarke.ErfasstAm) is null)
        {
            throw new InvalidOperationException("Es gibt keinen gültigen Wert, der beendet werden könnte.");
        }

        AbgrenzenAb(versionen, endemarke.GueltigVon, endemarke.ErfasstAm, endemarke.ErfasstVon, endemarke.Grund,
            v => v.Gegenstand == endemarke.Gegenstand);
        versionen.Add(endemarke);
    }

    /// <summary>
    /// Korrektur: Der Wert war schon immer falsch erfasst (z. B. Tippfehler). Die Version desselben Gegenstands,
    /// die am <paramref name="betroffenAm"/> gilt, wird für ihren gesamten Gültigkeitszeitraum ersetzt.
    /// Der alte Stand bleibt über <see cref="AlsStichtag{T}"/> mit früherem Wissensstand abrufbar.
    /// </summary>
    public static T Korrigieren<T>(ICollection<T> versionen, DateOnly betroffenAm, Func<DateOnly, DateOnly?, T> korrigiert,
        DateTimeOffset jetzt, Func<T, bool>? betrifft = null)
        where T : BitemporaleVersion<T>
    {
        ArgumentNullException.ThrowIfNull(versionen);
        ArgumentNullException.ThrowIfNull(korrigiert);
        var alt = versionen.Where(v => betrifft?.Invoke(v) ?? true).AlsStichtag(betroffenAm, jetzt)
            ?? throw new InvalidOperationException($"Am {betroffenAm:dd.MM.yyyy} gilt kein Wert, der korrigiert werden könnte.");

        var neu = korrigiert(alt.GueltigVon, alt.GueltigBis);
        if (neu.GueltigVon != alt.GueltigVon || neu.GueltigBis != alt.GueltigBis)
        {
            throw new InvalidOperationException("Eine Korrektur muss denselben Gültigkeitszeitraum behalten. Für Änderungen ab einem Datum AendernAb verwenden.");
        }

        if (neu.Gegenstand != alt.Gegenstand)
        {
            throw new InvalidOperationException("Eine Korrektur muss denselben Gegenstand betreffen.");
        }

        alt.Ersetzen(jetzt);
        versionen.Add(neu);
        return neu;
    }

    private static void AbgrenzenAb<T>(ICollection<T> versionen, DateOnly ab, DateTimeOffset jetzt, string von, string grund,
        Func<T, bool> betrifft)
        where T : BitemporaleVersion<T>
    {
        foreach (var alt in versionen.Where(v => v.IstAktuellerWissensstand && betrifft(v)).ToList())
        {
            var endetVorher = alt.GueltigBis is { } bis && bis <= ab;
            if (endetVorher)
            {
                continue;
            }

            alt.Ersetzen(jetzt);
            if (alt.GueltigVon < ab)
            {
                versionen.Add(alt.KopieMit(alt.GueltigVon, ab,
                    new Erfassung(jetzt, von, alt.IstEndemarke ? Erfassungsart.Beendet : Erfassungsart.Abgrenzung,
                        alt.IstEndemarke ? alt.Grund : $"Ende der Gültigkeit: {grund}")));
            }
        }
    }
}
