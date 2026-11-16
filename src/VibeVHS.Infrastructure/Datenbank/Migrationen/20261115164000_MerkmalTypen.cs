using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115164000_MerkmalTypen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Einheit",
            table: "Merkmale",
            type: "TEXT",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InaktiveWerte",
            table: "Merkmale",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<int>(
            name: "Max",
            table: "Merkmale",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Min",
            table: "Merkmale",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Typ",
            table: "Merkmale",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "JaNein");

        // Bestehende Merkmale: mit festgelegten Werten → Auswahl, sonst Ja/Nein (bisheriges Verhalten).
        migrationBuilder.Sql("UPDATE Merkmale SET Typ = CASE WHEN Werte IS NOT NULL AND Werte <> '[]' THEN 'Auswahl' ELSE 'JaNein' END;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Einheit",
            table: "Merkmale");

        migrationBuilder.DropColumn(
            name: "InaktiveWerte",
            table: "Merkmale");

        migrationBuilder.DropColumn(
            name: "Max",
            table: "Merkmale");

        migrationBuilder.DropColumn(
            name: "Min",
            table: "Merkmale");

        migrationBuilder.DropColumn(
            name: "Typ",
            table: "Merkmale");
    }
}
