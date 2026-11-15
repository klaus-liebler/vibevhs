using System.Text.RegularExpressions;
using VibeVHS.Application.Protokoll;

namespace VibeVHS.Application.Sicherheit;

public sealed record KontoEingabe(string Login, string Name, string? Funktion, IReadOnlyList<Rolle> Rollen, IReadOnlyList<int> Fachbereiche);

/// <summary>
/// Benutzerverwaltung für den Testbetrieb: Konten der Mitarbeitenden mit Rollen und Fachbereichen anlegen, ändern, deaktivieren.
/// Solange es die kommunale Anmeldung (OIDC/SAML) nicht gibt, ersetzt sie das Pflegen der Startdaten-Datei. Änderungen wirken ab
/// der nächsten Anmeldung. Portal-Konten (Teilnehmende, Dozierende) entstehen über Shop und Portale und sind hier ausgenommen.
/// </summary>
public sealed partial class Benutzerverwaltung(IBenutzerkontenPflege speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer)
{
    public static readonly Rolle[] Rollen = [Rolle.Systemverwaltung, Rolle.Leitung];

    /// <summary>Rollen, die hier vergeben werden können (ohne Portalrollen).</summary>
    public static IReadOnlyList<Rolle> VergebbareRollen { get; } = Enum.GetValues<Rolle>().Where(r => !Benutzerkonto.PortalRollen.Contains(r)).ToList();

    public async Task<IReadOnlyList<Benutzerkonto>> KontenAsync(CancellationToken abbruch = default)
        => (await speicher.AlleMitInaktivenAsync(abbruch)).Where(k => !k.IstPortalkonto).OrderBy(k => !k.Aktiv).ThenBy(k => k.Name, StringComparer.CurrentCulture).ToList();

    public async Task<Benutzerkonto> AnlegenAsync(KontoEingabe e, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        await RechtAsync(abbruch);
        var login = (e.Login ?? "").Trim().ToLowerInvariant();
        if (!LoginMuster().IsMatch(login))
        {
            throw new ArgumentException("Login: 3–40 Zeichen, Kleinbuchstaben, Ziffern, Punkt, Bindestrich (z. B. vorname.nachname).");
        }

        if (await speicher.LoginVergebenAsync(login, abbruch))
        {
            throw new InvalidOperationException($"Das Login „{login}“ ist bereits vergeben.");
        }

        Pruefen(e.Rollen);
        var konto = new Benutzerkonto(login, e.Name, string.IsNullOrWhiteSpace(e.Funktion) ? null : e.Funktion.Trim(), e.Rollen, e.Fachbereiche, null);
        speicher.Hinzufuegen(konto);
        await protokoll.ProtokollierenAsync("Benutzer.Angelegt", $"Konto {login} angelegt: {Beschreibung(konto)}", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return konto;
    }

    public async Task AendernAsync(Guid id, KontoEingabe e, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(e);
        var wer = await RechtAsync(abbruch);
        var konto = await KontoAsync(id, abbruch);
        Pruefen(e.Rollen);
        if (konto.Login == wer.Kennung && konto.Rollen.Contains(Rolle.Systemverwaltung) && !e.Rollen.Contains(Rolle.Systemverwaltung))
        {
            throw new InvalidOperationException("Die eigene Rolle Systemverwaltung kann man sich nicht selbst entziehen.");
        }

        var vorher = Beschreibung(konto);
        konto.Aendern(e.Name, e.Funktion, e.Rollen, e.Fachbereiche);
        await LetzteSystemverwaltungPruefenAsync(abbruch);
        await protokoll.ProtokollierenAsync("Benutzer.Geaendert", $"Konto {konto.Login}: {vorher} → {Beschreibung(konto)} (wirkt ab der nächsten Anmeldung)", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task AktivSetzenAsync(Guid id, bool aktiv, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var konto = await KontoAsync(id, abbruch);
        if (!aktiv && konto.Login == wer.Kennung)
        {
            throw new InvalidOperationException("Das eigene Konto kann man nicht deaktivieren.");
        }

        if (aktiv)
        {
            konto.Aktivieren();
        }
        else
        {
            konto.Deaktivieren();
        }

        await LetzteSystemverwaltungPruefenAsync(abbruch);
        await protokoll.ProtokollierenAsync(aktiv ? "Benutzer.Aktiviert" : "Benutzer.Deaktiviert", $"Konto {konto.Login} {(aktiv ? "wieder aktiviert" : "deaktiviert")}", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    private static string Beschreibung(Benutzerkonto k)
        => $"{k.Name}; Rollen {string.Join(", ", k.Rollen.Select(r => r.Anzeigename()))}{(k.Fachbereiche.Count > 0 ? $"; FB {string.Join(", ", k.Fachbereiche)}" : "")}";

    private static void Pruefen(IReadOnlyList<Rolle> rollen)
    {
        if (rollen.Count == 0)
        {
            throw new ArgumentException("Bitte mindestens eine Rolle wählen.");
        }

        if (rollen.Any(r => Benutzerkonto.PortalRollen.Contains(r)))
        {
            throw new ArgumentException("Portalrollen (Teilnehmende, Dozierende) werden nicht hier vergeben.");
        }
    }

    private async Task LetzteSystemverwaltungPruefenAsync(CancellationToken abbruch)
    {
        if (!(await speicher.AlleMitInaktivenAsync(abbruch)).Any(k => k.Aktiv && k.Rollen.Contains(Rolle.Systemverwaltung)))
        {
            throw new InvalidOperationException("Es muss mindestens ein aktives Konto mit der Rolle Systemverwaltung bleiben.");
        }
    }

    private async Task<Benutzerkonto> KontoAsync(Guid id, CancellationToken abbruch)
    {
        var konto = await speicher.ZumAendernAsync(id, abbruch) ?? throw new InvalidOperationException("Konto nicht gefunden.");
        return konto.IstPortalkonto ? throw new InvalidOperationException("Portal-Konten werden über Shop und Portale verwaltet.") : konto;
    }

    private async Task<Benutzer> RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        return wer.Rolle is { } r && Rollen.Contains(r) ? wer : throw new InvalidOperationException("Benutzerkonten pflegen Systemverwaltung und Leitung.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{2,39}$")]
    private static partial Regex LoginMuster();
}
