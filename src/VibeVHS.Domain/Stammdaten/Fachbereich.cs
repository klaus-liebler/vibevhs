namespace VibeVHS.Domain.Stammdaten;

/// <summary>Programmbereich der VHS (z. B. "4 Alphabetisierung und Deutsch als Zweitsprache").</summary>
public sealed class Fachbereich
{
    private Fachbereich()
    {
        Name = string.Empty;
    }

    public Fachbereich(int nr, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nr, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Nr = nr;
        Name = name.Trim();
    }

    /// <summary>Fachbereichsnummer, zugleich Schlüssel.</summary>
    public int Nr { get; private set; }

    public string Name { get; private set; }

    public string Anzeige => $"FB {Nr} {Name}";

    public void Umbenennen(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }
}
