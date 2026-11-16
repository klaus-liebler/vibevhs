using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116220733_SucheOhneIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Die von der Anwendung gepflegte FTS5-Suchtabelle (eine Kopie) entfällt: gesucht wird direkt in den Versionstabellen.
        migrationBuilder.Sql("DROP TABLE IF EXISTS PersonSuche;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE VIRTUAL TABLE IF NOT EXISTS PersonSuche USING fts5(PersonId UNINDEXED, Text, tokenize = 'unicode61 remove_diacritics 2')");
    }
}
