using Microsoft.Extensions.Logging;
using Quartz;
using VibeVHS.Application.Programm;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>Täglich: Aufgaben bei bald ablaufenden oder fehlenden Qualifikationen und betroffenen künftigen Veranstaltungen.</summary>
[DisallowConcurrentExecution]
internal sealed class QualifikationJob(QualifikationsDienst dienst, ILogger<QualifikationJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("qualifikationen-pruefen", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var ergebnis = await dienst.AufgabenErzeugenAsync(abbruch: cancellationToken);
        log.LogInformation("Qualifikationen: {Ergebnis}", ergebnis);
    }
}
