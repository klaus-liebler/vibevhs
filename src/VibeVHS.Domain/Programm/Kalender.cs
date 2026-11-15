using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Programm;

/// <summary>Schulferien (pflegbare Tabelle; Quelle: Schulministerium des Landes).</summary>
public sealed class Ferienzeitraum
{
    private Ferienzeitraum()
    {
        Name = string.Empty;
        Bundesland = "NRW";
    }

    public Ferienzeitraum(string name, DateOnly von, DateOnly bisEinschliesslich, string bundesland = "NRW", string? quelle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (bisEinschliesslich < von)
        {
            throw new ArgumentException("Das Ende der Ferien liegt vor dem Beginn.", nameof(bisEinschliesslich));
        }

        Id = Ids.Neu();
        Name = name.Trim();
        Von = von;
        Bis = bisEinschliesslich;
        Bundesland = bundesland;
        Quelle = string.IsNullOrWhiteSpace(quelle) ? null : quelle.Trim();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public DateOnly Von { get; private set; }

    /// <summary>Letzter Ferientag (einschließlich).</summary>
    public DateOnly Bis { get; private set; }

    public string Bundesland { get; private set; }

    /// <summary>Woher die Angabe stammt, z. B. die amtliche Ferienordnung.</summary>
    public string? Quelle { get; private set; }

    public bool Enthaelt(DateOnly tag) => Von <= tag && tag <= Bis;
}

/// <summary>Gesetzliche Feiertage in Nordrhein-Westfalen, eigene Berechnung (Osterformel nach Gauß/Lichtenberg).</summary>
public static class FeiertageNrw
{
    /// <summary>Ostersonntag im gregorianischen Kalender.</summary>
    public static DateOnly Ostersonntag(int jahr)
    {
        var a = jahr % 19;
        var b = jahr / 100;
        var c = jahr % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var monat = (h + l - (7 * m) + 114) / 31;
        var tag = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(jahr, monat, tag);
    }

    public static IReadOnlyList<(DateOnly Datum, string Name)> Fuer(int jahr)
    {
        var ostern = Ostersonntag(jahr);
        return
        [
            (new DateOnly(jahr, 1, 1), "Neujahr"),
            (ostern.AddDays(-2), "Karfreitag"),
            (ostern.AddDays(1), "Ostermontag"),
            (new DateOnly(jahr, 5, 1), "Tag der Arbeit"),
            (ostern.AddDays(39), "Christi Himmelfahrt"),
            (ostern.AddDays(50), "Pfingstmontag"),
            (ostern.AddDays(60), "Fronleichnam"),
            (new DateOnly(jahr, 10, 3), "Tag der Deutschen Einheit"),
            (new DateOnly(jahr, 11, 1), "Allerheiligen"),
            (new DateOnly(jahr, 12, 25), "1. Weihnachtstag"),
            (new DateOnly(jahr, 12, 26), "2. Weihnachtstag"),
        ];
    }

    public static string? Name(DateOnly tag) => Fuer(tag.Year).Where(f => f.Datum == tag).Select(f => f.Name).FirstOrDefault();
}

/// <summary>Kalender für die Planung: Feiertage NRW und Schulferien.</summary>
public sealed class Kalender(IEnumerable<Ferienzeitraum> ferien)
{
    private readonly IReadOnlyList<Ferienzeitraum> _ferien = ferien.ToList();

    public static Kalender OhneFerien { get; } = new([]);

    public string? Feiertag(DateOnly tag) => FeiertageNrw.Name(tag);

    public string? Ferien(DateOnly tag) => _ferien.FirstOrDefault(f => f.Enthaelt(tag))?.Name;
}
