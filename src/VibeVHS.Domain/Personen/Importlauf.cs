using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

/// <summary>Zuordnung einer Altnummer (z. B. KuferSQL-Teilnehmernummer) zur neu angelegten Person.</summary>
public sealed record Importzuordnung(string Altnummer, Guid PersonId);

/// <summary>Protokoll eines Datenimports (Altdaten): Datei, Prüfsumme, Ergebnis und Zuordnung der Altnummern.</summary>
public sealed class Importlauf
{
    private Importlauf()
    {
        Datei = string.Empty;
        Sha256 = string.Empty;
        Von = string.Empty;
    }

    /// <param name="art">„Personen“, „Kurse“ oder „Belegungen“; bei Kursen und Belegungen enthält <see cref="Importzuordnung.PersonId"/> die Kurs- bzw. Belegungs-ID.</param>
    public Importlauf(string datei, string sha256, int zeilen, int angelegt, int dubletten, int fehler, IEnumerable<Importzuordnung> zuordnungen, DateTimeOffset am, string von,
        string art = "Personen")
    {
        Art = art;
        Id = Ids.Neu();
        Datei = datei;
        Sha256 = sha256;
        Zeilen = zeilen;
        Angelegt = angelegt;
        Dubletten = dubletten;
        Fehler = fehler;
        Zuordnungen = zuordnungen.ToList();
        Am = am;
        Von = von;
    }

    public Guid Id { get; private set; }

    public string Datei { get; private set; }

    public string Sha256 { get; private set; }

    public int Zeilen { get; private set; }

    public int Angelegt { get; private set; }

    public int Dubletten { get; private set; }

    public int Fehler { get; private set; }

    public List<Importzuordnung> Zuordnungen { get; private set; } = [];

    public DateTimeOffset Am { get; private set; }

    public string Von { get; private set; }

    public string Art { get; private set; } = "Personen";
}
