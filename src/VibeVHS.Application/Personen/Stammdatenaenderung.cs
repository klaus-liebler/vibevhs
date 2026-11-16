using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

/// <summary>Alle Stammdaten aus dem Dialog „Stammdaten ändern“ (leer = keine Angabe).</summary>
public sealed record StammdatenEingabe(
    string? Anrede,
    string? Titel,
    string Vorname,
    string Nachname,
    string? Geburtsname,
    DateOnly? Geburtsdatum,
    string? Geschlecht,
    string? Sprache,
    string? AdressZusatz,
    string? Strasse,
    string? Postleitzahl,
    string? Ort,
    string? Telefon,
    string? EMail)
{
    /// <summary>Die Anschrift, die der Dialog zeigt und ändert: üblicher Aufenthaltsort, sonst Briefanschrift, sonst die erste.</summary>
    public static Postadresse? Hauptanschrift(PersonenStand s) => s.Aufenthaltsort ?? s.BriefAn ?? s.Adressen.FirstOrDefault();

    public static StammdatenEingabe Aus(PersonenStand s) => new(
        s.Anrede, s.Titel, s.Name?.Vorname ?? "", s.Name?.Nachname ?? "", s.Geburtsname, s.Geburtsdatum, s.Geschlecht, s.Sprache,
        Hauptanschrift(s)?.Zusatz, Hauptanschrift(s)?.Strasse, Hauptanschrift(s)?.Postleitzahl, Hauptanschrift(s)?.Ort,
        (s.Rufnummern.FirstOrDefault(r => r.Bevorzugt) ?? s.Rufnummern.FirstOrDefault())?.Nummer,
        (s.EMails.FirstOrDefault(e => e.Bevorzugt) ?? s.EMails.FirstOrDefault())?.Adresse);
}

/// <param name="Geaendert">Bezeichnungen der geänderten Felder (ohne Werte, für Meldung und Protokoll).</param>
public sealed record StammdatenErgebnis(IReadOnlyList<string> Geaendert, bool NameGeaendert);

