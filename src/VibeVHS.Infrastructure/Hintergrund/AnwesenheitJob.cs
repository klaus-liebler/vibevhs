using Quartz;
using VibeVHS.Application.Durchfuehrung;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>Nachts: offene Anwesenheitslisten vergangener Veranstaltungen abschließen (Nichterscheinen-Regel bei Kontingenten).</summary>
[DisallowConcurrentExecution]
internal sealed class AnwesenheitJob(AnwesenheitDienst dienst) : IJob
{
    public static readonly JobKey Schluessel = new("anwesenheit-abschliessen", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
        => await dienst.OffeneListenAbschliessenAsync(cancellationToken);
}
