using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using VibeVHS.Application.Betrieb;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>
/// Startet nach dem Serverstart die Abgleich-Jobs einmal nacheinander in fester Reihenfolge (Aufgabenregeln und Eskalation
/// zuletzt, damit sie die Ergebnisse der anderen sehen) und meldet den Fortschritt an <see cref="Startabgleich"/>.
/// Früher liefen die Jobs mit festen Verzögerungen (20–75 s ab Registrierung); nach langem Start feuerten sie gleichzeitig.
/// </summary>
internal sealed class StartabgleichDienst(ISchedulerFactory fabrik, Startabgleich abgleich,
    IHostApplicationLifetime lebensdauer, ILogger<StartabgleichDienst> log) : BackgroundService
{
    /// <summary>Reihenfolge des Abgleichs nach dem Start.</summary>
    public static readonly (JobKey Schluessel, string Anzeige)[] Reihenfolge =
    [
        (ZwischenstandJob.Schluessel, "Personen-Zwischenstände (Rollen, Namen, Adressen ab heute)"),
        (TermineFortschreibenJob.Schluessel, "Termine fortschreiben"),
        (MerkmalAblaufJob.Schluessel, "Ablaufende Merkmale"),
        (QualifikationJob.Schluessel, "Qualifikationen und Lehrfreigaben"),
        (AnwesenheitJob.Schluessel, "Anwesenheitslisten abschließen"),
        (FinanzJob.Schluessel, "Finanzen (Rechnungen, Mahnwesen, Honorare)"),
        (IntegrationJob.Schluessel, "Integrationskurse"),
        (AufgabenJob.Schluessel, "Aufgabenregeln und Eskalation"),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Erst wenn Kestrel und Quartz laufen.
        var gestartet = new TaskCompletionSource();
        await using var _ = lebensdauer.ApplicationStarted.Register(() => gestartet.TrySetResult());
        await gestartet.Task.WaitAsync(stoppingToken);
        try
        {
            await AbgleichenAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Der Abgleich ist eine Komfortfunktion; die stündlichen/nächtlichen Läufe holen alles nach.
            log.LogError(ex, "Startabgleich abgebrochen");
            foreach (var (schluessel, _) in Reihenfolge)
            {
                abgleich.Beendet(schluessel.ToString(), false);
            }
        }
    }

    private async Task AbgleichenAsync(CancellationToken stoppingToken)
    {
        var planer = await fabrik.GetScheduler(stoppingToken);
        var listener = new StartabgleichListener();
        planer.ListenerManager.AddJobListener(listener, GroupMatcher<JobKey>.GroupEquals("automatik"));
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (schluessel, anzeige) in Reihenfolge)
        {
            abgleich.Begonnen(schluessel.ToString());
            var fertig = listener.Erwarten(schluessel);
            try
            {
                await planer.TriggerJob(schluessel, stoppingToken);
                var ok = await fertig.WaitAsync(TimeSpan.FromMinutes(2), stoppingToken);
                abgleich.Beendet(schluessel.ToString(), ok);
            }
            catch (TimeoutException)
            {
                log.LogWarning("Startabgleich: {Schritt} nach 2 Minuten nicht fertig, weiter mit dem nächsten Schritt", anzeige);
                abgleich.Beendet(schluessel.ToString(), false);
            }
            catch (SchedulerException ex)
            {
                log.LogWarning(ex, "Startabgleich: {Schritt} konnte nicht gestartet werden", anzeige);
                abgleich.Beendet(schluessel.ToString(), false);
            }
        }

        planer.ListenerManager.RemoveJobListener(listener.Name);
        log.LogInformation("Startabgleich fertig in {Dauer} ms", uhr.ElapsedMilliseconds);
    }
}

/// <summary>Meldet das Ende von Jobs, auf die der Startabgleich wartet.</summary>
internal sealed class StartabgleichListener : IJobListener
{
    private readonly ConcurrentDictionary<JobKey, TaskCompletionSource<bool>> wartend = new();

    public string Name => "startabgleich";

    public Task<bool> Erwarten(JobKey schluessel)
        => wartend.GetOrAdd(schluessel, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    // Quartz 4: Listener-Methoden liefern ValueTask; nur das Ende interessiert, die übrigen haben Standardimplementierungen.
    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (wartend.TryRemove(context.JobDetail.Key, out var tcs))
        {
            tcs.TrySetResult(jobException is null);
        }

        return ValueTask.CompletedTask;
    }
}
