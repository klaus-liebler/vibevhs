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
public abstract class BitemporaleVersion<TSelf>
    where TSelf : BitemporaleVersion<TSelf>
{
    /// <summary>Nur für EF Core.</summary>
    protected BitemporaleVersion()
    {
        Grund = string.Empty;
    }

    protected BitemporaleVersion(DateOnly gueltigVon, DateOnly? gueltigBis, DateTimeOffset erfasstAm, string grund)
    {
        if (gueltigBis is { } bis && bis <= gueltigVon)
        {
            throw new ArgumentException($"Gültig bis ({bis:dd.MM.yyyy}) muss nach gültig von ({gueltigVon:dd.MM.yyyy}) liegen.", nameof(gueltigBis));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(grund);
        Id = Ids.Neu();
        GueltigVon = gueltigVon;
        GueltigBis = gueltigBis;
        ErfasstAm = erfasstAm;
        Grund = grund;
    }

    public Guid Id { get; private set; }

    /// <summary>Fachlich gültig ab diesem Tag (inklusive).</summary>
    public DateOnly GueltigVon { get; private set; }

    /// <summary>Fachlich gültig bis vor diesen Tag (exklusive); <c>null</c> = unbefristet.</summary>
    public DateOnly? GueltigBis { get; private set; }

    /// <summary>Zeitpunkt, ab dem das System diese Version kennt.</summary>
    public DateTimeOffset ErfasstAm { get; private set; }

    /// <summary>Zeitpunkt, ab dem diese Version durch eine andere ersetzt ist; <c>null</c> = aktueller Wissensstand.</summary>
    public DateTimeOffset? ErsetztAm { get; private set; }

    /// <summary>Warum es diese Version gibt (Pflicht, für die Nachvollziehbarkeit).</summary>
    public string Grund { get; private set; }

    public bool IstAktuellerWissensstand => ErsetztAm is null;

    public bool GiltAm(DateOnly tag) => GueltigVon <= tag && (GueltigBis is null || tag < GueltigBis);

    public bool BekanntAm(DateTimeOffset zeitpunkt) => ErfasstAm <= zeitpunkt && (ErsetztAm is null || zeitpunkt < ErsetztAm);

    /// <summary>Gleicher fachlicher Wert mit anderer Gültigkeit, neu erfasst.</summary>
    protected internal abstract TSelf KopieMit(DateOnly gueltigVon, DateOnly? gueltigBis, DateTimeOffset erfasstAm, string grund);

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
