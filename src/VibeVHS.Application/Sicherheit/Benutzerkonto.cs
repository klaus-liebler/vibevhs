using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Sicherheit;

/// <summary>
/// Benutzerkonto für die Anmeldung. Solange es die kommunale Anmeldung (OIDC/SAML) noch nicht gibt,
/// wählt man das Konto auf der Testanmeldeseite. Später kommen Rollen und Fachbereiche aus der Benutzerverwaltung.
/// </summary>
public sealed class Benutzerkonto
{
    /// <summary>Gespeicherter Name – nur bei Konten ohne Person (Funktionskonten); sonst <c>null</c>, der Name kommt aus der Person.</summary>
    private string? _name;

    /// <summary>Name der verknüpften Person, beim Laden gesetzt (keine Kopie in der Datenbank).</summary>
    private string? _personenname;

    private Benutzerkonto()
    {
        Login = string.Empty;
    }

    public Benutzerkonto(string login, string name, string? funktion, IEnumerable<Rolle> rollen, IEnumerable<int> fachbereiche,
        Guid? personId, string? hinweis = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = Ids.Neu();
        Login = login.Trim().ToLowerInvariant();
        PersonId = personId;
        NameSetzen(name);
        Funktion = funktion;
        Rollen = rollen.Distinct().ToList();
        Fachbereiche = fachbereiche.Distinct().Order().ToList();
        Hinweis = hinweis;
        if (Rollen.Count == 0)
        {
            throw new ArgumentException($"Das Konto {login} braucht mindestens eine Rolle.", nameof(rollen));
        }
    }

    public Guid Id { get; private set; }

    public string Login { get; private set; }

    /// <summary>Anzeigename: bei Konten mit Person deren heutiger Name, sonst der gespeicherte Name.</summary>
    public string Name => PersonId is null ? _name ?? string.Empty : _personenname ?? _name ?? string.Empty;

    public string? Funktion { get; private set; }

    /// <remarks>Gespeichert als eigene Tabelle (je Eintrag eine Zeile, siehe <see cref="VibeVHS.Domain.Gemeinsam.Listenwert{T}"/>).</remarks>
    public List<Rolle> Rollen
    {
        get => VibeVHS.Domain.Gemeinsam.Listenwerte.Werte(_rollen);
        private set => _rollen = VibeVHS.Domain.Gemeinsam.Listenwerte.Von(value);
    }

    private List<VibeVHS.Domain.Gemeinsam.Listenwert<Rolle>> _rollen = [];

    /// <remarks>Gespeichert als eigene Tabelle (je Eintrag eine Zeile, siehe <see cref="VibeVHS.Domain.Gemeinsam.Listenwert{T}"/>).</remarks>
    public List<int> Fachbereiche
    {
        get => VibeVHS.Domain.Gemeinsam.Listenwerte.Werte(_fachbereiche);
        private set => _fachbereiche = VibeVHS.Domain.Gemeinsam.Listenwerte.Von(value);
    }

    private List<VibeVHS.Domain.Gemeinsam.Listenwert<int>> _fachbereiche = [];

    /// <summary>Verknüpfte Person (Rolle Mitarbeitende).</summary>
    public Guid? PersonId { get; private set; }

    public string? Hinweis { get; private set; }

    public bool Aktiv { get; private set; } = true;

    /// <summary>Rollen der Portale; diese Konten entstehen über Shop- bzw. Portal-Anmeldung, nicht in der Benutzerverwaltung.</summary>
    public static readonly Rolle[] PortalRollen = [Rolle.Teilnehmende, Rolle.Dozierende];

    public bool IstPortalkonto => Rollen.All(r => PortalRollen.Contains(r));

    /// <summary>Login eines Portal-Kontos („doz-…“, „tn-…“) aus der vollen Personen-Id – eindeutig, auch bei vielen Personen aus einem Import.</summary>
    public static string PortalLogin(string praefix, Guid personId) => $"{praefix}-{personId:N}";

    /// <summary>Name, Funktion, Rollen und Fachbereiche ändern (Benutzerverwaltung). Wirkt ab der nächsten Anmeldung.</summary>
    /// <remarks>Bei Konten mit Person bleibt der Name der Person maßgeblich; <paramref name="name"/> wird dann nicht gespeichert.</remarks>
    public void Aendern(string name, string? funktion, IEnumerable<Rolle> rollen, IEnumerable<int> fachbereiche)
    {
        if (PersonId is null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
        }

        var neu = rollen.Distinct().ToList();
        if (neu.Count == 0)
        {
            throw new ArgumentException("Ein Konto braucht mindestens eine Rolle.", nameof(rollen));
        }

        NameSetzen(name);
        Funktion = string.IsNullOrWhiteSpace(funktion) ? null : funktion.Trim();
        Rollen = neu;
        Fachbereiche = fachbereiche.Distinct().Order().ToList();
    }

    /// <summary>Name der verknüpften Person zum heutigen Tag (vom Speicher beim Laden gesetzt, nicht gespeichert).</summary>
    public void PersonennameSetzen(string? name) => _personenname = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    private void NameSetzen(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // Mit Person führt deren Name; gespeichert wird nur bei Funktionskonten ohne Person.
        _name = PersonId is null ? name.Trim() : null;
        _personenname ??= PersonId is null ? null : name.Trim();
    }

    public void Deaktivieren() => Aktiv = false;

    public void Aktivieren() => Aktiv = true;

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

/// <summary>Speicher für die Benutzerverwaltung: auch inaktive Konten, verfolgt zum Ändern.</summary>
public interface IBenutzerkontenPflege
{
    Task<IReadOnlyList<Benutzerkonto>> AlleMitInaktivenAsync(CancellationToken abbruch = default);

    Task<Benutzerkonto?> ZumAendernAsync(Guid id, CancellationToken abbruch = default);

    Task<bool> LoginVergebenAsync(string login, CancellationToken abbruch = default);

    void Hinzufuegen(Benutzerkonto konto);

    Task SpeichernAsync(CancellationToken abbruch = default);
}
