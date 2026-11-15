// Testdaten-Generator für VibeVHS (fiktive Personen mit Rollen, Status, Fachbereichen, Kontaktdaten, Dubletten).
// Aufruf:  dotnet run --project tools/Testdaten -- [Anzahl] [Datenbankdatei]
// Standard: 3000 Personen in src/VibeVHS.Web/data/vibevhs.db. Die Anwendung erzeugt diese Daten in der
// Entwicklungsumgebung beim ersten Start auch selbst (Demodaten:Massendaten).

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VibeVHS.Adapters.Mocks.Testdaten;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Infrastructure;
using VibeVHS.Infrastructure.Datenbank;

var anzahl = args.Length > 0 && int.TryParse(args[0], out var a) ? a : 3000;
var datei = args.Length > 1 ? args[1] : Path.Combine("src", "VibeVHS.Web", "data", "vibevhs.db");

var konfiguration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:VibeVHS"] = $"Data Source={Path.GetFullPath(datei)}",
    })
    .Build();

var dienste = new ServiceCollection()
    .AddLogging(l => l.AddSimpleConsole().AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning))
    .AddSingleton(TimeProvider.System)
    .AddSingleton<IAktuellerBenutzer, Testdatenbenutzer>()
    .AddVibeVhsInfrastruktur(konfiguration, Directory.GetCurrentDirectory())
    .BuildServiceProvider();

await DatenbankInitialisierung.InitialisierenAsync(dienste);

var uhr = Stopwatch.StartNew();
await using (var bereich = dienste.CreateAsyncScope())
{
    var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
    var daten = new Massendaten().Erzeugen(anzahl, TimeProvider.System.GetLocalNow());
    db.Organisationen.AddRange(daten.Organisationen);
    db.Personen.AddRange(daten.Personen);
    await bereich.ServiceProvider.GetRequiredService<IAuditProtokoll>()
        .ProtokollierenAsync("Testdaten.Erzeugt", $"Testdaten-Generator: {daten.Personen.Count} Personen ({daten.GeplanteDubletten} geplante Dubletten), {daten.Organisationen.Count} Organisationen");
    await db.SaveChangesAsync();
    Console.WriteLine($"{daten.Personen.Count} Testpersonen in {Path.GetFullPath(datei)} angelegt ({uhr.ElapsedMilliseconds} ms).");
}

internal sealed class Testdatenbenutzer : IAktuellerBenutzer
{
    public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default)
        => ValueTask.FromResult(new Benutzer("testdaten", "Testdaten-Generator", null));
}
