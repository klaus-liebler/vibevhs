using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Personen;

/// <summary>Zeile der FTS5-Tabelle PersonSuche (nur lesend über EF; geschrieben per SQL).</summary>
public sealed class Suchindexeintrag
{
    public Guid PersonId { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Pflege und Abfrage des Volltextindex (SQLite FTS5). Indexiert werden normalisierte Texte
/// (siehe <see cref="Suchtext"/>), gesucht wird mit Präfix je Wort ("yil" findet "Yılmaz").
/// </summary>
public static class PersonenSuchindex
{
    public const string TabelleAnlegen =
        "CREATE VIRTUAL TABLE IF NOT EXISTS PersonSuche USING fts5(PersonId UNINDEXED, Text, tokenize = 'unicode61 remove_diacritics 2')";

    internal static async Task AktualisierenAsync(VibeVhsDbContext db, IReadOnlyList<Person> personen, CancellationToken abbruch)
    {
        foreach (var p in personen)
        {
            var id = Schluessel(p.Id);
            await db.Database.ExecuteSqlAsync($"DELETE FROM PersonSuche WHERE PersonId = {id}", abbruch);
            await db.Database.ExecuteSqlAsync($"INSERT INTO PersonSuche (PersonId, Text) VALUES ({id}, {p.SuchtextFuerIndex()})", abbruch);
        }
    }

    /// <summary>FTS5-Ausdruck: jedes Wort als Präfix, alle Wörter müssen vorkommen. <c>null</c> bei leerer Suche.</summary>
    public static string? Ausdruck(string? suche)
    {
        var woerter = Suchtext.Woerter(suche)
            .Select(w => w.Replace("\"", string.Empty, StringComparison.Ordinal))
            .Where(w => w.Length > 0)
            .ToList();
        return woerter.Count == 0 ? null : string.Join(' ', woerter.Select(w => $"\"{w}\"*"));
    }

    /// <summary>Ids der Treffer als Unterabfrage (komponierbar in weitere EF-Abfragen).</summary>
    internal static IQueryable<Guid> Treffer(VibeVhsDbContext db, string ausdruck)
        => db.Suchindex.FromSql($"SELECT PersonId, Text FROM PersonSuche WHERE PersonSuche MATCH {ausdruck}").Select(s => s.PersonId);

    /// <summary>Baut den Index neu auf, wenn er nicht zur Personentabelle passt (z. B. nach einer Migration).</summary>
    public static async Task AufbauenFallsNoetigAsync(VibeVhsDbContext db, ILogger log, CancellationToken abbruch = default)
    {
        // Nach einer Migration fehlen Zwischenstände (Anzeigename, Sortierschlüssel) für vorhandene Personen.
        var ohneZwischenstand = await db.Personen.Where(p => p.Anzeigename == "").Select(p => p.Id).ToListAsync(abbruch);
        if (ohneZwischenstand.Count > 0)
        {
            log.LogInformation("Zwischenstand für {Anzahl} Personen wird nachgeführt", ohneZwischenstand.Count);
            foreach (var block in ohneZwischenstand.Chunk(200))
            {
                var geladen = await db.Personen.Where(p => block.Contains(p.Id)).ToListAsync(abbruch);
                geladen.ForEach(p => p.ZwischenstandNachfuehren(DateTimeOffset.Now));
                await db.SaveChangesAsync(abbruch);
                db.ChangeTracker.Clear();
            }
        }

        var imIndex = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM PersonSuche").SingleAsync(abbruch);
        var personen = await db.Personen.CountAsync(abbruch);
        if (imIndex == personen)
        {
            return;
        }

        log.LogInformation("Suchindex wird neu aufgebaut ({Index} von {Personen} Personen indexiert)", imIndex, personen);
        await db.Database.ExecuteSqlAsync($"DELETE FROM PersonSuche", abbruch);
        var ids = await db.Personen.Select(p => p.Id).ToListAsync(abbruch);
        foreach (var block in ids.Chunk(200))
        {
            var geladen = await db.Personen.AsNoTracking().AsSplitQuery().Where(p => block.Contains(p.Id)).ToListAsync(abbruch);
            await using var tx = await db.Database.BeginTransactionAsync(abbruch);
            await AktualisierenAsync(db, geladen, abbruch);
            await tx.CommitAsync(abbruch);
        }
    }

    /// <summary>Gleiches Textformat wie EF Core für Guid-Spalten in SQLite.</summary>
    internal static string Schluessel(Guid id) => id.ToString().ToUpperInvariant();
}
