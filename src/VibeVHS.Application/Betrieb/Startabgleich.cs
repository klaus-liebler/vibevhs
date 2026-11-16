namespace VibeVHS.Application.Betrieb;

/// <summary>
/// Abgleich direkt nach dem Serverstart: Termine, Merkmale, Qualifikationen, Anwesenheit, Finanzen, Integration und zuletzt
/// Aufgabenregeln mit Eskalation laufen einmal nacheinander. Bis dahin zeigt die Startseite den Stand vor dem Abgleich;
/// sie weist darauf hin und aktualisiert sich, sobald der Abgleich fertig ist (<see cref="Geaendert"/>).
/// </summary>
public sealed class Startabgleich
{
    /// <summary>Spätestens danach gilt der Abgleich als beendet (z. B. wenn kein Hintergrunddienst läuft, wie in Tests).</summary>
    public static readonly TimeSpan Hoechstdauer = TimeSpan.FromMinutes(5);

    private readonly TimeProvider uhr;
    private readonly DateTimeOffset erzeugt;
    private readonly List<Startschritt> schritte;
    private readonly Lock sperre = new();

    public Startabgleich(TimeProvider uhr, IEnumerable<(string Schluessel, string Anzeige)> schritte)
    {
        ArgumentNullException.ThrowIfNull(uhr);
        this.uhr = uhr;
        erzeugt = uhr.GetUtcNow();
        this.schritte = schritte.Select(s => new Startschritt(s.Schluessel, s.Anzeige)).ToList();
    }

    /// <summary>Wird ausgelöst, wenn ein Schritt beginnt oder endet (Aufruf auf einem Hintergrund-Thread).</summary>
    public event Action? Geaendert;

    public DateTimeOffset? Beginn { get; private set; }

    public DateTimeOffset? Ende { get; private set; }

    public IReadOnlyList<Startschritt> Schritte
    {
        get
        {
            lock (sperre)
            {
                return schritte.Select(s => s with { }).ToList();
            }
        }
    }

    public bool Laeuft => Ende is null && schritte.Count > 0 && uhr.GetUtcNow() - erzeugt < Hoechstdauer;

    public void Begonnen(string schluessel) => Setzen(schluessel, s => s with { Zustand = Schrittzustand.Laeuft, Beginn = uhr.GetUtcNow() });

    public void Beendet(string schluessel, bool erfolgreich)
        => Setzen(schluessel, s => s with { Zustand = erfolgreich ? Schrittzustand.Fertig : Schrittzustand.Fehler, Ende = uhr.GetUtcNow() });

    private void Setzen(string schluessel, Func<Startschritt, Startschritt> aenderung)
    {
        lock (sperre)
        {
            var i = schritte.FindIndex(s => s.Schluessel == schluessel);
            if (i < 0)
            {
                return;
            }

            schritte[i] = aenderung(schritte[i]);
            Beginn ??= uhr.GetUtcNow();
            if (schritte.All(s => s.Zustand is Schrittzustand.Fertig or Schrittzustand.Fehler))
            {
                Ende = uhr.GetUtcNow();
            }
        }

        Geaendert?.Invoke();
    }
}

public enum Schrittzustand
{
    Wartet,
    Laeuft,
    Fertig,
    Fehler,
}

public sealed record Startschritt(string Schluessel, string Anzeige, Schrittzustand Zustand = Schrittzustand.Wartet, DateTimeOffset? Beginn = null, DateTimeOffset? Ende = null);
