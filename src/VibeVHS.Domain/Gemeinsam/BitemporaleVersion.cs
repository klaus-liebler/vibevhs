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
        Grund = string.Empty;
        ErfasstVon = string.Empty;
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
        ErfasstAm = erfassung.Am;
        ErfasstVon = string.IsNullOrWhiteSpace(erfassung.Von) ? Erfassung.System : erfassung.Von;
        Erfassungsart = erfassung.Art;
        Grund = erfassung.Grund;
    }

    public Guid Id { get; private set; }

    /// <summary>Fachlich gültig ab diesem Tag (inklusive).</summary>
    public DateOnly GueltigVon { get; private set; }

    /// <summary>Fachlich gültig bis vor diesen Tag (exklusive); <c>null</c> = unbefristet.</summary>
    public DateOnly? GueltigBis { get; private set; }

    /// <summary>Zeitpunkt, ab dem das System diese Version kennt.</summary>
    public DateTimeOffset ErfasstAm { get; private set; }

    /// <summary>Wer die Version erfasst hat (Name und Rolle oder "Automatik").</summary>
    public string ErfasstVon { get; private set; }

    public Erfassungsart Erfassungsart { get; private set; }

    /// <summary>Zeitpunkt, ab dem diese Version durch eine andere ersetzt ist; <c>null</c> = aktueller Wissensstand.</summary>
    public DateTimeOffset? ErsetztAm { get; private set; }

    /// <summary>Grund bzw. Nachweis (Pflicht, für die Nachvollziehbarkeit).</summary>
    public string Grund { get; private set; }

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

    internal void Ersetzen(DateTimeOffset zeitpunkt)
    {
        if (ErsetztAm is not null)
        {
            throw new InvalidOperationException("Diese Version ist bereits ersetzt.");
        }

        if (zeitpunkt < ErfasstAm)
        {
            throw new InvalidOperationException("Eine Version kann nicht vor ihrer Erfassung ersetzt werden.");
        }

        ErsetztAm = zeitpunkt;
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
