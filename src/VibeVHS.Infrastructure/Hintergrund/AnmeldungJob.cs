using Quartz;
using VibeVHS.Application.Anmeldung;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>Stündlich: abgelaufene Nachrückangebote verfallen lassen (nächste Person bekommt das Angebot) und Kontingente nach Ablauf schließen.</summary>
[DisallowConcurrentExecution]
internal sealed class AnmeldungJob(AnmeldungDienst dienst) : IJob
{
    public static readonly JobKey Schluessel = new("anmeldung-fristen", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        await dienst.AbgelaufeneAngeboteAsync(cancellationToken);
        await dienst.KontingenteAblaufenAsync(cancellationToken);
    }
}
