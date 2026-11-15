using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

/// <summary>Beschäftigungsart von Dozierenden: steuert, ob ein Einsatz über Arbeitszeit oder Honorarvertrag läuft.</summary>
public enum Beschaeftigungsart
{
    /// <summary>Festangestellt: Einsatz über Arbeitszeit, kein Honorar.</summary>
    Festangestellt,

    /// <summary>Freiberuflich: Honorarvertrag und Honorarsatz.</summary>
    Freiberuflich,
}

public static class BeschaeftigungAnzeige
{
    public static string Text(Beschaeftigungsart art) => art == Beschaeftigungsart.Festangestellt ? "festangestellt" : "freiberuflich";

    public static string Erklaerung(Beschaeftigungsart art) => art == Beschaeftigungsart.Festangestellt
        ? "Einsatz über Arbeitszeit, kein Honorar"
        : "Honorarvertrag und Honorarsatz";
}

/// <summary>
/// Qualifikation einer Person (Merkmal aus dem gemeinsamen Katalog mit Bezug „Person“), z. B. BAMF-Lehrkraftzulassung,
/// mit Nachweis, ausstellender Stelle und Gültigkeit (bitemporal).
/// </summary>
public sealed class PersonQualifikation : BitemporaleVersion<PersonQualifikation>
{
    private PersonQualifikation()
    {
        Schluessel = string.Empty;
        Bezeichnung = string.Empty;
    }

    public PersonQualifikation(string schluessel, string bezeichnung, string? wert, string? nachweis, string? ausstellendeStelle,
        DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        : base(gueltigVon, gueltigBis, erfassung)
    {
        Schluessel = schluessel;
        Bezeichnung = bezeichnung;
        WertText = Leer(wert);
        Nachweis = Leer(nachweis);
        AusstellendeStelle = Leer(ausstellendeStelle);
    }

    /// <summary>Schlüssel im Merkmalskatalog.</summary>
    public string Schluessel { get; private set; }

    /// <summary>Name des Merkmals zum Erfassungszeitpunkt (für die Historie).</summary>
    public string Bezeichnung { get; private set; }

    /// <summary>z. B. Zulassungsart „mit Auflagen“.</summary>
    public string? WertText { get; private set; }

    /// <summary>Dokument oder Aktenzeichen.</summary>
    public string? Nachweis { get; private set; }

    public string? AusstellendeStelle { get; private set; }

    /// <summary>Letzter gültiger Tag (einschließlich), falls befristet.</summary>
    public DateOnly? GueltigBisEinschliesslich => GueltigBis?.AddDays(-1);

    public override string Datenart => "Qualifikationen";

    public override string Feld => Bezeichnung;

    public override string Gegenstand => Schluessel;

    public override string Wert => IstEndemarke
        ? "(entzogen / beendet)"
        : string.Join(" · ", new[] { WertText, Nachweis is null ? null : $"Nachweis: {Nachweis}", AusstellendeStelle is null ? null : $"ausgestellt von {AusstellendeStelle}" }
            .Where(t => t is not null)) is { Length: > 0 } t ? t : "vorhanden";

    internal void Anonymisieren()
    {
        Nachweis = Nachweis is null ? null : "–";
        AusstellendeStelle = AusstellendeStelle is null ? null : "–";
    }

    protected internal override PersonQualifikation KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
        => new(Schluessel, Bezeichnung, WertText, Nachweis, AusstellendeStelle, gueltigVon, gueltigBis, erfassung);

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
