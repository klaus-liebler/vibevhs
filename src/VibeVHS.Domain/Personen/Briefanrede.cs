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
/// Einheitliche Briefanrede für alle Mails und Briefe. Titel stehen vor dem Nachnamen („Frau Dr. Muster“), Namensvorsätze bleiben
/// beim Nachnamen („Herr von Berg“). Ohne Anrede (divers, keine Angabe) wird der volle Name genannt.
/// </summary>
public static class Briefanrede
{
    public static string Bilden(string? anrede, string? titel, string? vorname, string nachname, Anredestil stil = Anredestil.Foermlich)
    {
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
