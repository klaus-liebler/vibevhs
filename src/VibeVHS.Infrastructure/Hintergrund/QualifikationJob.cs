using Quartz;
using VibeVHS.Application.Programm;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>Täglich: Aufgaben bei bald ablaufenden oder fehlenden Qualifikationen und betroffenen künftigen Veranstaltungen.</summary>
[DisallowConcurrentExecution]
internal sealed class QualifikationJob(QualifikationsDienst dienst) : IJob
{
    public static readonly JobKey Schluessel = new("qualifikationen-pruefen", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
        => await dienst.AufgabenErzeugenAsync(abbruch: cancellationToken);
}
