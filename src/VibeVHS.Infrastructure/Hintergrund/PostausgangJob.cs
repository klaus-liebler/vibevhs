using Microsoft.Extensions.Logging;
using Quartz;
using VibeVHS.Application.Postausgang;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>Stellt regelmäßig die wartenden Nachrichten aus dem Postausgang zu.</summary>
[DisallowConcurrentExecution]
internal sealed class PostausgangJob(Zustellung zustellung, ILogger<PostausgangJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("postausgang-zustellen", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var ergebnis = await zustellung.WartendeZustellenAsync(abbruch: cancellationToken);
        if (ergebnis.Zugestellt + ergebnis.Fehler > 0)
        {
            log.LogInformation("Postausgang: {Zugestellt} zugestellt, {Fehler} Fehler", ergebnis.Zugestellt, ergebnis.Fehler);
        }
    }
}
