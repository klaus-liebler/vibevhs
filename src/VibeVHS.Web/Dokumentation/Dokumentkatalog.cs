using System.Collections.Concurrent;
using VibeVHS.Application.Dokumentation;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Web.Dokumentation;

/// <summary>Ein Dokument in der Anwendung (Datei unter Dokumentation/, beim Bauen aus docs/ kopiert).</summary>
/// <param name="Schluessel">Adresse und Kommentarschlüssel, z. B. „spezifikation/anforderungen“ → /dokumentation/spezifikation/anforderungen.</param>
/// <param name="Datei">Pfad relativ zu docs/ (z. B. „spezifikation/anforderungen.md“).</param>
/// <param name="Archiv">Archivierter Stand der früheren Claude Docs (17.11.2026), nicht mehr gepflegt; maßgeblich ist das Repo.</param>
/// <param name="FuerAlle">Hilfe: alle Verwaltungsrollen; sonst nur <see cref="KommentarDienst.Rollen"/>.</param>
public sealed record Dokument(string Schluessel, string Titel, string Gruppe, string Datei, string Beschreibung, string? Archiv = null, bool FuerAlle = false)
{
    public string Adresse => Schluessel == Dokumentkatalog.Hilfe ? "hilfe" : $"dokumentation/{Schluessel}";
}

/// <summary>Welche Dokumente die Anwendung zeigt, wer sie lesen darf, und das Laden (mit Zwischenspeicher je Dateistand).</summary>
public sealed class Dokumentkatalog
{
    public const string Hilfe = "hilfe";

    public const string GruppeSpezifikation = "Spezifikation";

    public const string GruppeTechnik = "Umsetzung, Betrieb und Technik";

    public static readonly IReadOnlyList<Dokument> Alle =
    [
        new(Hilfe, "Hilfe zur Bedienung", "Hilfe", "hilfe.md", "Grundprinzipien und Abläufe Schritt für Schritt", FuerAlle: true),
        new("spezifikation/anforderungen", "Anforderungsanalyse und Use Cases", GruppeSpezifikation, "spezifikation/anforderungen.md",
            "Akteure, Use Cases, funktionale und Querschnittsanforderungen, Schnittstellen, Entscheidungen", "https://claude.ai/artifact/5zHT9KccZeiUj1xS1BmKNG"),
        new("spezifikation/architektur", "Architektur und Fachkonzepte", GruppeSpezifikation, "spezifikation/architektur.md",
            "Aufbau, Datenhaltung, Historisierung, Datenschutz, Schnittstellen-Mocks", "https://claude.ai/artifact/XVL9kYS5XcAYwGsoDQ4CN3"),
        new("spezifikation/fahrplan", "Umsetzungsfahrplan", GruppeSpezifikation, "spezifikation/fahrplan.md",
            "Iterationen und Abnahmen", "https://claude.ai/artifact/FFRgE1BfKbphyCYxcVz4S9"),
        new("spezifikation/buchungs-shop-plugin", "Konzept Buchungs-Shop", GruppeSpezifikation, "spezifikation/buchungs-shop-plugin.md",
            "Webshop, Web-Komponente, TYPO3", "https://claude.ai/artifact/VsQFogXRz3dzQYZajLgoMm"),
        new("doku/ueberblick", "Gesamtüberblick je Iteration", GruppeTechnik, "README.md", "Was umgesetzt ist, Iteration für Iteration"),
        new("doku/schnittstellen", "Schnittstellen nach außen", GruppeTechnik, "schnittstellen.md", "Mail, Messaging, VHS-Systeme, Stadtverwaltung, Finanzen, BAMF – mit Stand"),
        new("doku/abnahme-a", "Abnahme-Drehbuch", GruppeTechnik, "abnahme-a.md", "Zwölf Aufgaben für die Abnahme mit der vhs (auch als geführte Touren in der Hilfe)"),
        new("doku/betrieb", "Betrieb", GruppeTechnik, "betrieb.md", "Sicherung, Wiederherstellung, Messungen"),
        new("doku/sicherheit", "Sicherheit", GruppeTechnik, "sicherheit.md", "OWASP-Checkliste und Schutzmaßnahmen"),
        new("doku/nichtfunktionale-anforderungen", "Nichtfunktionale Anforderungen", GruppeTechnik, "nichtfunktionale-anforderungen.md", "Umsetzung der NFA"),
        new("doku/webshop", "Webshop", GruppeTechnik, "webshop.md", "Buchungs-API, Web-Komponente, TYPO3"),
        new("doku/dozierendenportal", "Portale", GruppeTechnik, "dozierendenportal.md", "Dozierenden- und Teilnehmendenportal, Kurs-QR"),
        new("doku/datenmodell-einleitung", "Datenmodell – Einleitung", GruppeTechnik, "datenmodell-einleitung.md", "Grundbegriffe der Datenhaltung"),
        new("doku/datenmodell", "Datenmodell – Tabellen", GruppeTechnik, "datenmodell.md", "Alle Tabellen und Spalten (erzeugt)"),
        new("doku/mandanten", "Mandanten", GruppeTechnik, "mandanten.md", "Eine Datenbank je VHS – Überlegungen"),
    ];

