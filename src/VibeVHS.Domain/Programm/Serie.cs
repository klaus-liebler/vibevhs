namespace VibeVHS.Domain.Programm;

/// <summary>
/// Regel für regelmäßige Veranstaltungen ("Schnellweg": regelmäßig, gleicher Ort, gleiche Dozentin).
/// Begrenzt über <see cref="Ende"/> oder <see cref="Anzahl"/>; ohne beides unbefristet (rollierend).
/// </summary>
public sealed record Serienregel
{
    public Serienregel()
    {
    }

    /// <summary>Kopie (auch für <c>with</c>): eigene Listeneinträge, damit zwei Regeln nie dieselben gespeicherten Zeilen teilen.</summary>
    private Serienregel(Serienregel original)
    {
        Start = original.Start;
        Ende = original.Ende;
        Anzahl = original.Anzahl;
        Beginn = original.Beginn;
        DauerMinuten = original.DauerMinuten;
        IntervallWochen = original.IntervallWochen;
        FerienAuslassen = original.FerienAuslassen;
        DozentId = original.DozentId;
        _wochentage = Gemeinsam.Listenwerte.Von(original.Wochentage);
        _ortIds = Gemeinsam.Listenwerte.Von(original.OrtIds);
    }

    public DateOnly Start { get; init; }

    /// <summary>Letzter möglicher Tag (einschließlich).</summary>
    public DateOnly? Ende { get; init; }

    /// <summary>Anzahl stattfindender Termine (ausgelassene zählen nicht).</summary>
    public int? Anzahl { get; init; }

    public List<DayOfWeek> Wochentage
    {
        get => Gemeinsam.Listenwerte.Werte(_wochentage);
        init => _wochentage = Gemeinsam.Listenwerte.Von(value);
    }

    private List<Gemeinsam.Listenwert<DayOfWeek>> _wochentage = [];

    public TimeOnly Beginn { get; init; }

    public int DauerMinuten { get; init; }

    /// <summary>1 = jede Woche, 2 = alle zwei Wochen.</summary>
    public int IntervallWochen { get; init; } = 1;

    /// <summary>Pausiert die Serie in den Schulferien? Vorgabe aus der Kursart; viele Kurse laufen in den Ferien weiter.</summary>
    public bool FerienAuslassen { get; init; }

    public List<Guid> OrtIds
    {
        get => Gemeinsam.Listenwerte.Werte(_ortIds);
        init => _ortIds = Gemeinsam.Listenwerte.Von(value);
    }

    private List<Gemeinsam.Listenwert<Guid>> _ortIds = [];

    public Guid? DozentId { get; init; }

    public bool Unbefristet => Ende is null && Anzahl is null;

    public Serienregel Verschoben(int tage) => this with { Start = Start.AddDays(tage), Ende = Ende?.AddDays(tage) };

    public void Pruefen()
    {
        if (Wochentage.Count == 0)
        {
            throw new ArgumentException("Bitte mindestens einen Wochentag wählen.");
        }

        if (DauerMinuten is < 5 or > 24 * 60)
        {
            throw new ArgumentException("Die Dauer muss zwischen 5 Minuten und 24 Stunden liegen.");
        }

        if (IntervallWochen < 1)
        {
            throw new ArgumentException("Das Intervall muss mindestens eine Woche sein.");
        }

        if (Ende is { } e && e < Start)
        {
            throw new ArgumentException("Das Ende liegt vor dem Start.");
        }

        if (Anzahl is < 1)
        {
            throw new ArgumentException("Die Anzahl der Termine muss mindestens 1 sein.");
        }
    }
}

/// <summary>Ein erzeugter (oder wegen Ferien/Feiertag ausgelassener) Termin der Vorschau.</summary>
public sealed record Serientermin(DateOnly Datum, DateTime Beginn, DateTime Ende, string? AusgelassenWeil)
{
    public bool Findet => AusgelassenWeil is null;
}

public static class Seriengenerator
{
    private const int MaxWochen = 260;

    /// <summary>
    /// Termine nach Regel, inklusive ausgelassener Tage mit Grund. Unbefristete Serien laufen bis
    /// <paramref name="horizontBis"/> (einschließlich), begrenzte höchstens bis dahin, wenn angegeben.
    /// </summary>
    public static IReadOnlyList<Serientermin> Erzeugen(Serienregel regel, Kalender kalender, DateOnly? horizontBis = null)
    {
        ArgumentNullException.ThrowIfNull(regel);
        regel.Pruefen();
        if (regel.Unbefristet && horizontBis is null)
        {
            throw new ArgumentException("Unbefristete Serien brauchen einen Horizont.", nameof(horizontBis));
        }

        var ergebnis = new List<Serientermin>();
        var stattfindend = 0;
        var wochenbeginn = regel.Start.AddDays(-(((int)regel.Start.DayOfWeek + 6) % 7)); // Montag der Startwoche
        var tage = regel.Wochentage.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();

        for (var woche = 0; woche < MaxWochen; woche += regel.IntervallWochen)
        {
            foreach (var wt in tage)
            {
                var datum = wochenbeginn.AddDays((woche * 7) + (((int)wt + 6) % 7));
                if (datum < regel.Start)
                {
                    continue;
                }

                if ((regel.Ende is { } ende && datum > ende) || (horizontBis is { } h && datum > h)
                    || (regel.Anzahl is { } anzahl && stattfindend >= anzahl))
                {
                    return ergebnis;
                }

                var beginn = datum.ToDateTime(regel.Beginn);
                // Gesetzliche Feiertage werden immer ausgelassen.
                var grund = kalender.Feiertag(datum) is { } f ? $"Feiertag: {f}"
                    : regel.FerienAuslassen && kalender.Ferien(datum) is { } fe ? $"Ferien: {fe}"
                    : null;
                ergebnis.Add(new Serientermin(datum, beginn, beginn.AddMinutes(regel.DauerMinuten), grund));
                if (grund is null)
                {
                    stattfindend++;
                }
            }
        }

        return ergebnis;
    }
}
