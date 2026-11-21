namespace VibeVHS.Domain.Gemeinsam;

/// <summary>Wie eine Version entstanden ist.</summary>
public enum Erfassungsart
{
    /// <summary>Erste Erfassung eines Werts.</summary>
    Ersterfassung,

    /// <summary>Neuer Wert ab einem Datum; die Vergangenheit bleibt (z. B. Heirat, Umzug).</summary>
    AenderungAbDatum,

    /// <summary>Der Wert war schon immer falsch erfasst und wird für seinen ganzen Zeitraum ersetzt.</summary>
    Korrektur,

    /// <summary>Der Wert gilt ab einem Datum nicht mehr (z. B. Nummer abgemeldet, Adresse aufgegeben).</summary>
    Beendet,

    /// <summary>Technische Abgrenzung: alter Wert mit verkürzter Gültigkeit nach einer Änderung ab Datum.</summary>
    Abgrenzung,
}

/// <summary>Wann, von wem, wie und warum eine Version erfasst wurde.</summary>
public sealed record Erfassung(DateTimeOffset Am, string Von, Erfassungsart Art, string Grund)
{
    /// <summary>
    /// Der Änderungsvorgang dieser Erfassung (Zeitpunkt, Person, Grund – einmal gespeichert). Kopien mit anderer Art (<c>with { Art = … }</c>)
    /// gehören zum selben Vorgang.
    /// </summary>
    public Aenderungsvorgang Vorgang { get; init; } = new(Am, Von, Grund);

    public const string System = "System";

    public static Erfassung Erst(DateTimeOffset am, string? von = null, string grund = "Ersterfassung")
        => new(am, von ?? System, Erfassungsart.Ersterfassung, grund);

    public static Erfassung Aenderung(DateTimeOffset am, string grund, string? von = null)
        => new(am, von ?? System, Erfassungsart.AenderungAbDatum, grund);

    public static Erfassung Korrektur(DateTimeOffset am, string grund, string? von = null)
        => new(am, von ?? System, Erfassungsart.Korrektur, grund);

    public static Erfassung Ende(DateTimeOffset am, string grund, string? von = null)
        => new(am, von ?? System, Erfassungsart.Beendet, grund);

    public static string Anzeigename(Erfassungsart art) => art switch
    {
        Erfassungsart.Ersterfassung => "Ersterfassung",
        Erfassungsart.AenderungAbDatum => "Änderung ab Datum",
        Erfassungsart.Korrektur => "Korrektur",
        Erfassungsart.Beendet => "Beendet",
        Erfassungsart.Abgrenzung => "Abgrenzung",
        _ => art.ToString(),
    };
}
