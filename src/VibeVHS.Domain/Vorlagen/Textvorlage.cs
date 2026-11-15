using System.Text.RegularExpressions;

namespace VibeVHS.Domain.Vorlagen;

/// <summary>
/// Zentral pflegbare Vorlage für Mails und Briefe mit Platzhaltern in geschweiften Klammern, z. B. {Name} oder {Kurstitel}.
/// </summary>
public sealed partial class Textvorlage
{
    private Textvorlage()
    {
        Schluessel = string.Empty;
        Bezeichnung = string.Empty;
        Betreff = string.Empty;
        Text = string.Empty;
        GeaendertVon = string.Empty;
    }

    public Textvorlage(string schluessel, string bezeichnung, string betreff, string text, DateTimeOffset am, string von)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schluessel);
        Schluessel = schluessel;
        Bezeichnung = bezeichnung;
        Betreff = betreff;
        Text = text;
        GeaendertAm = am;
        GeaendertVon = von;
    }

    public string Schluessel { get; private set; }

    public string Bezeichnung { get; private set; }

    public string Betreff { get; private set; }

    public string Text { get; private set; }

    public DateTimeOffset GeaendertAm { get; private set; }

    public string GeaendertVon { get; private set; }

    public void Aendern(string betreff, string text, DateTimeOffset am, string von)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(betreff);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var unbekannt = Platzhalter(betreff + text).Except(Textvorlagen.BekanntePlatzhalter, StringComparer.Ordinal).ToList();
        if (unbekannt.Count > 0)
        {
            throw new ArgumentException($"Unbekannte Platzhalter: {string.Join(", ", unbekannt.Select(p => "{" + p + "}"))}. Erlaubt sind: {string.Join(", ", Textvorlagen.BekanntePlatzhalter.Select(p => "{" + p + "}"))}.");
        }

        Betreff = betreff.Trim();
        Text = text.Trim();
        GeaendertAm = am;
        GeaendertVon = von;
    }

    /// <summary>Setzt die Werte ein; unbekannte oder fehlende Platzhalter bleiben sichtbar stehen.</summary>
    public (string Betreff, string Text) Fuellen(IReadOnlyDictionary<string, string> werte)
        => (Ersetzen(Betreff, werte), Ersetzen(Text, werte));

    public static IReadOnlyList<string> Platzhalter(string text) => PlatzhalterMuster().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    private static string Ersetzen(string text, IReadOnlyDictionary<string, string> werte)
        => PlatzhalterMuster().Replace(text, m => werte.TryGetValue(m.Groups[1].Value, out var w) ? w : m.Value);

    [GeneratedRegex(@"\{([A-Za-zÄÖÜäöüß]+)\}")]
    private static partial Regex PlatzhalterMuster();
}

/// <summary>Mitgelieferte Vorlagen (Vorschläge, in der Oberfläche änderbar) und erlaubte Platzhalter.</summary>
public static class Textvorlagen
{
    public const string Anmeldebestaetigung = "anmeldung.bestaetigung";
    public const string Wartelisteneintrag = "anmeldung.warteliste";
    public const string Nachrueckangebot = "warteliste.angebot";
    public const string Stornobestaetigung = "storno.bestaetigung";
    public const string Umbuchungsbestaetigung = "umbuchung.bestaetigung";
    public const string Einbuchungsbestaetigung = "einbuchung.nachgerueckt";

    public static IReadOnlyList<string> BekanntePlatzhalter { get; } =
    [
        "Name", "Kursnummer", "Kurstitel", "Beginn", "Ort", "Termine", "Entgelt", "Zahlart", "Abmeldefrist", "Warteplatz", "Frist",
        "Gebuehr", "Regel", "NeuerKurs", "Veranstaltung", "Kontingent", "Einrichtung", "Checkinlink",
    ];

