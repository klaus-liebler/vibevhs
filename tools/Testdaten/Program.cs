// Testdaten-Generator für VibeVHS.
// Aufruf:  dotnet run --project tools/Testdaten -- [Anzahl] [Datenbankdatei]
// Standard: 500 Personen in src/VibeVHS.Web/data/vibevhs.db (die Datenbank der lokal gestarteten Anwendung).

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Infrastructure;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Testdaten;

var anzahl = args.Length > 0 && int.TryParse(args[0], out var a) ? a : 500;
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

await using (var bereich = dienste.CreateAsyncScope())
{
    var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
    var jetzt = TimeProvider.System.GetLocalNow();
    var personen = new PersonenGenerator().Erzeugen(anzahl, jetzt);
    db.Personen.AddRange(personen);
    await bereich.ServiceProvider.GetRequiredService<IAuditProtokoll>()
        .ProtokollierenAsync("Testdaten.Erzeugt", $"Testdaten-Generator: {anzahl} Personen angelegt");
    await db.SaveChangesAsync();
}

Console.WriteLine($"{anzahl} Testpersonen in {Path.GetFullPath(datei)} angelegt.");

internal sealed class Testdatenbenutzer : IAktuellerBenutzer
{
    public ValueTask<Benutzer> ErmittelnAsync(CancellationToken abbruch = default)
        => ValueTask.FromResult(new Benutzer("testdaten", "Testdaten-Generator", null));
}
