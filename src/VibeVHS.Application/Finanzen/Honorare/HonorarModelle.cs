using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;

namespace VibeVHS.Application.Finanzen.Honorare;

/// <summary>Alle registrierten Honorarmodelle (eingebaut und aus Plugins), über die Kennung abrufbar.</summary>
public sealed class HonorarModelle(IEnumerable<IHonorarModell> modelle)
{
    private readonly IReadOnlyList<IHonorarModell> _alle = modelle.GroupBy(m => m.Kennung).Select(g => g.Last()).OrderBy(m => m.Anzeigename, StringComparer.CurrentCulture).ToList();

    public IReadOnlyList<IHonorarModell> Alle => _alle;

    public IHonorarModell? Finden(string kennung) => _alle.FirstOrDefault(m => m.Kennung == kennung);

    public IHonorarModell Holen(string kennung)
        => Finden(kennung) ?? throw new InvalidOperationException($"Das Honorarmodell „{kennung}“ ist nicht installiert (Plugin fehlt?).");
}

/// <summary>
/// Lädt zusätzliche Honorarmodelle aus einem Plugin-Ordner (je Plugin eine DLL). Jede Assembly bekommt einen eigenen
/// <see cref="AssemblyLoadContext"/>; die Schnittstelle kommt aus der Anwendung, damit die Typen zusammenpassen.
/// </summary>
public static class HonorarPlugins
{
    public static IReadOnlyList<string> Laden(IServiceCollection dienste, string ordner)
    {
        ArgumentNullException.ThrowIfNull(dienste);
        var geladen = new List<string>();
        if (!Directory.Exists(ordner))
        {
            return geladen;
        }

        foreach (var datei in Directory.EnumerateFiles(ordner, "*.dll"))
        {
            var kontext = new AssemblyLoadContext($"Honorarplugin:{Path.GetFileNameWithoutExtension(datei)}");
            Assembly assembly;
            try
            {
                assembly = kontext.LoadFromAssemblyPath(Path.GetFullPath(datei));
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            foreach (var typ in assembly.GetExportedTypes().Where(t => typeof(IHonorarModell).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }
                                                                       && t.GetConstructor(Type.EmptyTypes) is not null))
            {
                var modell = (IHonorarModell)Activator.CreateInstance(typ)!;
                dienste.AddSingleton(modell);
                geladen.Add($"{modell.Kennung} {modell.Version} ({Path.GetFileName(datei)})");
            }
        }

        return geladen;
    }
}
