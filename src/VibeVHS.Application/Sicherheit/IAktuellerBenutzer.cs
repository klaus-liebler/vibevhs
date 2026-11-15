namespace VibeVHS.Application.Sicherheit;

/// <summary>Wer handelt gerade? Für automatische Abläufe ist das <see cref="Benutzer.Automatik"/>.</summary>
public interface IAktuellerBenutzer
{
    ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default);
}

public sealed record Benutzer(string Kennung, string Name, Rolle? Rolle)
{
    public static Benutzer Automatik { get; } = new("automatik", "Automatik", null);

    public bool IstAutomatik => Kennung == Automatik.Kennung;

    public string Anzeige => Rolle is { } r ? $"{Name} ({r.Anzeigename()})" : Name;
}
