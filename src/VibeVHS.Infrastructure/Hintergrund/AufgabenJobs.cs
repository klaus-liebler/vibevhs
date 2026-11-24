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

/// <summary>Täglich: Rechnungslauf, Lastschrift- und Mahnvorschlag, Honorarentwürfe zum Monatsanfang, Finanzaufgaben.</summary>
[DisallowConcurrentExecution]
internal sealed class FinanzJob(VibeVHS.Application.Finanzen.FinanzRoutinen routinen, ILogger<FinanzJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("finanz-routinen", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var lauf = await routinen.TaeglichAsync(abbruch: cancellationToken);
        log.LogInformation("Finanzroutinen: {Lauf}", lauf);
    }
}

/// <summary>Täglich: Lichtbilder nach Kursende plus Frist löschen, Integrationskurs-Aufgaben abgleichen.</summary>
[DisallowConcurrentExecution]
internal sealed class IntegrationJob(VibeVHS.Application.Integration.IntegrationRoutinen routinen, ILogger<IntegrationJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("integration-routinen", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var (geloescht, aufgaben) = await routinen.TaeglichAsync(cancellationToken);
        log.LogInformation("Integrationskurse: {Geloescht} Lichtbilder gelöscht; Aufgaben {Aufgaben}", geloescht, aufgaben);
    }
}

/// <summary>Täglich 01:30: Online-Sicherung der Datenbank mit Prüfsumme und Wiederherstellungstest.</summary>
[DisallowConcurrentExecution]
internal sealed class SicherungJob(VibeVHS.Application.Betrieb.ISicherung sicherung, ILogger<SicherungJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("sicherung", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var e = await sicherung.SichernAsync(cancellationToken);
        log.LogInformation("Sicherung {Datei}: {Ergebnis}", e.Datei, e.Pruefergebnis);
    }
}

/// <summary>Nachts: Aufgaben „Schlüssel zurückfordern“ (QA-67).</summary>
[DisallowConcurrentExecution]
internal sealed class SchluesselJob(Application.Programm.SchluesselDienst schluessel, ILogger<SchluesselJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("schluessel-rueckgabe", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var ergebnis = await schluessel.RueckgabeAufgabenAsync(cancellationToken);
        log.LogInformation("Schlüssel zurückfordern: {Ergebnis}", ergebnis);
    }
}