/// <summary>
/// „Stammdaten ändern“: übernimmt nur die tatsächlich geänderten Felder, je nach Wahl als Korrektur (war schon immer so) oder als
/// Änderung ab Datum (z. B. Heirat, Umzug) – mit Grund/Nachweis in der Datenhistorie. Das Geburtsdatum ist immer eine Korrektur.
/// </summary>
public sealed class Stammdatenaenderung(IPersonenSpeicher speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    public static readonly IReadOnlyList<string> Anreden = ["Frau", "Herr", "Divers", "Keine Anrede"];

    public static readonly IReadOnlyList<string> Geschlechter = ["weiblich", "männlich", "divers", "ohne Angabe"];

    public async Task<StammdatenErgebnis> AendernAsync(Guid personId, StammdatenEingabe neu, bool korrektur, DateOnly datum, string grund,
        CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(neu);
        if (string.IsNullOrWhiteSpace(grund))
        {
            throw new ArgumentException("Bitte einen Grund bzw. Nachweis angeben (steht in der Datenhistorie).");
        }

        if (string.IsNullOrWhiteSpace(neu.Vorname) || string.IsNullOrWhiteSpace(neu.Nachname))
        {
            throw new ArgumentException("Vor- und Nachname sind Pflicht.");
        }

        var person = await speicher.LadenAsync(personId, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
        if (person.Datenschutz != Datenschutzstatus.Aktiv)
        {
            throw new InvalidOperationException("Die Person ist gesperrt oder anonymisiert; Stammdaten sind schreibgeschützt.");
        }

        var jetzt = uhr.GetLocalNow();
        var von = (await benutzer.ErmittelnAsync(abbruch)).Anzeige;
        var g = grund.Trim();
        var e = korrektur ? Erfassung.Korrektur(jetzt, g, von) : Erfassung.Aenderung(jetzt, g, von);
        // Verglichen wird mit dem Stand am gewählten Datum (Korrektur: die betroffene Version; Änderung: was ab dann gälte)
        var alt = StammdatenEingabe.Aus(person.StandAm(datum, jetzt));
        var geaendert = new List<string>();
        static bool Anders(string? a, string? b) => !string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

        var nameNeu = Anders(alt.Vorname, neu.Vorname) || Anders(alt.Nachname, neu.Nachname);
        if (nameNeu)
        {
            _ = korrektur
                ? person.NameKorrigieren(neu.Vorname.Trim(), neu.Nachname.Trim(), datum, g, jetzt, von)
                : person.NameAendernAb(neu.Vorname.Trim(), neu.Nachname.Trim(), datum, g, jetzt, von);
            geaendert.Add("Name");
        }

        foreach (var (art, a, n, feld) in new[]
                 {
                     (MerkmalArt.Titel, alt.Titel, neu.Titel, "Titel"),
                     (MerkmalArt.Anrede, alt.Anrede, neu.Anrede, "Anrede"),
                     (MerkmalArt.Geschlecht, alt.Geschlecht, neu.Geschlecht, "Geschlecht"),
                     (MerkmalArt.Geburtsname, alt.Geburtsname, neu.Geburtsname, "Geburtsname"),
                 })
        {
            if (Anders(a, n))
            {
                person.AngabeAendern(art, n, datum, e, korrektur);
                geaendert.Add(feld);
            }
        }

        if (neu.Geburtsdatum is { } gd && gd != alt.Geburtsdatum)
        {
            person.GeburtsdatumKorrigieren(gd, Erfassung.Korrektur(jetzt, g, von));
            geaendert.Add("Geburtsdatum");
        }

        if (!string.IsNullOrWhiteSpace(neu.Sprache) && Anders(alt.Sprache, neu.Sprache))
        {
            person.SpracheAendernAb(neu.Sprache, korrektur ? DateOnly.MinValue : datum, e);
            geaendert.Add("Sprache");
        }

        AnschriftUebernehmen(person, alt, neu, korrektur, datum, e, geaendert);
        KontakteUebernehmen(person, alt, neu, datum, e, geaendert);

        if (geaendert.Count == 0)
        {
            throw new InvalidOperationException("Es wurde nichts geändert.");
        }

        await protokoll.ProtokollierenAsync(korrektur ? "Person.StammdatenKorrigiert" : "Person.StammdatenGeaendert",
            $"{Pseudonym.Person(personId)}: {string.Join(", ", geaendert)} {(korrektur ? $"korrigiert (betroffen am {datum:dd.MM.yyyy})" : $"geändert ab {datum:dd.MM.yyyy}")}; Details in der Datenhistorie",
            nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new StammdatenErgebnis(geaendert, nameNeu);
    }

    private static void AnschriftUebernehmen(Person person, StammdatenEingabe alt, StammdatenEingabe neu, bool korrektur, DateOnly datum, Erfassung e,
        List<string> geaendert)
    {
        static bool Anders(string? a, string? b) => !string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);
        if (!Anders(alt.Strasse, neu.Strasse) && !Anders(alt.Postleitzahl, neu.Postleitzahl) && !Anders(alt.Ort, neu.Ort) && !Anders(alt.AdressZusatz, neu.AdressZusatz))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(neu.Strasse) || string.IsNullOrWhiteSpace(neu.Postleitzahl) || string.IsNullOrWhiteSpace(neu.Ort))
        {
            throw new ArgumentException("Für die Anschrift bitte Straße, Postleitzahl und Ort angeben.");
        }

        var aufenthalt = StammdatenEingabe.Hauptanschrift(person.StandAm(datum, e.Am))?.AdressId;
        if (aufenthalt is not { } id)
        {
            person.AdresseHinzufuegen(neu.AdressZusatz, neu.Strasse.Trim(), neu.Postleitzahl.Trim(), neu.Ort.Trim(), datum, e);
        }
        else if (korrektur)
        {
            person.AdresseKorrigieren(id, neu.AdressZusatz, neu.Strasse.Trim(), neu.Postleitzahl.Trim(), neu.Ort.Trim(), datum, e);
        }
        else
        {
            person.UmzugAb(id, neu.AdressZusatz, neu.Strasse.Trim(), neu.Postleitzahl.Trim(), neu.Ort.Trim(), datum, e);
        }

        geaendert.Add(korrektur ? "Anschrift" : "Anschrift (Umzug)");
    }

    private static void KontakteUebernehmen(Person person, StammdatenEingabe alt, StammdatenEingabe neu, DateOnly datum, Erfassung e, List<string> geaendert)
    {
        var stand = person.StandAm(datum, e.Am);
        if (!string.Equals(alt.Telefon?.Trim() ?? "", neu.Telefon?.Trim() ?? "", StringComparison.Ordinal))
        {
            var bisher = stand.Rufnummern.FirstOrDefault(r => r.Bevorzugt) ?? stand.Rufnummern.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(neu.Telefon))
            {
                if (bisher is not null)
                {
                    person.RufnummerAbmeldenAb(bisher.NummerId, datum, e);
                }
            }
            else if (bisher is not null)
            {
                person.RufnummerAendernAb(bisher.NummerId, bisher.Art, neu.Telefon.Trim(), bisher.Dienste, bisher.Einwilligung, true, datum, e);
            }
            else
            {
                person.RufnummerHinzufuegen(RufnummerArt.Mobil, neu.Telefon.Trim(), Messagingdienst.Anruf, false, true, datum, e);
            }

            geaendert.Add("Telefon");
        }

        if (!string.Equals(alt.EMail?.Trim() ?? "", neu.EMail?.Trim() ?? "", StringComparison.OrdinalIgnoreCase))
        {
            var bisher = stand.EMails.FirstOrDefault(x => x.Bevorzugt) ?? stand.EMails.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(neu.EMail))
            {
                person.EMailHinzufuegen(bisher?.Art ?? EMailArt.Privat, neu.EMail.Trim(), true, datum, e);
            }

            if (bisher is not null)
            {
                person.EMailAufgebenAb(bisher.AdressId, datum, e);
            }

            geaendert.Add("E-Mail");
        }
    }
}
