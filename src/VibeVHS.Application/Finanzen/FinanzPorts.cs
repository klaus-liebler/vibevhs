using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Finanzen;

namespace VibeVHS.Application.Finanzen;

public interface IFinanzSpeicher
{
    Task<Finanzeinstellungen?> EinstellungenAsync(CancellationToken abbruch = default);

    /// <summary>Nummernkreis zum Ändern; fehlt er, wird er angelegt.</summary>
    Task<Nummernkreis> NummernkreisAsync(Belegart art, int jahr, CancellationToken abbruch = default);

    Task<IReadOnlyList<Nummernkreis>> NummernkreiseAsync(CancellationToken abbruch = default);

    /// <summary>Beleg zum Ändern (Festschreiben, Storno vermerken).</summary>
    Task<Beleg?> BelegAsync(Guid id, CancellationToken abbruch = default);

    Task<Beleg?> BelegNachNummerAsync(string nummer, CancellationToken abbruch = default);

    /// <summary>Alle Belege (nur lesen), optional nur einer Partei (Person oder Organisation).</summary>
    Task<IReadOnlyList<Beleg>> BelegeAsync(Guid? partei = null, CancellationToken abbruch = default);

    /// <summary>Belege zum Ändern (z. B. für die Übergabe oder Anonymisierung).</summary>
    Task<IReadOnlyList<Beleg>> BelegeZumAendernAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default);

    /// <summary>Belegungen mit Entgelt oder Gebühr (für den Rechnungslauf), nur lesen.</summary>
    Task<IReadOnlyList<Belegung>> AbrechenbareBelegungenAsync(CancellationToken abbruch = default);

    /// <summary>Termine mit Kursleitung im Zeitraum (auch ausgefallene) mit anonymen Teilnahmezahlen – Grundlage der Honorare.</summary>
    Task<IReadOnlyList<Dozententermin>> DozententermineAsync(DateTime von, DateTime bis, Guid? dozentId = null, CancellationToken abbruch = default);

    Task<IReadOnlyList<SepaMandat>> MandateAsync(Guid? personId = null, CancellationToken abbruch = default);

    Task<SepaMandat?> MandatAsync(Guid id, CancellationToken abbruch = default);

    Task<int> MandateAnzahlAsync(CancellationToken abbruch = default);

    Task<Bankverbindung?> BankverbindungAsync(Guid personId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Bankverbindung>> BankverbindungenAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Zahlungslauf>> ZahlungslaeufeAsync(CancellationToken abbruch = default);

    Task<Zahlungslauf?> ZahlungslaufAsync(Guid id, CancellationToken abbruch = default);

    Task<bool> KontoauszugVorhandenAsync(string sha256, string auszugId, CancellationToken abbruch = default);

    Task<IReadOnlyList<Kontoauszug>> KontoauszuegeAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Umsatz>> UmsaetzeAsync(Guid? kontoauszugId = null, bool nurOffene = false, CancellationToken abbruch = default);

    Task<Umsatz?> UmsatzAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Mahnlauf>> MahnlaeufeAsync(CancellationToken abbruch = default);

    Task<Mahnlauf?> MahnlaufAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Honorarvertrag>> HonorarvertraegeAsync(Guid? personId = null, CancellationToken abbruch = default);

    Task<Honorarvertrag?> HonorarvertragAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Uebergabe>> UebergabenAsync(CancellationToken abbruch = default);

    Task<Uebergabe?> UebergabeAsync(Guid id, CancellationToken abbruch = default);

    Task<Domain.Personen.Organisation?> OrganisationAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Domain.Personen.Organisation>> AuftraggeberAsync(CancellationToken abbruch = default);

    Task<Domain.Personen.Organisation?> OrganisationNachNameAsync(string name, CancellationToken abbruch = default);

    void Hinzufuegen(object eintrag);

    void Entfernen(object eintrag);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

public sealed record Dozententermin(Guid DozentId, Guid Id, Guid KursId, string Kursnummer, string Kursart, int Fachbereich, DateTime Beginn, DateTime Ende,
    bool Ausgefallen, DateTimeOffset? AusgefallenAm, string? Bemerkung, string Ort, int Angemeldet, int? Anwesend, int Ermaessigt, Guid? OrtId = null);

/// <summary>Manipulationsgeschütztes Änderungsprotokoll für Finanzdaten (Hash-Kette, FA-128).</summary>
public interface IFinanzprotokoll
{
    /// <summary>Fügt einen Eintrag an; gespeichert wird mit der fachlichen Änderung.</summary>
    Task SchreibenAsync(string aktion, string objektTyp, Guid? objektId, string? vorher, string? nachher, CancellationToken abbruch = default);

    Task<IReadOnlyList<Finanzprotokolleintrag>> AlleAsync(CancellationToken abbruch = default);
}

public static class Finanzrollen
{
    /// <summary>Wer Belege, Läufe und Zuordnungen bearbeitet.</summary>
    public static readonly Rolle[] Buchen = [Rolle.Buchhaltung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    /// <summary>Wer Finanzdaten sehen darf (inkl. Prüfung, nur lesend).</summary>
    public static readonly Rolle[] Lesen = [Rolle.Buchhaltung, Rolle.Verwaltungsleitung, Rolle.Leitung, Rolle.Pruefung, Rolle.Teilnehmerservice];

    /// <summary>Wer SEPA-Mandate und Bankverbindungen erfasst (auch am Tresen).</summary>
    public static readonly Rolle[] Mandate = [Rolle.Buchhaltung, Rolle.Verwaltungsleitung, Rolle.Leitung, Rolle.Teilnehmerservice];
}

/// <summary>Eine Zeile im Personenkonto.</summary>
public sealed record Kontozeile(Beleg Beleg, long WirkungCent, long? OffenCent, string? BezugNummer);

/// <summary>Teilnehmer- bzw. Kundenkonto (FA-30): Soll und Haben aus festgeschriebenen Belegen.</summary>
public sealed record Personenkonto(Guid ParteiId, IReadOnlyList<Kontozeile> Zeilen, IReadOnlyList<Beleg> Entwuerfe)
{
    public long SollCent => Zeilen.Where(z => z.WirkungCent > 0).Sum(z => z.WirkungCent);

    public long HabenCent => -Zeilen.Where(z => z.WirkungCent < 0).Sum(z => z.WirkungCent);

    /// <summary>Positiv: offene Forderung der VHS; negativ: Guthaben bzw. Verbindlichkeit der VHS.</summary>
    public long SaldoCent => SollCent - HabenCent;

    public long OffeneForderungenCent => Zeilen.Where(z => z.OffenCent > 0 && z.Beleg.Art is Belegart.Rechnung or Belegart.Mahnung).Sum(z => z.OffenCent!.Value);
}

/// <summary>Offene Beträge je Forderung (Rechnung, Mahnung) bzw. Verbindlichkeit (Honorarabrechnung).</summary>
public static class Offenposten
{
    public static IReadOnlyDictionary<Guid, long> Berechnen(IReadOnlyCollection<Beleg> belege)
    {
        ArgumentNullException.ThrowIfNull(belege);
        var aktiv = belege.Where(b => b.Festgeschrieben && !b.Storniert && b.Art != Belegart.Storno).ToList();
        var ergebnis = new Dictionary<Guid, long>();
        foreach (var f in aktiv.Where(b => b.Art is Belegart.Rechnung or Belegart.Mahnung or Belegart.Honorarabrechnung || (b.Art == Belegart.Gutschrift && b.BezugBelegId is null)))
        {
            var ausgleich = f.Art is Belegart.Honorarabrechnung or Belegart.Gutschrift
                ? aktiv.Where(z => z.BezugBelegId == f.Id && z.Art == Belegart.Zahlungsausgang).Sum(z => z.BetragCent)
                : aktiv.Where(z => z.BezugBelegId == f.Id && z.Art is Belegart.Zahlungseingang or Belegart.Gutschrift).Sum(z => z.BetragCent)
                  - aktiv.Where(z => z.BezugBelegId == f.Id && z.Art == Belegart.Zahlungsausgang).Sum(z => z.BetragCent);
            ergebnis[f.Id] = f.BetragCent - ausgleich;
        }

        return ergebnis;
    }

    /// <summary>Wirkung eines Belegs auf das Konto (Storno: Gegenbuchung zum stornierten Beleg).</summary>
    public static long Wirkung(Beleg b, IReadOnlyDictionary<Guid, Beleg> alle)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(alle);
        if (!b.Festgeschrieben)
        {
            return 0;
        }

        return b.Art == Belegart.Storno
            ? b.KontoCent(b.BezugBelegId is { } id && alle.TryGetValue(id, out var o) ? o.Art : Belegart.Rechnung)
            : b.KontoCent();
    }
}
