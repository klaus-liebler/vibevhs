using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116193609_VorlagenAnredestil : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Anredestil",
            table: "Vorlagen",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "Foermlich");

        // Gespeicherte Vorlagen mit der bisherigen Anrede auf den Platzhalter {Anrede} umstellen (eigene Texte bleiben sonst unverändert).
        migrationBuilder.Sql("UPDATE \"Vorlagen\" SET \"Text\" = REPLACE(\"Text\", 'Guten Tag {Name},', '{Anrede},') WHERE \"Text\" LIKE 'Guten Tag {Name},%';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"Vorlagen\" SET \"Text\" = REPLACE(\"Text\", '{Anrede},', 'Guten Tag {Name},') WHERE \"Text\" LIKE '{Anrede},%';");
        migrationBuilder.DropColumn(
            name: "Anredestil",
            table: "Vorlagen");
    }
}
