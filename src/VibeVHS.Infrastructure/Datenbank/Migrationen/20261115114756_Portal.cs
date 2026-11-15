using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115114756_Portal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Dateien",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Typ = table.Column<string>(type: "TEXT", nullable: false),
                Groesse = table.Column<long>(type: "INTEGER", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                Inhalt = table.Column<byte[]>(type: "BLOB", nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Dateien", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Kursunterlagen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                DateiId = table.Column<Guid>(type: "TEXT", nullable: false),
                Titel = table.Column<string>(type: "TEXT", nullable: false),
                HochgeladenVon = table.Column<Guid>(type: "TEXT", nullable: false),
                TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                HochgeladenAm = table.Column<long>(type: "INTEGER", nullable: false),
                GueltigBis = table.Column<long>(type: "INTEGER", nullable: false),
                ZurueckgezogenAm = table.Column<long>(type: "INTEGER", nullable: true),
                Abrufe = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Kursunterlagen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Portalanliegen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Titel = table.Column<string>(type: "TEXT", nullable: false),
                Text = table.Column<string>(type: "TEXT", nullable: false),
                Daten = table.Column<string>(type: "TEXT", nullable: false),
                BezugId = table.Column<Guid>(type: "TEXT", nullable: true),
                Fachbereich = table.Column<int>(type: "INTEGER", nullable: true),
                DateiId = table.Column<Guid>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                EingereichtAm = table.Column<long>(type: "INTEGER", nullable: false),
                EntschiedenAm = table.Column<long>(type: "INTEGER", nullable: true),
                EntschiedenVon = table.Column<string>(type: "TEXT", nullable: true),
                Vermerk = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Portalanliegen", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Kursunterlagen_KursId",
            table: "Kursunterlagen",
            column: "KursId");

        migrationBuilder.CreateIndex(
            name: "IX_Kursunterlagen_TokenHash",
            table: "Kursunterlagen",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Portalanliegen_PersonId",
            table: "Portalanliegen",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Portalanliegen_Status",
            table: "Portalanliegen",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Dateien");

        migrationBuilder.DropTable(
            name: "Kursunterlagen");

        migrationBuilder.DropTable(
            name: "Portalanliegen");
    }
}
