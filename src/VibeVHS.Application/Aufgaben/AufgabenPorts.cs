using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Aufgaben;

namespace VibeVHS.Application.Aufgaben;

public interface IAufgabenSpeicher
{
    Task<IReadOnlyList<Aufgabentyp>> TypenAsync(CancellationToken abbruch = default);

    void Hinzufuegen(Aufgabentyp typ);

    void Hinzufuegen(Aufgabe aufgabe);

    /// <summary>Lädt zum Ändern.</summary>
    Task<Aufgabe?> LadenAsync(Guid id, CancellationToken abbruch = default);

    /// <summary>Alle Aufgaben eines Typs zum Ändern (für den Abgleich einer Regel).</summary>
    Task<IReadOnlyList<Aufgabe>> ZumTypAsync(string typ, CancellationToken abbruch = default);

    /// <summary>Offene Aufgaben; mit <paramref name="verfolgen"/> zum Ändern.</summary>
    Task<IReadOnlyList<Aufgabe>> OffeneAsync(bool verfolgen = false, CancellationToken abbruch = default);

    /// <summary>Seit dem Zeitpunkt erledigte oder automatisch geschlossene Aufgaben (nur lesen).</summary>
    Task<IReadOnlyList<Aufgabe>> AbgeschlossenSeitAsync(DateTimeOffset seit, CancellationToken abbruch = default);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Neuer oder weiter bestehender Anlass einer Regel.</summary>
public sealed record AufgabenAnlass(string Schluessel, string Titel, string? Beschreibung, DateOnly Faellig, string? Ziel,
    int? Fachbereich = null, Dringlichkeit Dringlichkeit = Dringlichkeit.Normal);

public sealed record Abgleichergebnis(int Neu, int Geschlossen, int Wiedereroeffnet)
{
    public static Abgleichergebnis Leer { get; } = new(0, 0, 0);

    public static Abgleichergebnis operator +(Abgleichergebnis a, Abgleichergebnis b) => new(a.Neu + b.Neu, a.Geschlossen + b.Geschlossen, a.Wiedereroeffnet + b.Wiedereroeffnet);

    public override string ToString() => $"{Neu} neu, {Geschlossen} automatisch geschlossen, {Wiedereroeffnet} wieder geöffnet";
}

/// <summary>Standard-Aufgabentypen. Zuständigkeit und Fristen sind je Typ einstellbar (Seite „Aufgabenregeln“).</summary>
public static class Aufgabentypen
{
    public const string Mindestteilnehmer = "mindestteilnehmer";
    public const string QualifikationAblauf = "qualifikation-ablauf";
    public const string QualifikationNachweis = "qualifikation-nachweis";
    public const string QualifikationEinsatz = "qualifikation-einsatz";
    public const string MerkmalAblauf = "merkmal-ablauf";
    public const string ZulassungAblauf = "zulassung-ablauf";
    public const string LoeschlaufFreigabe = "loeschlauf-freigabe";
    public const string PostausgangFehler = "postausgang-fehler";
    public const string LastschriftFreigabe = "lastschrift-freigabe";
    public const string MahnlaufFreigabe = "mahnlauf-freigabe";
    public const string HonorarFreigabe = "honorar-freigabe";
    public const string KontoauszugZuordnung = "kontoauszug-zuordnung";
    public const string Ruecklastschrift = "ruecklastschrift";

