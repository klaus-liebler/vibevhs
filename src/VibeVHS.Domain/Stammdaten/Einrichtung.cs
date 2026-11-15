namespace VibeVHS.Domain.Stammdaten;

/// <summary>
/// Die Volkshochschule selbst: Briefkopf-Stammdaten (Name, Kurzname, Orte, Bundesland).
/// Es gibt genau eine Einrichtung je Installation.
/// </summary>
public sealed class Einrichtung
{
    /// <summary>Fester Schlüssel des einzigen Datensatzes.</summary>
    public const int EinzigeId = 1;

    private Einrichtung()
    {
        Name = string.Empty;
        Kurzname = string.Empty;
        Bundesland = string.Empty;
    }

    public Einrichtung(string name, string kurzname, IEnumerable<string> orte, string bundesland)
    {
        Id = EinzigeId;
        Aktualisieren(name, kurzname, orte, bundesland);
        Name = name.Trim();
        Kurzname = kurzname.Trim();
        Bundesland = bundesland.Trim();
    }

    public int Id { get; private set; }

    public string Name { get; private set; }

    public string Kurzname { get; private set; }

    /// <summary>Standorte (z. B. Emsdetten, Greven, Saerbeck).</summary>
    public List<string> Orte { get; private set; } = [];

    public string Bundesland { get; private set; }

    /// <summary>"Emsdetten · Greven · Saerbeck (NRW)"</summary>
    public string OrteAnzeige => string.Join(" · ", Orte) + (Bundesland.Length > 0 ? $" ({Bundesland})" : string.Empty);

    public void Aktualisieren(string name, string kurzname, IEnumerable<string> orte, string bundesland)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(kurzname);
        ArgumentNullException.ThrowIfNull(orte);
        Name = name.Trim();
        Kurzname = kurzname.Trim();
        Orte = orte.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();
        Bundesland = bundesland?.Trim() ?? string.Empty;
    }
}
