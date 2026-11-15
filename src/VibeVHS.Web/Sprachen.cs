namespace VibeVHS.Web;

public sealed record Sprache(string Code, string Name);

/// <summary>Korrespondenzsprachen zur Auswahl. Für alle anderen hilft der Übersetzungslink.</summary>
public static class Sprachen
{
    public static IReadOnlyList<Sprache> Alle { get; } =
    [
        new("de", "Deutsch"),
        new("en", "Englisch"),
        new("tr", "Türkisch"),
        new("uk", "Ukrainisch"),
        new("ar", "Arabisch"),
        new("ru", "Russisch"),
        new("pl", "Polnisch"),
        new("es", "Spanisch"),
        new("fa", "Persisch"),
        new("ro", "Rumänisch"),
    ];

    public static string Name(string code) => Alle.FirstOrDefault(s => s.Code == code)?.Name ?? code;
}
