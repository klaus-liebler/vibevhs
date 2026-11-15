using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Stammdaten;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Startdaten;

/// <summary>
/// Lädt die Startdaten einer VHS (Briefkopf, Fachbereiche, Benutzerkonten mit verknüpfter Person) aus einer JSON-Datei.
/// Idempotent: Vorhandene Datensätze werden nicht doppelt angelegt; ein zweiter Lauf ändert nichts.
/// </summary>
public sealed class StartdatenLader(VibeVhsDbContext db, IAuditProtokoll protokoll, TimeProvider uhr, ILogger<StartdatenLader> log)
{
    private static readonly JsonSerializerOptions JsonOptionen = new() { PropertyNameCaseInsensitive = true };

    public async Task<StartdatenErgebnis> AusDateiLadenAsync(string datei, CancellationToken abbruch = default)
    {
        await using var strom = File.OpenRead(datei);
        var daten = await JsonSerializer.DeserializeAsync<StartdatenDatei>(strom, JsonOptionen, abbruch)
            ?? throw new InvalidDataException($"Startdaten {datei} sind leer.");
        return await LadenAsync(daten, abbruch);
    }

    public async Task<StartdatenErgebnis> LadenAsync(StartdatenDatei daten, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(daten);
        int neueFachbereiche = 0, neueKonten = 0, neuePersonen = 0;
        var einrichtungNeu = false;

        if (daten.Vhs is { } vhs && !await db.Einrichtungen.AnyAsync(abbruch))
        {
            db.Einrichtungen.Add(new Einrichtung(vhs.Name, vhs.Kurz, vhs.Orte, vhs.Bundesland ?? string.Empty));
            einrichtungNeu = true;
        }

        var vorhandeneFb = await db.Fachbereiche.Select(f => f.Nr).ToListAsync(abbruch);
        foreach (var fb in daten.Fachbereiche.Where(f => !vorhandeneFb.Contains(f.Nr)))
        {
            db.Fachbereiche.Add(new Fachbereich(fb.Nr, fb.Name));
            neueFachbereiche++;
        }

        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var vorhandeneLogins = await db.Benutzerkonten.Select(k => k.Login).ToListAsync(abbruch);
        foreach (var b in daten.Benutzer)
        {
            var login = b.Login.Trim().ToLowerInvariant();
            if (vorhandeneLogins.Contains(login))
            {
                continue;
            }

            var rollen = new List<Rolle>();
            foreach (var text in b.Rollen)
            {
                if (!RollenExtensions.TryParse(text, out var rolle))
                {
                    throw new InvalidDataException($"Unbekannte Rolle „{text}“ bei {b.Login}.");
                }

                rollen.Add(rolle);
            }

            var person = await db.Personen.FirstOrDefaultAsync(p => p.Benutzerkennung == login, abbruch);
            if (person is null)
            {
                var (titel, vorname, nachname) = NameZerlegen(b.Name);
                person = Person.Anlegen(vorname, nachname, heute, jetzt);
                person.TitelSetzen(titel);
                person.AlsMitarbeitendeVerknuepfen(login);
                db.Personen.Add(person);
                neuePersonen++;
            }

            db.Benutzerkonten.Add(new Benutzerkonto(login, b.Name, b.Funktion, rollen, b.Fachbereiche, person.Id, b.Hinweis));
            neueKonten++;
        }

        var ergebnis = new StartdatenErgebnis(einrichtungNeu, neueFachbereiche, neueKonten, neuePersonen);
        if (ergebnis.EtwasGeaendert)
        {
            await protokoll.ProtokollierenAsync("Startdaten.Geladen",
                $"Startdaten geladen ({daten.Vhs?.Kurz ?? "VHS"}): {neueFachbereiche} Fachbereiche, {neueKonten} Benutzerkonten, {neuePersonen} Mitarbeitende"
                + (einrichtungNeu ? ", Briefkopf" : string.Empty), abbruch: abbruch);
            await db.SaveChangesAsync(abbruch);
            log.LogInformation("Startdaten geladen: {Ergebnis}", ergebnis);
        }

        return ergebnis;
    }

    /// <summary>"Dr. Kai Lüken" → ("Dr.", "Kai", "Lüken"). Der Nachname ist das letzte Wort.</summary>
    public static (string? Titel, string Vorname, string Nachname) NameZerlegen(string name)
    {
        var teile = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var titel = new List<string>();
        while (teile.Count > 2 && teile[0].EndsWith('.'))
        {
            titel.Add(teile[0]);
            teile.RemoveAt(0);
        }

        if (teile.Count < 2)
        {
            throw new InvalidDataException($"Name „{name}“ braucht Vor- und Nachnamen.");
        }

        return (titel.Count > 0 ? string.Join(' ', titel) : null, string.Join(' ', teile[..^1]), teile[^1]);
    }
}

public readonly record struct StartdatenErgebnis(bool EinrichtungAngelegt, int Fachbereiche, int Benutzerkonten, int Personen)
{
    public bool EtwasGeaendert => EinrichtungAngelegt || Fachbereiche + Benutzerkonten + Personen > 0;
}

public sealed class StartdatenDatei
{
    public string? Quelle { get; set; }

    public VhsEintrag? Vhs { get; set; }

    public List<FachbereichEintrag> Fachbereiche { get; set; } = [];

    public List<string> Rollen { get; set; } = [];

    public List<BenutzerEintrag> Benutzer { get; set; } = [];

    public List<string> Hinweise { get; set; } = [];
}

public sealed class VhsEintrag
{
    public string Name { get; set; } = string.Empty;

    public string Kurz { get; set; } = string.Empty;

    public List<string> Orte { get; set; } = [];

    public string? Bundesland { get; set; }
}

public sealed class FachbereichEintrag
{
    public int Nr { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class BenutzerEintrag
{
    public string Login { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Funktion { get; set; }

    public List<string> Rollen { get; set; } = [];

    public List<int> Fachbereiche { get; set; } = [];

    public string? Hinweis { get; set; }
}
