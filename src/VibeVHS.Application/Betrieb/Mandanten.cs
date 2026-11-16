namespace VibeVHS.Application.Betrieb;

/// <summary>Eine Volkshochschule (Mandant). Je Mandant gibt es eine eigene Datenbank (siehe docs/mandanten.md).</summary>
/// <param name="Kennung">Kurzkennung, z. B. „vhs-egs“ (Teil von Pfad oder Hostname).</param>
/// <param name="Verbindung">SQLite-Verbindungszeichenfolge der Datenbank dieses Mandanten.</param>
/// <param name="Host">Optional: Hostname, unter dem der Mandant erreichbar ist (z. B. „vhs-egs.example.org“).</param>
public sealed record Mandant(string Kennung, string Verbindung, string? Host = null);

/// <summary>
/// Vorbereitung Mandantenfähigkeit (Modell „eine Datenbank je VHS“): Liste der Mandanten aus der Konfiguration und Auflösung aus
/// Hostname oder erstem Pfadsegment. Heute gibt es genau einen Mandanten („standard“, Verbindung aus ConnectionStrings:VibeVHS).
/// </summary>
public sealed class Mandanten
{
    private readonly Dictionary<string, Mandant> _nachKennung;

    public Mandanten(IEnumerable<Mandant> mandanten)
    {
        var liste = mandanten.ToList();
        if (liste.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Mandant ist nötig.", nameof(mandanten));
        }

        _nachKennung = liste.ToDictionary(m => m.Kennung, StringComparer.OrdinalIgnoreCase);
        Standard = liste[0];
    }

    /// <summary>Der Mandant, wenn nichts anderes aufgelöst wird (Einzelbetrieb).</summary>
    public Mandant Standard { get; }

    public IReadOnlyCollection<Mandant> Alle => _nachKennung.Values;

    public bool Mehrere => _nachKennung.Count > 1;

    /// <summary>Mandant aus Hostname (exakt) oder erstem Pfadsegment („/vhs-egs/…“); sonst der Standard-Mandant.</summary>
    public Mandant Aufloesen(string? host, string? pfad)
    {
        if (!Mehrere)
        {
            return Standard;
        }

        if (host is not null && _nachKennung.Values.FirstOrDefault(m => string.Equals(m.Host, host, StringComparison.OrdinalIgnoreCase)) is { } perHost)
        {
            return perHost;
        }

        var segment = (pfad ?? "").Trim('/').Split('/', 2)[0];
        return _nachKennung.GetValueOrDefault(segment) ?? Standard;
    }
}

/// <summary>Der Mandant der aktuellen Anfrage bzw. des aktuellen Hintergrundlaufs (scoped).</summary>
public sealed class Mandantenkontext(Mandanten mandanten)
{
    public Mandant Aktuell { get; private set; } = mandanten.Standard;

    public void Setzen(Mandant mandant) => Aktuell = mandant ?? throw new ArgumentNullException(nameof(mandant));
}
