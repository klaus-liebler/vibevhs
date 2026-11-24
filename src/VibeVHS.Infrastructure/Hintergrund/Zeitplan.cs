using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using VibeVHS.Application.Betrieb;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>Zeitgesteuerte Läufe nach Uhrzeit (Quartz-Cron), Ortszeit. Gemeinsame Liste für den Echtbetrieb und den Spielzeitplaner.</summary>
public static class Zeitplan
{
    public sealed record Eintrag(JobKey Schluessel, Type Job, string Name, string Cron);

    public static IReadOnlyList<Eintrag> Eintraege { get; } =
    [
        new(LoeschlaufJob.Schluessel, typeof(LoeschlaufJob), "loeschlauf-monatlich", "0 0 3 1 * ?"),
        new(AnmeldungJob.Schluessel, typeof(AnmeldungJob), "anmeldung-stuendlich", "0 5 * * * ?"),
        new(TermineFortschreibenJob.Schluessel, typeof(TermineFortschreibenJob), "termine-nacht", "0 15 2 * * ?"),
        new(MerkmalAblaufJob.Schluessel, typeof(MerkmalAblaufJob), "merkmale-nacht", "0 15 2 * * ?"),
        new(QualifikationJob.Schluessel, typeof(QualifikationJob), "qualifikation-nacht", "0 15 2 * * ?"),
        new(AnwesenheitJob.Schluessel, typeof(AnwesenheitJob), "anwesenheit-nacht", "0 15 2 * * ?"),
        new(AufgabenJob.Schluessel, typeof(AufgabenJob), "aufgaben-stuendlich", "0 10 * * * ?"),
        new(ErinnerungJob.Schluessel, typeof(ErinnerungJob), "erinnerung-morgens", "0 0 7 * * ?"),
        new(SchluesselJob.Schluessel, typeof(SchluesselJob), "schluessel-nachts", "0 20 2 * * ?"),
        new(IntegrationJob.Schluessel, typeof(IntegrationJob), "integration-nachts", "0 45 2 * * ?"),
        new(FinanzJob.Schluessel, typeof(FinanzJob), "finanzen-morgens", "0 30 6 * * ?"),
    ];

    /// <summary>Läufe, deren Zeitpunkt in (von, bis] liegt – je Lauf höchstens einmal (übersprungene Zeitpunkte werden nicht einzeln nachgeholt).</summary>
    public static IReadOnlyList<Eintrag> Faellig(DateTimeOffset von, DateTimeOffset bis, TimeZoneInfo zone)
        => Eintraege.Where(e =>
        {
            var cron = new CronExpression(e.Cron, zone);
            return cron.GetNextValidTimeAfter(von) is { } naechster && naechster <= bis;
        }).ToList();

    /// <summary>Einen Lauf direkt ausführen (eigener Bereich, wie Quartz ihn anlegen würde).</summary>
    public static async Task AusfuehrenAsync(IServiceProvider dienste, Eintrag eintrag, CancellationToken abbruch)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var job = (IJob)ActivatorUtilities.CreateInstance(bereich.ServiceProvider, eintrag.Job);
        await job.Execute(null!, abbruch);
    }
}

/// <summary>
/// Zeitplaner im Schulungsbetrieb (QA-68, aus SchoolCraft FK-14 R5): Statt der echten Uhr prüft er jede Sekunde, welche Zeitpunkte der
/// Läufe die Spielzeit überschritten hat, und startet jeden betroffenen Lauf einmal. Nach Sprüngen beginnt die Prüfung beim neuen Zeitpunkt.
/// </summary>
public sealed class Spielzeitplaner(IServiceProvider dienste, Spielzeit uhr, ILogger<Spielzeitplaner> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken abbruch)
    {
        var letzte = uhr.GetUtcNow();
        var weltstand = uhr.Weltstand;
        using var takt = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await takt.WaitForNextTickAsync(abbruch))
        {
            if (uhr.Wartung)
            {
                continue;
            }

            var jetzt = uhr.GetUtcNow();
            if (uhr.Weltstand != weltstand || jetzt <= letzte)
            {
                (letzte, weltstand) = (jetzt, uhr.Weltstand);
                continue;
            }

            await NachholenAsync(dienste, letzte, jetzt, uhr.LocalTimeZone, log, abbruch);
            letzte = jetzt;
        }
    }

    /// <summary>Alle in (von, bis] fälligen Läufe einmal ausführen (auch für den Vorlagenbau).</summary>
    public static async Task<int> NachholenAsync(IServiceProvider dienste, DateTimeOffset von, DateTimeOffset bis, TimeZoneInfo zone, ILogger log,
        CancellationToken abbruch)
    {
        var faellig = Zeitplan.Faellig(von, bis, zone);
        foreach (var e in faellig)
        {
            try
            {
                await Zeitplan.AusfuehrenAsync(dienste, e, abbruch);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Spielzeit: Lauf {Lauf} gescheitert", e.Name);
            }
        }

        return faellig.Count;
    }
}