    public static IReadOnlyList<Aufgabentyp> Vorschlaege() =>
    [
        new(Mindestteilnehmer, "Mindestteilnehmerzahl nicht erreicht",
            "Prüft X Tage vor Kursbeginn die Anmeldungen und schlägt „absagen oder durchführen“ vor. Schließt sich selbst, sobald die Mindestzahl erreicht ist.",
            nameof(Rolle.Fachbereichsleitung), jeFachbereich: true, eskalationNachTagen: 1, vorlaufTage: 10, menschGibtFrei: true),
        new(QualifikationAblauf, "Qualifikation läuft ab", "Nachweis einer Kursleitung läuft bald ab.",
            nameof(Rolle.Fachbereichsleitung), jeFachbereich: false, eskalationNachTagen: 14),
        new(QualifikationNachweis, "Nachweis fehlt", "Für eine nachweispflichtige Qualifikation fehlt der Nachweis.",
            nameof(Rolle.Fachbereichsleitung), jeFachbereich: false, eskalationNachTagen: 14),
        new(QualifikationEinsatz, "Einsatz ohne Qualifikation", "Künftige Veranstaltungen mit einer Kursleitung ohne erforderliche Qualifikation.",
            nameof(Rolle.Fachbereichsleitung), jeFachbereich: true, eskalationNachTagen: 3),
        new(MerkmalAblauf, "Merkmal eines Orts läuft ab", "Befristetes Merkmal eines Kursorts (z. B. Brandschutzabnahme) läuft ab.",
            nameof(Rolle.Verwaltungsleitung), jeFachbereich: false, eskalationNachTagen: 7),
        new(ZulassungAblauf, "BAMF-Zulassung eines Orts läuft ab", "Zulassung eines Kursorts für Integrationskurse läuft ab.",
            nameof(Rolle.IntegrationskursTeam), jeFachbereich: false, eskalationNachTagen: 7),
        new(LoeschlaufFreigabe, "Löschlauf freigeben", "Vorbereiteter Löschlauf wartet auf zwei Freigaben (Vier-Augen-Prinzip).",
            nameof(Rolle.Verwaltungsleitung), jeFachbereich: false, eskalationNachTagen: 14),
        new(PostausgangFehler, "Fehlgeschlagene Nachrichten", "Mails oder Schnittstellenaufrufe sind nach mehreren Versuchen gescheitert.",
            nameof(Rolle.Systemverwaltung), jeFachbereich: false, eskalationNachTagen: 1),
        new(LastschriftFreigabe, "Lastschriftlauf freigeben", "Vorschlag für den SEPA-Einzug prüfen, freigeben (Vorabankündigung) und die Datei zur Bank geben.",
            nameof(Rolle.Buchhaltung), jeFachbereich: false, eskalationNachTagen: 2, menschGibtFrei: true),
        new(MahnlaufFreigabe, "Mahnvorschlag freigeben", "Fällige Mahnungen prüfen, einzelne ausschließen und freigeben.",
            nameof(Rolle.Buchhaltung), jeFachbereich: false, eskalationNachTagen: 3, menschGibtFrei: true),
        new(HonorarFreigabe, "Honorarabrechnungen freigeben", "Entwürfe der Honorarabrechnungen prüfen und festschreiben.",
            nameof(Rolle.Buchhaltung), jeFachbereich: false, eskalationNachTagen: 5),
        new(KontoauszugZuordnung, "Kontoauszug zuordnen", "Umsätze ohne sichere Zuordnung klären.",
            nameof(Rolle.Buchhaltung), jeFachbereich: false, eskalationNachTagen: 2),
        new(Ruecklastschrift, "Rücklastschrift klären", "Nach einer Rücklastschrift ist die Rechnung wieder offen: Kontakt aufnehmen, neues Mandat oder Überweisung.",
            nameof(Rolle.Buchhaltung), jeFachbereich: false, eskalationNachTagen: 7),
    ];
}

/// <summary>Was steht für die aktuelle Person an? Grundlage der Startseite.</summary>
public sealed record Aufgabenuebersicht(
    IReadOnlyList<Aufgabe> Meine,
    IReadOnlyList<Aufgabe> Gruppenkorb,
    IReadOnlyList<Aufgabe> BeiKollegen,
    IReadOnlyList<Aufgabe> Eskaliert,
    IReadOnlyList<Aufgabe> HeuteGeschlossen,
    IReadOnlyList<Protokoll.AuditEintrag> HeuteAutomatisch,
    IReadOnlyList<UeberfaelligeGruppe> UeberfaelligJeGruppe,
    string Korbname);

public sealed record UeberfaelligeGruppe(string Gruppe, int? Fachbereich, int Offen, int Ueberfaellig, int Eskaliert);

/// <summary>Mögliche Bearbeiter:in beim Zuweisen.</summary>
public sealed record Kollegin(string Login, string Name, string? Funktion);
