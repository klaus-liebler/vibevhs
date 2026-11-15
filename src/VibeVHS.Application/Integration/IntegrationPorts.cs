using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Integration;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Integration;

public interface IIntegrationSpeicher
{
    /// <summary>Kursdaten zum Ändern.</summary>
    Task<Integrationskursdaten?> KursdatenAsync(Guid kursId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Integrationskursdaten>> AlleKursdatenAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Teilnahmeberechtigung>> BerechtigungenAsync(IEnumerable<Guid> personIds, CancellationToken abbruch = default);

    Task<IReadOnlyList<Identitaetspruefung>> IdentitaetspruefungenAsync(IEnumerable<Guid> personIds, CancellationToken abbruch = default);

    /// <summary>Lichtbild einer Person für einen Kurs (zum Ändern), nur wenn noch vorhanden.</summary>
    Task<Lichtbild?> LichtbildAsync(Guid personId, Guid kursId, CancellationToken abbruch = default);

    Task<IReadOnlyList<(Guid PersonId, Guid KursId)>> LichtbilderVorhandenAsync(Guid kursId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Lichtbild>> LichtbilderZumLoeschenAsync(DateOnly heute, CancellationToken abbruch = default);

    /// <summary>Anwesenheiten eines Kurses zum Ändern.</summary>
    Task<IReadOnlyList<Anwesenheit>> AnwesenheitenDesKursesAsync(Guid kursId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Anwesenheit>> VorlaeufigeAnwesenheitenAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<IkFahrtkosten>> FahrtkostenAsync(Guid kursId, CancellationToken abbruch = default);

    Task<IkFahrtkosten?> FahrtkostenantragAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Pruefungstermin>> PruefungenAsync(CancellationToken abbruch = default);

    Task<Pruefungstermin?> PruefungAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Pruefungsanmeldung>> PruefungsanmeldungenAsync(Guid? pruefungId = null, CancellationToken abbruch = default);

    Task<Pruefungsanmeldung?> PruefungsanmeldungAsync(Guid id, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Kursbeginnmeldung an das BAMF (InGe-Online, Mock).</summary>
public sealed record BamfKursmeldung(string BamfKursnummer, string Kursnummer, string Kursart, DateOnly Beginn, string Ort, IReadOnlyList<string> Teilnehmernummern);

/// <summary>Abrechnung eines Kursabschnitts gegenüber dem BAMF (Mock): nur Teilnehmernummern, keine Namen.</summary>
public sealed record BamfAbrechnung(string BamfKursnummer, string Abschnitt, DateOnly Von, DateOnly Bis, string Csv, long SummeCent);

public sealed record BamfPruefungsmeldung(Pruefungsart Art, DateTime Beginn, string Ort, IReadOnlyList<string> Teilnehmernummern);

/// <summary>
/// BAMF-Schnittstelle (InGe-Online, Abrechnung, Prüfungsanmeldung). In Stufe 1 ein Mock: Meldungen landen im Postausgang
/// und bekommen eine simulierte Vorgangsnummer.
/// </summary>
public interface IBamfSchnittstelle
{
    Task<string> KursbeginnMeldenAsync(BamfKursmeldung meldung, CancellationToken abbruch = default);

    Task<string> AbrechnungUebermittelnAsync(BamfAbrechnung abrechnung, CancellationToken abbruch = default);

    Task<string> PruefungAnmeldenAsync(BamfPruefungsmeldung meldung, CancellationToken abbruch = default);
}

/// <summary>Einstellungen des Integrationskursmoduls.</summary>
/// <param name="LichtbildFristTage">So viele Tage nach Kursende wird das Lichtbild gelöscht (FA-135).</param>
public sealed record IntegrationOptionen(int LichtbildFristTage = 14);

public static class Integrationsrollen
{
    public static readonly Rolle[] Bearbeiten = [Rolle.IntegrationskursTeam, Rolle.Verwaltungsleitung, Rolle.Leitung];
}

/// <summary>UE eines Teilnehmenden in einem Abschnitt.</summary>
public sealed record AbschnittUe(int Nr, decimal Anwesend, decimal Vorlaeufig, decimal Entschuldigt, decimal Gehalten);

public sealed record IkTeilnehmer(Belegung Belegung, string Name, Teilnahmeberechtigung? Berechtigung, Identitaetspruefung? Ausweis, bool Lichtbild,
    IReadOnlyList<AbschnittUe> Abschnitte)
{
    public decimal AnwesendUe => Abschnitte.Sum(a => a.Anwesend);

    public decimal GehaltenUe => Abschnitte.Sum(a => a.Gehalten);

    /// <summary>Anwesenheitsquote (abrechenbar) an den gehaltenen UE.</summary>
    public decimal Quote => GehaltenUe == 0 ? 0 : Math.Round(AnwesendUe / GehaltenUe * 100, 0);

    public bool AusweisOk => Ausweis is { StimmtUeberein: true };
}

/// <summary>Stand eines Abschnitts: geplante und gehaltene UE, Zeitraum, abgerechnet?</summary>
public sealed record Abschnittsstand(Kursabschnitt Abschnitt, decimal GeplantImKurs, decimal Gehalten, DateOnly? Von, DateOnly? Bis, bool Beendet, Guid? AbrechnungBelegId);

public sealed record IkUebersicht(Kurs Kurs, Integrationskursdaten Daten, IReadOnlyList<IkTeilnehmer> Teilnehmende, IReadOnlyList<Abschnittsstand> Abschnitte,
    IReadOnlyList<string> Kursbeginnhindernisse);

/// <summary>Ergebnis des Ausweis-Scans im Integrationskurs: Sichtvergleich durch die Kursleitung nötig (FA-136).</summary>
/// <param name="LichtbildDatenUri">Als data:-URI eingebettet und nicht als abrufbare Datei – so landet es nicht im Gerätespeicher.</param>
public sealed record Sichtvergleich(Guid PersonId, string Name, string? LichtbildDatenUri, bool AusweisGeprueft, string? Hinweis);
