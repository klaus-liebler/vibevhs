using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115060605_Stammdaten : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Benutzerkennung",
            table: "Personen",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Rollen",
            table: "Personen",
            type: "INTEGER",
            maxLength: 40,
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "Titel",
            table: "Personen",
            type: "TEXT",
            maxLength: 50,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Benutzerkonten",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Login = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Funktion = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                Rollen = table.Column<string>(type: "TEXT", nullable: false),
                Fachbereiche = table.Column<string>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                Hinweis = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                Aktiv = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Benutzerkonten", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Einrichtung",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Kurzname = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                Orte = table.Column<string>(type: "TEXT", nullable: false),
                Bundesland = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Einrichtung", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Fachbereiche",
            columns: table => new
            {
                Nr = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Fachbereiche", x => x.Nr);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Personen_Benutzerkennung",
            table: "Personen",
            column: "Benutzerkennung",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Benutzerkonten_Login",
            table: "Benutzerkonten",
            column: "Login",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Benutzerkonten");

        migrationBuilder.DropTable(
            name: "Einrichtung");

        migrationBuilder.DropTable(
            name: "Fachbereiche");

        migrationBuilder.DropIndex(
            name: "IX_Personen_Benutzerkennung",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Benutzerkennung",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Rollen",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Titel",
            table: "Personen");
    }
}
