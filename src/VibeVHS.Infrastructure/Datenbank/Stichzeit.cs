using Microsoft.EntityFrameworkCore.Diagnostics;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>Setzt beim Laden einer Person deren Bezugszeitpunkt auf „jetzt“ der Anwendungsuhr (für die berechneten Eigenschaften).</summary>
/// Zustandslos (eine Instanz für alle Kontexte, sonst baut EF je Kontext einen eigenen internen Dienstanbieter); die Uhr kommt vom Kontext.
internal sealed class StichzeitBeimLaden : IMaterializationInterceptor
{
    public static readonly StichzeitBeimLaden Instanz = new();

    private StichzeitBeimLaden()
    {
    }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is Person p)
        {
            p.StichzeitSetzen(((VibeVhsDbContext)materializationData.Context).Uhr.GetLocalNow());
        }

        return entity;
    }
}