    public static IReadOnlyList<(string Schluessel, string Bezeichnung, string Betreff, string Text)> Vorschlaege { get; } =
    [
        (Anmeldebestaetigung, "Anmeldebestätigung", "Anmeldebestätigung: {Kursnummer} {Kurstitel}",
            "Guten Tag {Name},\n\nvielen Dank für Ihre Anmeldung zum Kurs „{Kurstitel}“ ({Kursnummer}).\n\nBeginn: {Beginn}\nOrt: {Ort}\nTermine: {Termine}\nEntgelt: {Entgelt} ({Zahlart})\n{Kontingent}\n\nAlle Termine finden Sie als Kalenderdatei im Anhang. Eine kostenlose Abmeldung ist bis {Abmeldefrist} möglich.\n\nPersönlicher Check-in: Öffnen Sie diesen Link einmal auf Ihrem Handy – danach genügt ein Scan des QR-Codes im Kursraum.\n{Checkinlink}\nIm Anhang finden Sie außerdem Ihren QR-Ausweis zum Vorzeigen bei der Kursleitung.\n\nMit freundlichen Grüßen\n{Einrichtung}"),
        (Wartelisteneintrag, "Eintrag auf die Warteliste", "Warteliste: {Kursnummer} {Kurstitel}",
            "Guten Tag {Name},\n\nder Kurs „{Kurstitel}“ ({Kursnummer}) ist leider ausgebucht. Wir haben Sie auf die Warteliste gesetzt (Platz {Warteplatz}).\n\nSobald ein Platz frei wird, erhalten Sie ein Angebot mit einer Frist zur Annahme.\n\nMit freundlichen Grüßen\n{Einrichtung}"),
        (Nachrueckangebot, "Nachrückangebot von der Warteliste", "Ein Platz ist frei: {Kursnummer} {Kurstitel}",
            "Guten Tag {Name},\n\nim Kurs „{Kurstitel}“ ({Kursnummer}) ist ein Platz frei geworden. Wir halten ihn für Sie bis {Frist} bereit.\n\nBeginn: {Beginn}\nEntgelt: {Entgelt}\n\nBitte melden Sie sich bis dahin am Tresen, telefonisch oder per Antwort auf diese Mail. Danach geht der Platz an die nächste Person auf der Warteliste.\n\nMit freundlichen Grüßen\n{Einrichtung}"),
        (Stornobestaetigung, "Abmeldebestätigung", "Abmeldung: {Kursnummer} {Kurstitel}",
            "Guten Tag {Name},\n\nwir haben Ihre Abmeldung vom Kurs „{Kurstitel}“ ({Kursnummer}) erhalten.\n\n{Regel}\nFällige Gebühr: {Gebuehr}\n\nMit freundlichen Grüßen\n{Einrichtung}"),
        (Umbuchungsbestaetigung, "Umbuchungsbestätigung", "Umbuchung auf {NeuerKurs}",
            "Guten Tag {Name},\n\nwir haben Sie vom Kurs {Kursnummer} auf den Kurs {NeuerKurs} umgebucht.\n\nBeginn: {Beginn}\nOrt: {Ort}\nEntgelt: {Entgelt}\nUmbuchungsgebühr: {Gebuehr}\n\nAlle Termine finden Sie als Kalenderdatei im Anhang.\n\nMit freundlichen Grüßen\n{Einrichtung}"),
        (Einbuchungsbestaetigung, "Nachgerückt in eine Veranstaltung", "Platz frei: {Kurstitel} am {Veranstaltung}",
            "Guten Tag {Name},\n\nfür {Kurstitel} am {Veranstaltung} ist ein Platz frei geworden. Wir haben Sie von der Warteliste eingebucht.\n\nStand Ihres Kontingents: {Kontingent}\n\nFalls Sie nicht kommen können, tragen Sie sich bitte rechtzeitig aus.\n\nMit freundlichen Grüßen\n{Einrichtung}"),
    ];

    /// <summary>Beispielwerte für die Vorschau.</summary>
    public static IReadOnlyDictionary<string, string> Beispielwerte { get; } = new Dictionary<string, string>
    {
        ["Name"] = "Olena Kovalenko",
        ["Kursnummer"] = "26H-501",
        ["Kurstitel"] = "Englisch A2 – Konversation",
        ["Beginn"] = "Di 29.09.2026, 18:00 Uhr",
        ["Ort"] = "vhs-Haus Emsdetten › Haus A › A 1.12 Kursraum",
        ["Termine"] = "12 Termine, dienstags 18:00–19:30 Uhr",
        ["Entgelt"] = "98,00 €",
        ["Zahlart"] = "SEPA-Lastschrift",
        ["Abmeldefrist"] = "22.09.2026",
        ["Warteplatz"] = "2",
        ["Frist"] = "18.11.2026, 23:59 Uhr",
        ["Gebuehr"] = "0,00 €",
        ["Regel"] = "Rechtzeitig (bis 22.09.2026, 7 Tage vor Beginn): kostenlos.",
        ["NeuerKurs"] = "26H-503 Niederländisch für die Grenzregion",
        ["Veranstaltung"] = "Mo 23.11.2026, 18:00 Uhr",
        ["Kontingent"] = "10er-Kontingent: 3 besucht, 1 eingebucht, 6 verbleibend",
        ["Einrichtung"] = "Volkshochschule Emsdetten Greven Saerbeck",
        ["Checkinlink"] = "https://vhs.example/checkin/geraet/…",
    };
}
