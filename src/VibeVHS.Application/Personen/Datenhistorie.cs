using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

/// <summary>Ein Eintrag der Zeitleiste: eine Änderung an einem Feld.</summary>
public sealed record HistorienEintrag(
    string Datenart,
    string Feld,
    string? AlterWert,
    string NeuerWert,
    DateOnly GueltigVon,
    DateOnly? GueltigBis,
    DateTimeOffset ErfasstAm,
    string ErfasstVon,
    Erfassungsart Art,
    string Grund,
    DateTimeOffset? ErsetztAm)
{
    /// <summary>Durch eine spätere Erfassung ersetzt (z. B. Korrektur); wird blass angezeigt.</summary>
    public bool Ersetzt => ErsetztAm is not null;

    public string ArtAnzeige => Erfassung.Anzeigename(Art);
}

/// <summary>
/// Baut aus allen Versionen aller Stammdaten einer Person eine chronologische Zeitleiste (neueste Erfassung oben).
/// Technische Abgrenzungen (verkürzte Kopien nach einer Änderung ab Datum) werden weggelassen.
/// </summary>
public static class Datenhistorie
{
    /// <summary>Reihenfolge der Filter-Chips.</summary>
    public static IReadOnlyList<string> Datenarten { get; } = ["Name", "Geburtsdatum", "Adressen", "Telefon", "E-Mail", "Rollen", "Sprache", "Organisationen", "Vollmachten", "Einwilligungen"];

    public static IReadOnlyList<HistorienEintrag> Erstellen(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        var alle = person.AlleVersionen.ToList();
        return alle
            .Where(v => v.Erfassungsart != Erfassungsart.Abgrenzung)
            .Select((v, i) => (v, i))
            .OrderByDescending(x => x.v.ErfasstAm)
            .ThenBy(x => Rang(x.v.Datenart))
            .ThenBy(x => x.i)
            .Select(x =>
            {
                var ende = Fortsetzung(x.v, alle);
                return new HistorienEintrag(x.v.Datenart, x.v.Feld, AlterWert(x.v, alle), x.v.Wert, x.v.GueltigVon, ende.GueltigBis,
                    x.v.ErfasstAm, x.v.ErfasstVon, x.v.Erfassungsart, x.v.Grund, ende.ErsetztAm);
            })
            .ToList();
    }

    /// <summary>
    /// Wurde eine Version nur technisch abgegrenzt (gleicher Wert, kürzere Gültigkeit), gilt sie fachlich weiter –
    /// nur eben bis zum neuen Ende. Erst eine Korrektur ersetzt sie wirklich.
    /// </summary>
    private static IHistorienwert Fortsetzung(IHistorienwert v, List<IHistorienwert> alle)
    {
        var aktuell = v;
        while (aktuell.ErsetztAm is { } ersetzt
               && alle.FirstOrDefault(x => x.Erfassungsart is Erfassungsart.Abgrenzung && x.GetType() == aktuell.GetType()
                                           && x.Gegenstand == aktuell.Gegenstand && x.ErfasstAm == ersetzt
                                           && x.GueltigVon == aktuell.GueltigVon && x.Wert == aktuell.Wert) is { } weiter)
        {
            aktuell = weiter;
        }

        return aktuell;
    }

    private static int Rang(string datenart)
    {
        var i = Datenarten.ToList().IndexOf(datenart);
        return i < 0 ? int.MaxValue : i;
    }

    /// <summary>Was galt für denselben Gegenstand unmittelbar vor dieser Erfassung?</summary>
    private static string? AlterWert(IHistorienwert v, List<IHistorienwert> alle)
    {
        if (v.Erfassungsart == Erfassungsart.Ersterfassung)
        {
            return null;
        }

        var kurzVorher = v.ErfasstAm.AddTicks(-1);
        var tag = v.Erfassungsart == Erfassungsart.Korrektur || v.GueltigVon == DateOnly.MinValue
            ? v.GueltigVon
            : v.GueltigVon.AddDays(-1);
        return alle.FirstOrDefault(x => x.GetType() == v.GetType() && x.Gegenstand == v.Gegenstand && !x.IstEndemarke
                                        && x.GiltAm(tag) && x.BekanntAm(kurzVorher))?.Wert;
    }
}
