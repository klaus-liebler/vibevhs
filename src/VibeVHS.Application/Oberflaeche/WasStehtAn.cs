using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Stammdaten;

namespace VibeVHS.Application.Oberflaeche;

/// <summary>
/// Stellt die Vorschläge für "Was steht an?" auf der Startseite zusammen, passend zur aktiven Rolle.
/// Die Fachbereichsleitung sieht nur Einträge ihrer eigenen Fachbereiche.
/// </summary>
public static class WasStehtAn
{
    public sealed record Lage(int FehlgeschlageneNachrichten);

    public static IReadOnlyList<NaechsterSchritt> Fuer(Benutzer benutzer, IReadOnlyList<Fachbereich> alleFachbereiche, Lage lage)
    {
        ArgumentNullException.ThrowIfNull(benutzer);
        ArgumentNullException.ThrowIfNull(alleFachbereiche);
        var liste = new List<NaechsterSchritt>();
        if (lage.FehlgeschlageneNachrichten > 0)
        {
            liste.Add(new($"{lage.FehlgeschlageneNachrichten} fehlgeschlagene Nachricht(en) prüfen", "postausgang?status=Fehlgeschlagen",
                "Diese Nachrichten wurden nach mehreren Versuchen nicht zugestellt.", Hauptschritt: true));
        }

        switch (benutzer.Rolle)
        {
            case Rolle.Leitung:
                liste.Add(new("Programm und Raumplan", "raumplan", "Belegung aller Standorte mit Konflikten"));
                liste.Add(new("Protokoll ansehen", "protokoll", "Was ist heute passiert?", Hauptschritt: liste.Count == 0));
                break;
            case Rolle.Verwaltungsleitung:
                liste.Add(new("Aufgaben der Verwaltung verteilen", "aufgaben", "Gruppenkorb ansehen und Aufgaben zuweisen"));
                liste.Add(new("Postausgang ansehen", "postausgang", Hauptschritt: liste.Count == 0));
                break;
            case Rolle.Fachbereichsleitung:
                var eigene = EigeneFachbereiche(benutzer, alleFachbereiche);
                if (eigene.Count == 0)
                {
                    liste.Add(new("Fachbereiche zuordnen lassen", "", Freigabe: Freigabe.Nein(
                        "Ihrem Konto ist noch kein Fachbereich zugeordnet. Bitte an die Systemverwaltung wenden.")));
                }

                foreach (var fb in eigene)
                {
                    liste.Add(new($"{fb.Anzeige}: Kurse planen", $"kurse?fb={fb.Nr}", "Kurse des Fachbereichs, Konflikte und Termine"));
                }

                liste.Add(new("Protokoll ansehen", "protokoll", Hauptschritt: liste.Count(s => s.Status.Erlaubt) == 0));
                break;
            case Rolle.Teilnehmerservice:
                liste.Add(new("Person anlegen", "personen?neu=1", "Neue Teilnehmende oder Dozierende erfassen", Hauptschritt: liste.Count == 0));
                liste.Add(new("Anmeldung erfassen", "anmeldung", "Person und Kurs wählen, Buchungsprüfung, Bestätigung"));
                liste.Add(new("Personen durchsuchen", "personen"));
                break;
            case Rolle.Buchhaltung:
                liste.Add(new("Mahnvorschläge freigeben", "finanzen?tab=mahnwesen", "Vorschlag prüfen, Posten abwählen, freigeben"));
                liste.Add(new("Postausgang (Übergaben an die Kämmerei)", "postausgang", Hauptschritt: liste.Count == 0));
                break;
            case Rolle.IntegrationskursTeam:
                liste.Add(new("BAMF-Meldungen prüfen", "integration", "Abschnitte, Kostenbeiträge und Meldungen"));
                liste.Add(new("Postausgang ansehen", "postausgang", Hauptschritt: liste.Count == 0));
                break;
            case Rolle.Systemverwaltung:
                liste.Add(new("Protokoll prüfen", "protokoll", "Automatische Abläufe und Fehler im Blick behalten", Hauptschritt: liste.Count == 0));
                liste.Add(new("Benutzer und Rollen pflegen", "einstellungen?tab=benutzer", "Konten, Rollen und Fachbereiche für den Testbetrieb"));
                break;
        }

        liste.Add(new("Postausgang ansehen", "postausgang"));
        return liste.DistinctBy(s => s.Ziel == "postausgang" ? s.Ziel : s.Titel).ToList();
    }

    /// <summary>Die Fachbereiche, für die der Benutzer zuständig ist, in Nummernfolge.</summary>
    public static IReadOnlyList<Fachbereich> EigeneFachbereiche(Benutzer benutzer, IReadOnlyList<Fachbereich> alleFachbereiche)
        => alleFachbereiche.Where(f => benutzer.ZustaendigeFachbereiche.Contains(f.Nr)).OrderBy(f => f.Nr).ToList();
}
