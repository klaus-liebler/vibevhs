namespace VibeVHS.Application.Sicherheit;

/// <summary>
/// Fachliche Rollen. Ein Benutzer kann mehrere haben; nach der Anmeldung wählt er die aktive Rolle.
/// </summary>
public enum Rolle
{
    Leitung,
    Verwaltungsleitung,
    Fachbereichsleitung,
    Teilnehmerservice,
    Buchhaltung,
    IntegrationskursTeam,
    Systemverwaltung,
}

public static class RollenExtensions
{
    public static string Anzeigename(this Rolle rolle) => rolle switch
    {
        Rolle.Leitung => "Leitung",
        Rolle.Verwaltungsleitung => "Verwaltungsleitung",
        Rolle.Fachbereichsleitung => "Fachbereichsleitung",
        Rolle.Teilnehmerservice => "Teilnehmerservice",
        Rolle.Buchhaltung => "Buchhaltung",
        Rolle.IntegrationskursTeam => "Integrationskurs-Team",
        Rolle.Systemverwaltung => "Systemverwaltung",
        _ => rolle.ToString(),
    };

    /// <summary>Liest eine Rolle aus ihrem Anzeigenamen ("Integrationskurs-Team") oder technischen Namen.</summary>
    public static bool TryParse(string? text, out Rolle rolle)
    {
        rolle = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var r in Enum.GetValues<Rolle>())
        {
            if (string.Equals(r.Anzeigename(), text.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                rolle = r;
                return true;
            }
        }

        return false;
    }
}
