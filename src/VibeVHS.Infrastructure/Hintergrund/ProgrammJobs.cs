using Microsoft.Extensions.Logging;
using Quartz;
using VibeVHS.Application.Programm;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>Täglich: unbefristete Kurse rollierend bis 12 Wochen voraus fortschreiben.</summary>
[DisallowConcurrentExecution]
internal sealed class TermineFortschreibenJob(ProgrammDienst programm, ILogger<TermineFortschreibenJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("termine-fortschreiben", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var neu = await programm.UnbefristeteFortschreibenAsync(cancellationToken);
        log.LogInformation("Rollierende Termine: {Anzahl} neue Veranstaltungen", neu);
    }
}

/// <summary>Täglich: ablaufende Zulassungen/Merkmale von Orten prüfen und Aufgaben anlegen.</summary>
[DisallowConcurrentExecution]
internal sealed class MerkmalAblaufJob(ProgrammDienst programm, ILogger<MerkmalAblaufJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("merkmal-ablauf", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var neu = await programm.AblaufendeMerkmalePruefenAsync(abbruch: cancellationToken);
        log.LogInformation("Ablaufende Merkmale: {Anzahl} neue Aufgaben", neu);
    }
}
