using System.Security.Claims;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Adapters.Mocks;

/// <summary>Ein Testkonto für die Mock-Anmeldung (ersetzt später die kommunale Anmeldung per OIDC/SAML).</summary>
public sealed record MockKonto(string Kennung, string Name, string Anrede, IReadOnlyList<Rolle> Rollen);

/// <summary>
/// Mock-Anmeldung für Entwicklung und Vorführung: Konto und Rolle werden ausgewählt, ein Passwort gibt es nicht.
/// Darf nur in der Entwicklungsumgebung aktiv sein.
/// </summary>
public static class MockAnmeldung
{
    public const string RollenClaim = ClaimTypes.Role;
    public const string KennungClaim = ClaimTypes.NameIdentifier;
    public const string AnredeClaim = "vibevhs:anrede";

    public static IReadOnlyList<MockKonto> Konten { get; } =
    [
        new("jkrueger", "Julia Krüger", "Frau Krüger", [Rolle.Teilnehmerservice, Rolle.Fachbereichsleitung]),
        new("moeztuerk", "Murat Öztürk", "Herr Öztürk", [Rolle.Teilnehmerservice]),
        new("slorenz", "Sabine Lorenz", "Frau Lorenz", [Rolle.Fachbereichsleitung]),
        new("tbaumann", "Thomas Baumann", "Herr Baumann", [Rolle.Buchhaltung]),
        new("nschaefer", "Nina Schäfer", "Frau Schäfer", [Rolle.IntegrationskursTeam]),
    ];

    public static MockKonto? Finden(string kennung) => Konten.FirstOrDefault(k => k.Kennung == kennung);

    /// <summary>Erzeugt die Identität mit genau einer aktiven Rolle.</summary>
    public static ClaimsPrincipal Anmelden(MockKonto konto, Rolle aktiveRolle, string authentifizierungsart)
    {
        ArgumentNullException.ThrowIfNull(konto);
        if (!konto.Rollen.Contains(aktiveRolle))
        {
            throw new InvalidOperationException($"{konto.Name} hat die Rolle {aktiveRolle.Anzeigename()} nicht.");
        }

        var identitaet = new ClaimsIdentity(
        [
            new Claim(KennungClaim, konto.Kennung),
            new Claim(ClaimTypes.Name, konto.Name),
            new Claim(AnredeClaim, konto.Anrede),
            new Claim(RollenClaim, aktiveRolle.ToString()),
        ], authentifizierungsart, ClaimTypes.Name, RollenClaim);
        return new ClaimsPrincipal(identitaet);
    }

    /// <summary>Liest Benutzer und aktive Rolle aus einer Identität.</summary>
    public static Benutzer? AlsBenutzer(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var kennung = principal.FindFirst(KennungClaim)?.Value ?? principal.Identity.Name ?? "unbekannt";
        var name = principal.Identity.Name ?? kennung;
        Rolle? rolle = Enum.TryParse<Rolle>(principal.FindFirst(RollenClaim)?.Value, out var r) ? r : null;
        return new Benutzer(kennung, name, rolle);
    }
}
