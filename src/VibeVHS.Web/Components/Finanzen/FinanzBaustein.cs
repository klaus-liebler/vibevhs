using Microsoft.AspNetCore.Components;
using Radzen;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Web.Sicherheit;

namespace VibeVHS.Web.Components.Finanzen;

/// <summary>Gemeinsame Grundlage der Finanzbausteine: Benutzer, Freigaben mit Begründung, Ausführen mit Rückmeldung.</summary>
public abstract class FinanzBaustein : ComponentBase
{
    [Inject]
    private Vorgang Vorgang { get; set; } = default!;

    [Inject]
    protected NotificationService Hinweise { get; set; } = default!;

    [Inject]
    protected TimeProvider Uhr { get; set; } = default!;

    protected Benutzer Benutzer { get; private set; } = Benutzer.Automatik;

    protected DateOnly Heute => DateOnly.FromDateTime(Uhr.GetLocalNow().Date);

    /// <summary>Buchen, stornieren, freigeben: Buchhaltung, Verwaltungsleitung, Leitung. Die Prüfung liest nur.</summary>
    protected Freigabe Buchen => Benutzer.Rolle == Rolle.Pruefung
        ? Freigabe.Nein("Die Rolle „Prüfung“ hat nur lesenden Zugriff.")
        : Freigabe.NurFuer(Benutzer.Rolle, Finanzrollen.Buchen);

    protected bool Beschaeftigt { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        Benutzer = await Vorgang.BenutzerAsync();
        await LadenAsync();
    }

    protected abstract Task LadenAsync();

    protected Task<T> LesenAsync<T>(Func<IServiceProvider, Task<T>> arbeit) => Vorgang.AusfuehrenAsync(arbeit);

    protected Task LesenAsync(Func<IServiceProvider, Task> arbeit) => Vorgang.AusfuehrenAsync(arbeit);

    /// <summary>Aktion ausführen, Finanzaufgaben abgleichen, Ergebnis melden und neu laden.</summary>
    protected async Task<bool> AusfuehrenAsync(Func<IServiceProvider, Task<string?>> arbeit, string titel)
    {
        Beschaeftigt = true;
        try
        {
            var meldung = await Vorgang.AusfuehrenAsync(async d =>
            {
                var m = await arbeit(d);
                await d.GetRequiredService<FinanzRoutinen>().AufgabenAbgleichenAsync();
                return m;
            });
            Hinweise.Notify(NotificationSeverity.Success, titel, meldung ?? "Erledigt – steht im Finanzprotokoll.");
            await LadenAsync();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Hinweise.Notify(NotificationSeverity.Warning, "Nicht möglich", ex.Message);
            return false;
        }
        finally
        {
            Beschaeftigt = false;
        }
    }

    protected static string Euro(long cent) => new Geld(cent).ToString();
}
