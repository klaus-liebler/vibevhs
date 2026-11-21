namespace VibeVHS.Domain.Personen;

/// <summary>Wie Mails und Briefe die Person ansprechen: förmlich (Standard) oder informell, je Vorlage einstellbar.</summary>
public enum Anredestil
{
    /// <summary>„Sehr geehrte Frau Dr. Muster“ / „Sehr geehrter Herr Prof. Dr. Muster“; ohne Anrede „Guten Tag Dr. Kim Muster“.</summary>
    Foermlich,

    /// <summary>„Guten Tag Frau Dr. Muster“; ohne Anrede „Guten Tag Dr. Kim Muster“.</summary>
    Informell,
}

/// <summary>
/// Persönliche Ansprache einer Person in Oberfläche und Mails: Du oder Sie, förmlich oder freundlich (Standard: Sie, förmlich).
/// Gespeichert als zeitlich versioniertes Merkmal (<see cref="MerkmalArt.Ansprache"/>) im Format „Sie;Foermlich“.
/// </summary>
public sealed record Ansprache(bool Du, Anredestil Stil)
{
    public static Ansprache Standard { get; } = new(false, Anredestil.Foermlich);

    public string Speicherwert => $"{(Du ? "Du" : "Sie")};{Stil}";

    public string Anzeige => (Du, Stil) switch
    {
        (false, Anredestil.Foermlich) => "Sie, förmlich („Sehr geehrte Frau Dr. Muster“)",
        (false, _) => "Sie, freundlich („Guten Tag Frau Dr. Muster“)",
        (true, Anredestil.Foermlich) => "Du, förmlich („Liebe Kim“)",
        (true, _) => "Du, freundlich („Hallo Kim“)",
    };

    /// <summary>Gespeicherten Wert lesen; leer oder unbekannt = keine persönliche Einstellung (<c>null</c>).</summary>
    public static Ansprache? Lesen(string? wert)
    {
        var teile = (wert ?? "").Split(';');
        return teile.Length == 2 && teile[0] is "Du" or "Sie" && Enum.TryParse<Anredestil>(teile[1], out var stil) ? new Ansprache(teile[0] == "Du", stil) : null;
    }

    /// <summary>Sie- oder Du-Fassung eines Oberflächentexts.</summary>
    public string Text(string sie, string du) => Du ? du : sie;
}

/// <summary>
/// Einheitliche Briefanrede für alle Mails und Briefe. Titel stehen vor dem Nachnamen („Frau Dr. Muster“), Namensvorsätze bleiben
/// beim Nachnamen („Herr von Berg“). Ohne Anrede (divers, keine Angabe) wird der volle Name genannt.
/// </summary>
public static class Briefanrede
{
    public static string Bilden(string? anrede, string? titel, string? vorname, string nachname, Anredestil stil = Anredestil.Foermlich, bool du = false)
    {
        if (du)
        {
            var rufname = string.IsNullOrWhiteSpace(vorname) ? nachname : vorname.Trim();
            return (anrede?.Trim(), stil) switch
            {
                ("Frau", Anredestil.Foermlich) => $"Liebe {rufname}",
                ("Herr", Anredestil.Foermlich) => $"Lieber {rufname}",
                _ => $"Hallo {rufname}",
            };
        }

        var t = string.IsNullOrWhiteSpace(titel) ? "" : $"{titel.Trim()} ";
        return (anrede?.Trim(), stil) switch
        {
            ("Frau", Anredestil.Foermlich) => $"Sehr geehrte Frau {t}{nachname}",
            ("Herr", Anredestil.Foermlich) => $"Sehr geehrter Herr {t}{nachname}",
            ("Frau" or "Herr", _) => $"Guten Tag {anrede!.Trim()} {t}{nachname}",
            _ => $"Guten Tag {t}{(string.IsNullOrWhiteSpace(vorname) ? "" : vorname.Trim() + " ")}{nachname}",
        };
    }
}
