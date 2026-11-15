using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using VibeVHS.Application.Aufgaben;

namespace VibeVHS.Infrastructure.Hintergrund;

/// <summary>
/// Stündlich: Mindestteilnehmerprüfung, Wartelisten-Nachrücken, fehlgeschlagene Nachrichten, wartende Löschläufe
/// und Eskalation überfälliger Aufgaben.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class AufgabenJob(Routinen routinen, ILogger<AufgabenJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("aufgaben-routinen", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var lauf = await routinen.StuendlichAsync(cancellationToken);
        log.LogInformation("Aufgaben-Routinen: {Aufgaben}; {Abgesagt} Absagen, {Nachruecken} Nachrückangebote, {Eskaliert} eskaliert",
            lauf.Aufgaben, lauf.Abgesagt, lauf.Nachrueckangebote, lauf.Eskaliert);
    }
}

/// <summary>Täglich morgens: Erinnerung an Teilnehmende vor Kursbeginn (Automatik:ErinnerungTageVorher, Standard 2).</summary>
[DisallowConcurrentExecution]
internal sealed class ErinnerungJob(Routinen routinen, IConfiguration konfiguration, ILogger<ErinnerungJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("erinnerung-kursbeginn", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var anzahl = await routinen.ErinnerungenAsync(konfiguration.GetValue("Automatik:ErinnerungTageVorher", 2), cancellationToken);
        log.LogInformation("Erinnerungen vor Kursbeginn: {Anzahl} verschickt", anzahl);
    }
}