    private readonly string _wurzel;
    private readonly ConcurrentDictionary<string, (DateTime Stand, IReadOnlyList<Abschnitt> Abschnitte)> _zwischenspeicher = new();

    public Dokumentkatalog(string? wurzel = null) => _wurzel = wurzel ?? Path.Combine(AppContext.BaseDirectory, "Dokumentation");

    public static Dokument? Finden(string schluessel) => Alle.FirstOrDefault(d => d.Schluessel == schluessel);

    public static Freigabe Lesbar(Dokument dokument, Rolle? rolle)
        => dokument.FuerAlle ? Freigabe.Ja : Freigabe.NurFuer(rolle, KommentarDienst.Rollen);

    /// <summary>Lädt das Dokument in Abschnitten; null, wenn die Datei fehlt. Prüft das Leserecht (auch hier, nicht nur in der Seite).</summary>
    public IReadOnlyList<Abschnitt>? Laden(Dokument dokument, Rolle? rolle)
    {
        var recht = Lesbar(dokument, rolle);
        if (!recht.Erlaubt)
        {
            throw new InvalidOperationException(recht.Begruendung);
        }

        var datei = Path.Combine(_wurzel, dokument.Datei.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(datei))
        {
            return null;
        }

        var stand = File.GetLastWriteTimeUtc(datei);
        if (_zwischenspeicher.TryGetValue(dokument.Schluessel, out var gespeichert) && gespeichert.Stand == stand)
        {
            return gespeichert.Abschnitte;
        }

        var abschnitte = MarkdownDarstellung.InAbschnitte(File.ReadAllText(datei), ziel => LinkZiel(dokument, ziel));
        _zwischenspeicher[dokument.Schluessel] = (stand, abschnitte);
        return abschnitte;
    }

    /// <summary>Relativer Link aus einem Dokument (z. B. „../betrieb.md#x“) → Seite in der Anwendung, falls das Ziel im Katalog steht.</summary>
    internal static string? LinkZiel(Dokument von, string ziel)
    {
        var (pfad, anker) = ziel.IndexOf('#') is var i and >= 0 ? (ziel[..i], ziel[i..]) : (ziel, string.Empty);
        if (pfad.Length == 0)
        {
            return anker.Length > 0 ? von.Adresse + anker : null;
        }

        var basis = Path.GetDirectoryName(von.Datei.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var aufgeloest = Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar + "docs", basis, pfad.Replace('/', Path.DirectorySeparatorChar)));
        var praefix = Path.GetFullPath(Path.DirectorySeparatorChar + "docs") + Path.DirectorySeparatorChar;
        if (!aufgeloest.StartsWith(praefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativ = aufgeloest[praefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
        return Alle.FirstOrDefault(d => string.Equals(d.Datei, relativ, StringComparison.OrdinalIgnoreCase)) is { } treffer
            ? treffer.Adresse + anker
            : null;
    }
}
