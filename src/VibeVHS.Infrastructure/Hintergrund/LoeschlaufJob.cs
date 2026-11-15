using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using VibeVHS.Application.Datenschutz;
using VibeVHS.Domain.Datenschutz;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>
/// Monatlich: Löschlauf als Vorschau vorbereiten (Personen ohne Aktivität seit Datenschutz:InaktivMonate, Standard 36).
/// Ausgeführt wird erst nach zwei Freigaben durch verschiedene Personen.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class LoeschlaufJob(DatenschutzDienst datenschutz, IDatenschutzSpeicher speicher, IConfiguration konfiguration, ILogger<LoeschlaufJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("loeschlauf-vorschau", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        if ((await speicher.LoeschlaeufeAsync(cancellationToken)).Any(l => l.Status is Loeschlaufstatus.Vorschau or Loeschlaufstatus.TeilweiseFreigegeben))
        {
            log.LogInformation("Löschlauf: Es wartet bereits ein Lauf auf Freigabe");
            return;
        }

        var lauf = await datenschutz.LoeschlaufVorschauAsync(konfiguration.GetValue("Datenschutz:InaktivMonate", 36), cancellationToken);
        log.LogInformation("Löschlauf vorbereitet: {Treffer} Treffer", lauf.Treffer);
    }
}
