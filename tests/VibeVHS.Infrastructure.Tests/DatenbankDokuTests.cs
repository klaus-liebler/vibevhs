using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>
/// Erzeugt docs/datenmodell.md aus dem EF-Modell (alle Tabellen, Spalten, Typen, Schlüssel, Verweise, Indizes, Einstufung) und prüft,
/// dass die Datei aktuell ist. Neu schreiben: Umgebungsvariable VIBEVHS_DOKU_SCHREIBEN=1 setzen und den Test ausführen.
/// </summary>
public sealed class DatenbankDokuTests
{
    private static string Erzeugen()
    {
        using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite(new SqliteConnection("Data Source=:memory:")).Options);
        var sb = new StringBuilder();
        sb.AppendLine("# Datenbank – Tabellenstrukturen");
        sb.AppendLine();
        sb.AppendLine("Automatisch erzeugt aus dem EF-Modell (`DatenbankDokuTests`), nicht von Hand ändern. SQLite. Speicherformate: Ids als 16-Byte-BLOB");
        sb.AppendLine("(UUIDv7, Big Endian), Kalendertage als Ganzzahl JJJJMMTT, Zeitpunkte (`DateTimeOffset`) und Termine als UTC-Millisekunden seit 1970,");
        sb.AppendLine("Aufzählungen als Text. Namen sortieren per Collation `DIN5007_2` (siehe unten).");
        sb.AppendLine();
        sb.AppendLine("**Bitemporale Versionstabellen** (Namen, Merkmale, Adressen, … – Spalten `GueltigVon`/`GueltigBis` = fachliche Zeit, halboffen;");
        sb.AppendLine("`ErfasstAm`/`ErsetztAm` = Wissenszeit, `ErsetztAm` = durch neueres Wissen ersetzt (Änderung ab Datum oder Korrektur); `Erfassungsart`, `ErfasstVon`, `Grund` = Änderungsvorgang).");
        sb.AppendLine("„Einstufung“: F = führend, C = Lese-Cache (Kopie), A = nur nach Anonymisierung (siehe `Spalteneinstufung.cs`). Spalten mit „JSON“ enthalten eine");
        sb.AppendLine("serialisierte Liste bzw. Struktur (Kandidaten für eigene Tabellen).");
        sb.AppendLine();
        // Fest geschriebene Einleitung (bleibt bei jedem Neuerzeugen erhalten).
        sb.Append(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Pfad())!, "datenmodell-einleitung.md")).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        var typen = db.Model.GetEntityTypes().Where(e => e.GetTableName() is not null && !e.IsOwned())
            .GroupBy(e => e.GetTableName()!).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        sb.AppendLine("## Übersicht");
        sb.AppendLine();
        foreach (var g in typen)
        {
            sb.AppendLine($"- [{g.Key}](#{g.Key.ToLowerInvariant()}) – {string.Join(", ", g.Select(e => e.ClrType.Name))}");
        }

        foreach (var g in typen)
        {
            var tabelle = g.Key;
            var einstufung = Spalteneinstufung.Tabellen.GetValueOrDefault(tabelle);
            sb.AppendLine();
            sb.AppendLine($"## {tabelle}");
            sb.AppendLine();
            sb.AppendLine($"Klasse: `{string.Join("`, `", g.Select(e => e.ClrType.FullName))}`");
            sb.AppendLine();
            sb.AppendLine("| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |");
            sb.AppendLine("|---|---|---|---|---|");
            var props = g.SelectMany(e => e.GetProperties()).GroupBy(p => p.GetColumnName()).Select(x => x.First())
                .OrderBy(p => p.IsPrimaryKey() ? 0 : 1).ThenBy(p => p.GetColumnName(), StringComparer.Ordinal);
            foreach (var p in props)
            {
                var typ = p.GetColumnType() + (p.GetMaxLength() is { } m ? $"({m})" : "");
                var schluessel = new List<string>();
                if (p.IsPrimaryKey())
                {
                    schluessel.Add("PK");
                }

                foreach (var fk in p.GetContainingForeignKeys())
                {
                    schluessel.Add($"→ {fk.PrincipalEntityType.GetTableName()}");
                }

                if (p.GetContainingIndexes().Any(i => i.IsUnique))
                {
                    schluessel.Add("eindeutig");
                }
                else if (p.GetContainingIndexes().Any())
                {
                    schluessel.Add("Index");
                }

                var hinweis = new List<string>();
                if (einstufung?.GetValueOrDefault(p.GetColumnName()) is { } e && e.Bedeutung is not null)
                {
                    hinweis.Add($"{(e.Art switch { Spaltenart.Cache => "C", Spaltenart.Anonymisierungsrest => "A", _ => "F" })}: {e.Bedeutung}");
                }

                var konverter = p.GetValueConverter()?.GetType().Name ?? "";
                if (konverter.Contains("Json", StringComparison.OrdinalIgnoreCase) || p.IsPrimitiveCollection
                    || (p.ClrType != typeof(string) && typ.StartsWith("TEXT", StringComparison.Ordinal) && p.ClrType.IsClass))
                {
                    hinweis.Add("JSON");
                }

                sb.AppendLine($"| {p.GetColumnName()} | {typ} | {(p.IsNullable ? "" : "ja")} | {string.Join(", ", schluessel)} | {string.Join("; ", hinweis)} |");
            }
        }

        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Pfad([System.Runtime.CompilerServices.CallerFilePath] string quelle = "")
    {
        var verzeichnis = new DirectoryInfo(Path.GetDirectoryName(quelle)!);
        while (verzeichnis is not null && !File.Exists(Path.Combine(verzeichnis.FullName, "VibeVHS.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return Path.Combine(verzeichnis?.FullName ?? throw new InvalidOperationException("Repository nicht gefunden"), "docs", "datenmodell.md");
    }

    [Fact]
    public void Dokumentation_ist_aktuell()
    {
        var neu = Erzeugen();
        var pfad = Pfad();
        if (Environment.GetEnvironmentVariable("VIBEVHS_DOKU_SCHREIBEN") == "1")
        {
            File.WriteAllText(pfad, neu, new UTF8Encoding(false));
        }

        File.Exists(pfad).ShouldBeTrue("docs/datenmodell.md fehlt – mit VIBEVHS_DOKU_SCHREIBEN=1 erzeugen");
        File.ReadAllText(pfad).Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(neu, "docs/datenmodell.md ist veraltet – mit VIBEVHS_DOKU_SCHREIBEN=1 neu erzeugen");
    }
}
