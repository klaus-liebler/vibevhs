namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Abfragen und Fortschreibungen für Listen bitemporaler Versionen.
/// </summary>
public static class ZeitlicheHistorie
{
    /// <summary>
    /// Stichtagsabfrage: Welche Version galt am <paramref name="gueltigAm"/>
    /// nach dem Wissensstand vom <paramref name="bekanntAm"/>?
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
            1 => treffer[0],
            _ => throw new InvalidOperationException(
                $"Inkonsistente Historie: mehrere Versionen gültig am {gueltigAm:dd.MM.yyyy}, bekannt am {bekanntAm:dd.MM.yyyy HH:mm}."),
        };
    }

    /// <summary>Alle Versionen des aktuellen Wissensstands, nach Gültigkeit sortiert.</summary>
    public static IReadOnlyList<T> AktuellerVerlauf<T>(this IEnumerable<T> versionen)
        where T : BitemporaleVersion<T>
        => versionen.Where(v => v.IstAktuellerWissensstand).OrderBy(v => v.GueltigVon).ToList();

    /// <summary>
    /// Änderung ab Datum: Der neue Wert gilt ab <c>neu.GueltigVon</c>. Was vorher galt, bleibt für die
    /// Vergangenheit unverändert (z. B. Heirat: bis zur Heirat hieß die Person anders).
    /// </summary>
    public static void AendernAb<T>(ICollection<T> versionen, T neu, DateTimeOffset jetzt)
        where T : BitemporaleVersion<T>
    {
        ArgumentNullException.ThrowIfNull(versionen);
        ArgumentNullException.ThrowIfNull(neu);
        var ab = neu.GueltigVon;

        foreach (var alt in versionen.Where(v => v.IstAktuellerWissensstand).ToList())
        {
            var endetVorher = alt.GueltigBis is { } bis && bis <= ab;
            if (endetVorher)
            {
                continue;
            }

            alt.Ersetzen(jetzt);
            if (alt.GueltigVon < ab)
            {
                versionen.Add(alt.KopieMit(alt.GueltigVon, ab, jetzt, $"Ende der Gültigkeit: {neu.Grund}"));
            }
        }

        versionen.Add(neu);
    }

    /// <summary>
    /// Korrektur: Der Wert war schon immer falsch erfasst (z. B. Tippfehler). Die Version, die am
    /// <paramref name="betroffenAm"/> gilt, wird für ihren gesamten Gültigkeitszeitraum ersetzt.
    /// Der alte Stand bleibt über <see cref="AlsStichtag{T}"/> mit früherem Wissensstand abrufbar.
    /// </summary>
    public static T Korrigieren<T>(ICollection<T> versionen, DateOnly betroffenAm, Func<DateOnly, DateOnly?, T> korrigiert, DateTimeOffset jetzt)
        where T : BitemporaleVersion<T>
    {
        ArgumentNullException.ThrowIfNull(versionen);
        ArgumentNullException.ThrowIfNull(korrigiert);
        var alt = versionen.AlsStichtag(betroffenAm, jetzt)
            ?? throw new InvalidOperationException($"Am {betroffenAm:dd.MM.yyyy} gilt kein Wert, der korrigiert werden könnte.");

        var neu = korrigiert(alt.GueltigVon, alt.GueltigBis);
        if (neu.GueltigVon != alt.GueltigVon || neu.GueltigBis != alt.GueltigBis)
        {
            throw new InvalidOperationException("Eine Korrektur muss denselben Gültigkeitszeitraum behalten. Für Änderungen ab einem Datum AendernAb verwenden.");
        }

        alt.Ersetzen(jetzt);
        versionen.Add(neu);
        return neu;
    }
}
