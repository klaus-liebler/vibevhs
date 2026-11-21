using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>
/// Eigene SQLite-Erweiterungen der Anwendung: Collation <c>DIN5007_2</c> (Sortierung von Namen nach DIN 5007-2) und Funktion
/// <c>suchtext(x)</c> (normalisierte Schreibweise für die Suche). Microsoft.Data.Sqlite
/// merkt sich die Registrierung an der Verbindung und wendet sie bei jedem Öffnen an – auch bei Verbindungen, die von außen geöffnet
/// übergeben werden (Tests, Sicherung). Werkzeuge ohne diese Collation (z. B. ein DB-Browser) können Tabellen mit ihr zwar lesen, aber
/// nicht danach sortieren und keine Namensversionen schreiben (der Index braucht die Collation).
/// </summary>
public static class SqliteErweiterungen
{
    public static bool Registrieren(DbContextOptions optionen)
    {
        if (optionen.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault()?.Connection is SqliteConnection verbindung)
        {
            Registrieren(verbindung);
        }

        return true;
    }

    public static void Registrieren(SqliteConnection verbindung)
    {
        verbindung.CreateCollation(Din5007.Collation, (a, b) => Din5007.Vergleichen(a, b));
        // suchtext(x): gleiche Normalisierung wie Suchbegriffe (Groß/klein, Akzente, ä/ae, ß/ss …) – für die Suche direkt in den Versionen.
        verbindung.CreateFunction("suchtext", (string? text) => Suchtext.Normalisieren(text), isDeterministic: true);
    }
}

/// <summary>Registriert die Erweiterungen an jeder Verbindung, die EF selbst anlegt und öffnet (Verbindungszeichenfolge).</summary>
internal sealed class SqliteErweiterungenBeimOeffnen : Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor
{
    public static readonly SqliteErweiterungenBeimOeffnen Instanz = new();

    public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult ConnectionOpening(System.Data.Common.DbConnection connection,
        Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result)
    {
        if (connection is SqliteConnection s)
        {
            SqliteErweiterungen.Registrieren(s);
        }

        return result;
    }

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult> ConnectionOpeningAsync(System.Data.Common.DbConnection connection,
        Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult result,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
}
