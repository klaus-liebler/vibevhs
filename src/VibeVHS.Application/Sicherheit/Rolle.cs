namespace VibeVHS.Application.Sicherheit;

/// <summary>Fachliche Rollen der Verwaltung. Die Anmeldung liefert die aktive Rolle als Claim.</summary>
public enum Rolle
{
    Teilnehmerservice,
    Fachbereichsleitung,
    Buchhaltung,
    IntegrationskursTeam,
}

public static class RollenExtensions
{
    public static string Anzeigename(this Rolle rolle) => rolle switch
    {
        Rolle.Teilnehmerservice => "Teilnehmerservice",
        Rolle.Fachbereichsleitung => "Fachbereichsleitung",
        Rolle.Buchhaltung => "Buchhaltung",
        Rolle.IntegrationskursTeam => "Integrationskurs-Team",
        _ => rolle.ToString(),
    };
}
