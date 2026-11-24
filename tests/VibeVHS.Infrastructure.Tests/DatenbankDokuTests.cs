using System.Text;
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
        using var db = new VibeVhsDbContext(new DbContextOptionsBuilder<VibeVhsDbContext>().UseNpgsql("Host=modell;Database=modell").Options);
        var sb = new StringBuilder();
        sb.AppendLine("# Datenbank – Tabellenstrukturen");
        sb.AppendLine();
        sb.AppendLine("Automatisch erzeugt aus dem EF-Modell (`DatenbankDokuTests`, Zwecktexte in `DatenmodellBeschreibung.cs`), nicht von Hand ändern.");
        sb.AppendLine("PostgreSQL 17, angelegt beim ersten Start per `EnsureCreated` aus dem Modell (noch keine Migrationen, siehe docs/betrieb.md); die");
        sb.AppendLine("Schemaversion steht in der Tabelle `Schemainfo`. Speicherformate: Ids als `uuid` (UUIDv7, zeitlich sortiert), Kalendertage als `date`");
        sb.AppendLine("(offenes Ende 9999-12-31), Zeitpunkte (`DateTimeOffset`) und Termine als `timestamptz` in UTC (Millisekunden), Aufzählungen als Text.");
        sb.AppendLine("Namen sortieren per ICU-Collation `DIN5007_2` (de-DE, Telefonbuch). Gesucht wird über die SQL-Funktionen `suchtext`, `suchwoerter` und");
        sb.AppendLine("`namensuchtext` mit Trigramm-Indizes (pg_trgm). Keine Kopien: Namen, Anzeigetexte und Stände werden zur Laufzeit aus den Quellen");
        sb.AppendLine("gelesen. Keine JSON-Spalten: Listen stehen in eigenen Tabellen (je Eintrag eine Zeile).");
        sb.AppendLine();
        sb.AppendLine("**Bitemporale Versionstabellen** (Namen, Merkmale, Adressen, …): `GueltigVon`/`GueltigBis` = fachliche Zeit (halboffen);");
        sb.AppendLine("Wissenszeit, Person und Grund stehen im Änderungsvorgang (`ErfasstDurchVorgangId`, ersetzt durch `ErsetztDurchVorgangId` → Tabelle");
        sb.AppendLine("`Aenderungsvorgaenge`); `Erfassungsart` = Rolle der Zeile im Vorgang. „Einstufung“: F = führend, A = nur nach Anonymisierung");
        sb.AppendLine("(siehe `Spalteneinstufung.cs`).");
        sb.AppendLine();
        sb.AppendLine("**Lesbare Sichten:** Zu jeder Tabelle gibt es eine Sicht `v_<Tabelle>` (offenes Ende als NULL; bei Versionstabellen zusätzlich");
        sb.AppendLine("`ErfasstAm`, `ErfasstVon`, `Grund`, `ErsetztAm` aus dem Vorgang) – zum Prüfen im DB-Werkzeug.");
        sb.AppendLine();
        // Fest geschriebene Einleitung (bleibt bei jedem Neuerzeugen erhalten).
        sb.Append(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Pfad())!, "datenmodell-einleitung.md")).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        var modell = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(db).Model;
        var typen = modell.GetEntityTypes().Where(e => e.GetTableName() is not null)
            .GroupBy(e => e.GetTableName()!).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var fehlend = typen.Select(g => g.Key).Where(t => !DatenmodellBeschreibung.Tabellen.ContainsKey(t)).ToList();
        fehlend.ShouldBeEmpty("Tabellen ohne Zweck in DatenmodellBeschreibung.cs");
        var bereichVon = typen.ToDictionary(g => g.Key, g => DatenmodellBeschreibung.Tabellen[g.Key].Bereich);
        sb.AppendLine("## Übersicht");
        foreach (var bereich in DatenmodellBeschreibung.Bereiche)
        {
            var tabellen = typen.Where(g => bereichVon[g.Key] == bereich).ToList();
            sb.AppendLine();
            sb.AppendLine($"### {bereich}");
            sb.AppendLine();
            foreach (var g in tabellen)
            {
                sb.AppendLine($"- [{g.Key}](#{g.Key.ToLowerInvariant()}) – {DatenmodellBeschreibung.Tabellen[g.Key].Zweck}");
            }

            // ER-Diagramm des Bereichs: Tabellen mit Schlüssel- und Verweisspalten, Beziehungen aus den Fremdschlüsseln.
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine("erDiagram");
            var namen = tabellen.Select(g => g.Key).ToHashSet();
            var kanten = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var g in tabellen)
            {
                foreach (var fk in g.SelectMany(e => e.GetForeignKeys()))
                {
                    var ziel = fk.PrincipalEntityType.GetTableName();
                    if (ziel is null || ziel == g.Key && fk.IsOwnership)
                    {
                        continue;
                    }

                    var spalte = string.Join("+", fk.Properties.Select(x => x.GetColumnName()));
                    kanten.Add($"    {ziel} {(fk.IsUnique ? "||--o|" : "||--o{")} {g.Key} : \"{spalte}\"");
                    namen.Add(ziel);
                }
            }

            foreach (var g in typen.Where(g => namen.Contains(g.Key)))
            {
                var spalten = g.SelectMany(e => e.GetProperties()).Where(x => x.IsPrimaryKey() || x.IsForeignKey())
                    .GroupBy(x => x.GetColumnName()).Select(x => x.First()).OrderBy(x => x.IsPrimaryKey() ? 0 : 1).ThenBy(x => x.GetColumnName(), StringComparer.Ordinal);
                sb.AppendLine($"    {g.Key} {{");
                foreach (var x in spalten)
                {
                    sb.AppendLine($"        {(Nullable.GetUnderlyingType(x.ClrType) ?? x.ClrType).Name} {x.GetColumnName()}{(x.IsPrimaryKey() ? " PK" : x.IsForeignKey() ? " FK" : "")}");
                }

                sb.AppendLine("    }");
            }

            foreach (var k in kanten)
            {
                sb.AppendLine(k);
            }

            sb.AppendLine("```");
        }

        foreach (var g in typen.OrderBy(g => DatenmodellBeschreibung.Bereiche.ToList().IndexOf(bereichVon[g.Key])).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var tabelle = g.Key;
            var einstufung = Spalteneinstufung.Tabellen.GetValueOrDefault(tabelle);
            sb.AppendLine();
            sb.AppendLine($"## {tabelle}");
            sb.AppendLine();
            sb.AppendLine($"{DatenmodellBeschreibung.Tabellen[tabelle].Zweck} (Bereich: {bereichVon[tabelle]})");
            sb.AppendLine();
            sb.AppendLine($"Klasse: `{string.Join("`, `", g.Select(e => e.ClrType.FullName))}`");
            sb.AppendLine();
            sb.AppendLine("| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |");
            sb.AppendLine("|---|---|---|---|---|");
            var props = g.SelectMany(e => e.GetProperties()).GroupBy(p => p.GetColumnName()).Select(x => x.First())
                .OrderBy(p => p.IsPrimaryKey() ? 0 : 1).ThenBy(p => p.GetColumnName(), StringComparer.Ordinal);
            foreach (var p in props)
            {
                var typ = p.GetColumnType();
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
                    || (p.ClrType != typeof(string) && typ.StartsWith("text", StringComparison.Ordinal) && p.ClrType.IsClass))
                {
                    hinweis.Add("JSON");
                }

                sb.AppendLine($"| {p.GetColumnName()} | {typ} | {(p.IsNullable ? "" : "ja")} | {string.Join(", ", schluessel)} | {string.Join("; ", hinweis)} |");
            }

            var indizes = g.SelectMany(e => e.GetIndexes()).Select(i => $"`{i.GetDatabaseName()}` ({string.Join(", ", i.Properties.OfType<Microsoft.EntityFrameworkCore.Metadata.IProperty>().Select(x => x.GetColumnName() + (x.GetCollation() is { } c ? $" COLLATE {c}" : "")))}){(i.IsUnique ? ", eindeutig" : "")}{(i.GetFilter() is { } f ? $", nur {f}" : "")}")
                .Distinct().Order(StringComparer.Ordinal).ToList();
            if (indizes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Indizes: {string.Join("; ", indizes)}");
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
