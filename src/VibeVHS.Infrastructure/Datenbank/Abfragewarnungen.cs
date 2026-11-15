using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>
/// EF-Warnungen zu nicht deterministischen Abfragen: Skip/Take bzw. First ohne OrderBy liefern je nach Ausführungsplan
/// unterschiedliche Zeilen (Blättern springt, Seeds treffen zufällige Datensätze). In Entwicklung und Tests brechen solche
/// Abfragen ab, damit sie sofort auffallen; im Betrieb werden sie nur protokolliert.
/// </summary>
public static class Abfragewarnungen
{
    public static readonly EventId[] Nichtdeterministisch =
    [
        CoreEventId.RowLimitingOperationWithoutOrderByWarning,
        CoreEventId.FirstWithoutOrderByAndFilterWarning,
    ];

    public static T Konfigurieren<T>(this T optionen, bool streng)
        where T : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(optionen);
        return (T)optionen.ConfigureWarnings(w =>
        {
            if (streng)
            {
                w.Throw(Nichtdeterministisch);
            }
            else
            {
                w.Log(Nichtdeterministisch);
            }
        });
    }
}
