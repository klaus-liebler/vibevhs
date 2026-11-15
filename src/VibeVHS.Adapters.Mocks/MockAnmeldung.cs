using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Adapters.Mocks;

/// <summary>Konfiguration der Testanmeldung (Abschnitt "Anmeldung" in appsettings).</summary>
public sealed class TestanmeldungOptionen
{
    public const string Abschnitt = "Anmeldung";

    /// <summary>
    /// Optionaler gemeinsamer Zugangscode auf der Anmeldeseite. Gehört NICHT ins Repository,
    /// sondern in eine Umgebungsvariable (Anmeldung__Zugangscode). Auf dem Testserver schützt
    /// stattdessen Basic Auth am Webserver; dann bleibt dieser Wert leer.
    /// </summary>
    public string? Zugangscode { get; set; }

    /// <summary>Ohne gesetzten Zugangscode ist dann keine Anmeldung möglich.</summary>
    public bool ZugangscodeErforderlich { get; set; }
}

/// <summary>Prüft den optionalen Zugangscode der Testanmeldung.</summary>
public sealed class Zugangsschutz(IOptions<TestanmeldungOptionen> optionen)
{
    public bool ZugangscodeNoetig => optionen.Value.ZugangscodeErforderlich || !string.IsNullOrEmpty(optionen.Value.Zugangscode);

    public bool AnmeldungEingerichtet => !optionen.Value.ZugangscodeErforderlich || !string.IsNullOrEmpty(optionen.Value.Zugangscode);

    public bool ZugangscodePruefen(string? eingabe)
    {
        if (!ZugangscodeNoetig)
        {
            return true;
        }

        var erwartet = optionen.Value.Zugangscode;
        if (string.IsNullOrEmpty(erwartet) || string.IsNullOrEmpty(eingabe))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(eingabe)),
            SHA256.HashData(Encoding.UTF8.GetBytes(erwartet)));
    }
}

/// <summary>Erzeugen, Auslesen und Rollenwechsel der Anmeldeidentität.</summary>
public static class MockAnmeldung
{
    public const string RollenClaim = ClaimTypes.Role;
    public const string KennungClaim = ClaimTypes.NameIdentifier;
    public const string AnredeClaim = "vibevhs:anrede";
    public const string VerfuegbareRolleClaim = "vibevhs:rolle-verfuegbar";
    public const string FachbereichClaim = "vibevhs:fachbereich";

    /// <summary>Erzeugt die Identität. Die aktive Rolle steht im Rollen-Claim, alle Rollen als "verfügbar".</summary>
    public static ClaimsPrincipal Anmelden(Benutzer benutzer, string authentifizierungsart)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        if (benutzer.Rolle is not { } aktiv || !benutzer.Rollen.Contains(aktiv))
        {
            throw new InvalidOperationException($"{benutzer.Name}: Die aktive Rolle muss eine der eigenen Rollen sein.");
        }

        var claims = new List<Claim>
        {
            new(KennungClaim, benutzer.Kennung),
            new(ClaimTypes.Name, benutzer.Name),
            new(AnredeClaim, benutzer.Name),
            new(RollenClaim, aktiv.ToString()),
        };
        claims.AddRange(benutzer.Rollen.Select(r => new Claim(VerfuegbareRolleClaim, r.ToString())));
        claims.AddRange(benutzer.ZustaendigeFachbereiche.Select(f => new Claim(FachbereichClaim, f.ToString(CultureInfo.InvariantCulture))));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authentifizierungsart, ClaimTypes.Name, RollenClaim));
    }

    /// <summary>Gleiche Identität mit anderer aktiver Rolle; <c>null</c>, wenn die Rolle nicht zum Benutzer gehört.</summary>
    public static ClaimsPrincipal? RolleWechseln(ClaimsPrincipal principal, Rolle neueRolle, string authentifizierungsart)
    {
        var benutzer = AlsBenutzer(principal);
        if (benutzer is null || !benutzer.Rollen.Contains(neueRolle))
        {
            return null;
        }

        return Anmelden(benutzer with { Rolle = neueRolle }, authentifizierungsart);
    }

    /// <summary>Liest Benutzer, aktive und verfügbare Rollen sowie Fachbereiche aus einer Identität.</summary>
    public static Benutzer? AlsBenutzer(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var kennung = principal.FindFirst(KennungClaim)?.Value ?? principal.Identity.Name ?? "unbekannt";
        var name = principal.Identity.Name ?? kennung;
        Rolle? rolle = Enum.TryParse<Rolle>(principal.FindFirst(RollenClaim)?.Value, out var r) ? r : null;
        var verfuegbar = principal.FindAll(VerfuegbareRolleClaim)
            .Select(c => Enum.TryParse<Rolle>(c.Value, out var v) ? v : (Rolle?)null)
            .OfType<Rolle>()
            .ToList();
        if (verfuegbar.Count == 0 && rolle is { } einzige)
        {
            verfuegbar.Add(einzige);
        }

        var fachbereiche = principal.FindAll(FachbereichClaim)
            .Select(c => int.TryParse(c.Value, CultureInfo.InvariantCulture, out var f) ? f : (int?)null)
            .OfType<int>()
            .ToList();
        return new Benutzer(kennung, name, rolle, verfuegbar, fachbereiche);
    }
}
