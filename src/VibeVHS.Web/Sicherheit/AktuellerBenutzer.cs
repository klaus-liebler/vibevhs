using Microsoft.AspNetCore.Components.Authorization;
using VibeVHS.Adapters.Mocks;
using VibeVHS.Application.Sicherheit;

namespace VibeVHS.Web.Sicherheit;

/// <summary>Hält den Benutzer für Arbeitsbereiche, die außerhalb der Blazor-Verbindung erzeugt werden.</summary>
internal sealed class BenutzerKontext
{
    public Benutzer? Gesetzt { get; set; }
}

/// <summary>
/// Ermittelt den angemeldeten Benutzer: zuerst aus einem gesetzten Kontext (siehe <see cref="Vorgang"/>),
/// dann aus der Blazor-Anmeldung, dann aus der HTTP-Anfrage. Ohne Anmeldung (Hintergrundjobs) ist es die Automatik.
/// </summary>
internal sealed class AktuellerBenutzer(BenutzerKontext kontext, IServiceProvider dienste, IHttpContextAccessor http)
    : IAktuellerBenutzer
{
    public async ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default)
    {
        if (kontext.Gesetzt is { } gesetzt)
        {
            return gesetzt;
        }

        if (dienste.GetService<AuthenticationStateProvider>() is { } anmeldung)
        {
            try
            {
                var zustand = await anmeldung.GetAuthenticationStateAsync();
                if (MockAnmeldung.AlsBenutzer(zustand.User) is { } ausBlazor)
                {
                    return ausBlazor;
                }
            }
            catch (InvalidOperationException)
            {
                // Außerhalb einer Blazor-Verbindung (z. B. Hintergrundjob) gibt es keinen Anmeldezustand.
            }
        }

        return MockAnmeldung.AlsBenutzer(http.HttpContext?.User) ?? Benutzer.Automatik;
    }
}

/// <summary>
/// Führt eine Aktion in einem eigenen, kurzlebigen Arbeitsbereich (eigener DbContext) aus,
/// behält aber den angemeldeten Benutzer für das Protokoll bei.
/// </summary>
internal sealed class Vorgang(IServiceScopeFactory bereiche, IAktuellerBenutzer benutzer)
{
    public async Task<T> AusfuehrenAsync<T>(Func<IServiceProvider, Task<T>> arbeit)
    {
        var wer = await benutzer.ErmittelnAsync();
        await using var bereich = bereiche.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = wer;
        return await arbeit(bereich.ServiceProvider);
    }

    public Task AusfuehrenAsync(Func<IServiceProvider, Task> arbeit)
        => AusfuehrenAsync<bool>(async d =>
        {
            await arbeit(d);
            return true;
        });

    public ValueTask<Benutzer> BenutzerAsync() => benutzer.ErmittelnAsync();
}
