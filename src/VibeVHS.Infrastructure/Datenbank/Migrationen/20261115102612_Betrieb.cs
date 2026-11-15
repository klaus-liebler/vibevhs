using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115102612_Betrieb : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Importlaeufe",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Datei = table.Column<string>(type: "TEXT", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                Zeilen = table.Column<int>(type: "INTEGER", nullable: false),
                Angelegt = table.Column<int>(type: "INTEGER", nullable: false),
                Dubletten = table.Column<int>(type: "INTEGER", nullable: false),
                Fehler = table.Column<int>(type: "INTEGER", nullable: false),
                Zuordnungen = table.Column<string>(type: "TEXT", nullable: false),
                Am = table.Column<long>(type: "INTEGER", nullable: false),
                Von = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Importlaeufe", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Importlaeufe_Sha256",
            table: "Importlaeufe",
            column: "Sha256");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Importlaeufe");
    }
}
