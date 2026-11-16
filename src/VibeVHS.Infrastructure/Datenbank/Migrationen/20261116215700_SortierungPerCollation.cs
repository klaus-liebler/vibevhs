using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116215700_SortierungPerCollation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PersonNamen_Nachname",
            table: "PersonNamen");

        migrationBuilder.DropIndex(
            name: "IX_PersonNamen_Sortierschluessel",
            table: "PersonNamen");

        migrationBuilder.DropColumn(
            name: "Sortierschluessel",
            table: "PersonNamen");

        migrationBuilder.AlterColumn<string>(
            name: "Vorname",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            collation: "DIN5007_2",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200);

        migrationBuilder.AlterColumn<string>(
            name: "Nachname",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            collation: "DIN5007_2",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200);

        migrationBuilder.CreateIndex(
            name: "IX_PersonNamen_Sortierung",
            table: "PersonNamen",
            columns: new[] { "Nachname", "Vorname" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PersonNamen_Sortierung",
            table: "PersonNamen");

        migrationBuilder.AlterColumn<string>(
            name: "Vorname",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldCollation: "DIN5007_2");

        migrationBuilder.AlterColumn<string>(
            name: "Nachname",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldCollation: "DIN5007_2");

        migrationBuilder.AddColumn<string>(
            name: "Sortierschluessel",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 400,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_PersonNamen_Nachname",
            table: "PersonNamen",
            column: "Nachname");

        migrationBuilder.CreateIndex(
            name: "IX_PersonNamen_Sortierschluessel",
            table: "PersonNamen",
            column: "Sortierschluessel");
    }
}
