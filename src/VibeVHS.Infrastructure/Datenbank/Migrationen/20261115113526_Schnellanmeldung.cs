using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115113526_Schnellanmeldung : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Schnellanmeldungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                BelegungId = table.Column<Guid>(type: "TEXT", nullable: false),
                Kursnummer = table.Column<string>(type: "TEXT", nullable: false),
                Weg = table.Column<string>(type: "TEXT", nullable: false),
                StornoHash = table.Column<string>(type: "TEXT", nullable: false),
                Einwilligungen = table.Column<string>(type: "TEXT", nullable: false),
                Zeitpunkt = table.Column<long>(type: "INTEGER", nullable: false),
                KostenfreiBis = table.Column<long>(type: "INTEGER", nullable: false),
                StorniertAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Schnellanmeldungen", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Schnellanmeldungen_PersonId",
            table: "Schnellanmeldungen",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Schnellanmeldungen_StornoHash",
            table: "Schnellanmeldungen",
            column: "StornoHash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Schnellanmeldungen");
    }
}
