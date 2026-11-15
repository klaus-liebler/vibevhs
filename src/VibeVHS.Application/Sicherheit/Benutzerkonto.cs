using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Sicherheit;

/// <summary>
/// Benutzerkonto für die Anmeldung. Solange es die kommunale Anmeldung (OIDC/SAML) noch nicht gibt,
/// wählt man das Konto auf der Testanmeldeseite. Später kommen Rollen und Fachbereiche aus der Benutzerverwaltung.
/// </summary>
public sealed class Benutzerkonto
{
    private Benutzerkonto()
    {
        Login = string.Empty;
        Name = string.Empty;
    }

    public Benutzerkonto(string login, string name, string? funktion, IEnumerable<Rolle> rollen, IEnumerable<int> fachbereiche,
        Guid? personId, string? hinweis = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = Ids.Neu();
        Login = login.Trim().ToLowerInvariant();
        Name = name.Trim();
        Funktion = funktion;
        Rollen = rollen.Distinct().ToList();
        Fachbereiche = fachbereiche.Distinct().Order().ToList();
        PersonId = personId;
        Hinweis = hinweis;
        if (Rollen.Count == 0)
        {
            throw new ArgumentException($"Das Konto {login} braucht mindestens eine Rolle.", nameof(rollen));
        }
    }

    public Guid Id { get; private set; }

    public string Login { get; private set; }

    public string Name { get; private set; }

    public string? Funktion { get; private set; }

    public List<Rolle> Rollen { get; private set; } = [];

    public List<int> Fachbereiche { get; private set; } = [];

    /// <summary>Verknüpfte Person (Rolle Mitarbeitende).</summary>
    public Guid? PersonId { get; private set; }

    public string? Hinweis { get; private set; }

    public bool Aktiv { get; private set; } = true;

    /// <summary>Benutzer mit der ersten Rolle als aktiver Rolle (Wechsel danach oben rechts).</summary>
    public Benutzer AlsBenutzer(Rolle? aktiveRolle = null)
    {
        var rolle = aktiveRolle is { } r && Rollen.Contains(r) ? r : Rollen[0];
        return new Benutzer(Login, Name, rolle, Rollen, Fachbereiche);
    }
}

public interface IBenutzerkonten
{
    Task<IReadOnlyList<Benutzerkonto>> AlleAsync(CancellationToken abbruch = default);

    Task<Benutzerkonto?> FindenAsync(string login, CancellationToken abbruch = default);
}
