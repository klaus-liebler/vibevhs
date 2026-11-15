using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Application.Oberflaeche;

/// <summary>
/// Ob eine Aktion oder ein Feld gerade erlaubt ist. Ist sie es nicht, gibt es immer eine
/// verständliche <see cref="Begruendung"/>, die die Oberfläche als Tooltip anzeigt.
/// </summary>
public sealed record Freigabe
{
    private Freigabe(bool erlaubt, string? begruendung)
    {
        Erlaubt = erlaubt;
        Begruendung = begruendung;
    }

    public bool Erlaubt { get; }

    public string? Begruendung { get; }

    public static Freigabe Ja { get; } = new(true, null);

    public static Freigabe Nein(string begruendung)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(begruendung);
        return new Freigabe(false, begruendung);
    }

    /// <summary>Nur für bestimmte Rollen erlaubt; die Begründung nennt die benötigte und die eigene Rolle.</summary>
    public static Freigabe NurFuer(Rolle? aktuelleRolle, params Rolle[] erlaubteRollen)
    {
        if (aktuelleRolle is { } r && erlaubteRollen.Contains(r))
        {
            return Ja;
        }

        var benoetigt = string.Join(" oder ", erlaubteRollen.Select(x => $"„{x.Anzeigename()}“"));
        var eigene = aktuelleRolle?.Anzeigename() ?? "keine";
        return Nein($"Nur für die Rolle {benoetigt}. Ihre Rolle: {eigene}.");
    }

    /// <summary>Funktion ist geplant, aber noch nicht umgesetzt.</summary>
    public static Freigabe Geplant(string funktion, string? iteration = null)
        => Nein(iteration is null
            ? $"{funktion} ist noch nicht umgesetzt und folgt in einer späteren Iteration (siehe Umsetzungsfahrplan)."
            : $"{funktion} folgt in Iteration {iteration} (siehe Umsetzungsfahrplan).");

    /// <summary>Verknüpft mehrere Bedingungen; die erste nicht erfüllte liefert die Begründung.</summary>
    public Freigabe Und(Func<Freigabe> weitere) => Erlaubt ? weitere() : this;
}
