namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Ein Eintrag einer einfachen Werteliste (Fachbereichsnummer, Rolle, Merkmalsanforderung, Wochentag …). In der Datenbank eine Zeile je
/// Eintrag in einer eigenen Tabelle (1. Normalform) statt einer JSON-Spalte; die Fachklassen bieten weiterhin die einfache Liste an.
/// </summary>
public sealed record Listenwert<T>(T Wert);

/// <summary>Ein Schlüssel-Wert-Eintrag (z. B. Parameter eines Honorarvertrags, Angaben eines Portalanliegens), je Eintrag eine Zeile.</summary>
public sealed record Eintrag(string Schluessel, string Wert);

public static class Listenwerte
{
    public static List<Listenwert<T>> Von<T>(IEnumerable<T> werte) => werte.Select(w => new Listenwert<T>(w)).ToList();

    public static List<T> Werte<T>(IEnumerable<Listenwert<T>> liste) => liste.Select(w => w.Wert).ToList();

    public static List<Eintrag> Von(IReadOnlyDictionary<string, string> werte) => werte.Select(w => new Eintrag(w.Key, w.Value)).ToList();

    public static Dictionary<string, string> Woerterbuch(IEnumerable<Eintrag> liste) => liste.ToDictionary(e => e.Schluessel, e => e.Wert);
}
