namespace VibeVHS.Application.Sicherheit;

/// <summary>Wer handelt gerade? Für automatische Abläufe ist das <see cref="Benutzer.Automatik"/>.</summary>
public interface IAktuellerBenutzer
{
    ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default);
}

/// <param name="Kennung">Anmeldename.</param>
/// <param name="Name">Anzeigename.</param>
/// <param name="Rolle">Aktive Rolle (eine von <see cref="VerfuegbareRollen"/>).</param>
/// <param name="VerfuegbareRollen">Alle Rollen des Benutzers; zwischen ihnen kann er wechseln.</param>
/// <param name="Fachbereiche">Nummern der Fachbereiche, für die der Benutzer zuständig ist.</param>
public sealed record Benutzer(
    string Kennung,
    string Name,
    Rolle? Rolle,
    IReadOnlyList<Rolle>? VerfuegbareRollen = null,
    IReadOnlyList<int>? Fachbereiche = null)
{
    public static Benutzer Automatik { get; } = new("automatik", "Automatik", null);

    public bool IstAutomatik => Kennung == Automatik.Kennung;

    public string Anzeige => Rolle is { } r ? $"{Name} ({r.Anzeigename()})" : Name;

    public IReadOnlyList<Rolle> Rollen => VerfuegbareRollen ?? (Rolle is { } r ? [r] : []);

    public IReadOnlyList<int> ZustaendigeFachbereiche => Fachbereiche ?? [];
}
