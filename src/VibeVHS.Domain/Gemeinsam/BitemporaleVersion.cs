namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Eine Version eines zeitlich veränderlichen Werts mit zwei Zeitachsen:
/// <list type="bullet">
/// <item><b>Gültigkeit</b> (fachliche Zeit): <see cref="GueltigVon"/> inklusive bis <see cref="GueltigBis"/> exklusive (halboffen, <c>null</c> = offen).</item>
/// <item><b>Erfassung</b> (Systemzeit): <see cref="ErfasstAm"/> inklusive bis <see cref="ErsetztAm"/> exklusive.</item>
/// </list>
/// Versionen werden nie gelöscht oder überschrieben, sondern nur durch neue ersetzt.
/// So lässt sich jederzeit beantworten: "Was galt am Tag X nach unserem Wissensstand vom Zeitpunkt Y?"
/// </summary>
public abstract class BitemporaleVersion<TSelf> : IHistorienwert
    where TSelf : BitemporaleVersion<TSelf>
{
    /// <summary>Nur für EF Core.</summary>
    protected BitemporaleVersion()
    {
        ErfasstDurch = null!;
    }

    protected BitemporaleVersion(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung)
    {
        ArgumentNullException.ThrowIfNull(erfassung);
        if (gueltigBis is { } bis && bis <= gueltigVon)
        {
            throw new ArgumentException($"Gültig bis ({bis:dd.MM.yyyy}) muss nach gültig von ({gueltigVon:dd.MM.yyyy}) liegen.", nameof(gueltigBis));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(erfassung.Grund);
        Id = Ids.Neu();
        GueltigVon = gueltigVon;
        GueltigBis = gueltigBis;
        ErfasstDurch = erfassung.Vorgang;
        Erfassungsart = erfassung.Art;
    }

    public Guid Id { get; private set; }

    /// <summary>Fachlich gültig ab diesem Tag (inklusive).</summary>
    public DateOnly GueltigVon { get; private set; }

    /// <summary>Fachlich gültig bis vor diesen Tag (exklusive); <c>null</c> = unbefristet.</summary>
    public DateOnly? GueltigBis { get; private set; }

    /// <summary>Aenderungsvorgang, der diese Zeile angelegt hat (Zeitpunkt, Person, Grund).</summary>
    public Aenderungsvorgang ErfasstDurch { get; private set; }

    /// <summary>Aenderungsvorgang, durch den diese Zeile ersetzt wurde (neueres Wissen); <c>null</c> = aktueller Wissensstand.</summary>
    public Aenderungsvorgang? ErsetztDurch { get; private set; }

    public DateTimeOffset ErfasstAm => ErfasstDurch.Am;

    public string ErfasstVon => ErfasstDurch.Von;

    public Erfassungsart Erfassungsart { get; private set; }

    public DateTimeOffset? ErsetztAm => ErsetztDurch?.Am;

    /// <summary>Grund des Vorgangs; eine Abgrenzung (Ende der Gültigkeit wegen einer Änderung) nennt den Grund der Änderung.</summary>
    public string Grund => Erfassungsart == Erfassungsart.Abgrenzung ? $"Ende der Gültigkeit: {ErfasstDurch.Grund}" : ErfasstDurch.Grund;

    public bool IstAktuellerWissensstand => ErsetztAm is null;

    /// <summary>Beendet-Versionen tragen keinen Wert, sondern markieren das Ende eines Gegenstands.</summary>
    public bool IstEndemarke => Erfassungsart == Erfassungsart.Beendet;

    public abstract string Datenart { get; }

    public abstract string Feld { get; }

    public abstract string Gegenstand { get; }

    public abstract string Wert { get; }

    public bool GiltAm(DateOnly tag) => GueltigVon <= tag && (GueltigBis is null || tag < GueltigBis);

    public bool BekanntAm(DateTimeOffset zeitpunkt) => ErfasstAm <= zeitpunkt && (ErsetztAm is null || zeitpunkt < ErsetztAm);

    /// <summary>Gleicher fachlicher Wert mit anderer Gültigkeit und neuer Erfassung.</summary>
    protected internal abstract TSelf KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, Erfassung erfassung);

    /// <summary>Anonymisierung: Grund/Nachweis kann Personendaten enthalten und wird überschrieben.</summary>
    internal void GrundAnonymisieren() => ErfasstDurch = new Aenderungsvorgang(ErfasstDurch.Am, ErfasstDurch.Von, "anonymisiert");

    internal void Ersetzen(Aenderungsvorgang durch)
    {
        ArgumentNullException.ThrowIfNull(durch);
        if (ErsetztDurch is not null)
        {
            throw new InvalidOperationException("Diese Version ist bereits ersetzt.");
        }

        if (durch.Am < ErfasstAm)
        {
            throw new InvalidOperationException("Eine Version kann nicht vor ihrer Erfassung ersetzt werden.");
        }

        ErsetztDurch = durch;
    }
}

/// <summary>Einheitliche Sicht auf alle Versionen für die Datenhistorie.</summary>
public interface IHistorienwert
{
    /// <summary>Datenart für die Filter-Chips, z. B. "Name", "Adressen", "Telefon".</summary>
    string Datenart { get; }

    /// <summary>Feldbezeichnung, z. B. "Postadresse" oder "Rufnummer (mobil)".</summary>
    string Feld { get; }

    /// <summary>Welcher Gegenstand (z. B. welche der Adressen) – Versionen mit gleichem Gegenstand bilden eine Reihe.</summary>
    string Gegenstand { get; }

    /// <summary>Lesbarer Wert dieser Version.</summary>
    string Wert { get; }

    DateOnly GueltigVon { get; }

    DateOnly? GueltigBis { get; }

    DateTimeOffset ErfasstAm { get; }

    string ErfasstVon { get; }

    Erfassungsart Erfassungsart { get; }

    DateTimeOffset? ErsetztAm { get; }

    string Grund { get; }

    bool IstEndemarke { get; }

    bool GiltAm(DateOnly tag);

    bool BekanntAm(DateTimeOffset zeitpunkt);
}
