using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115122027_Abwesenheit : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Abwesenheiten",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Login = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Von = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Bis = table.Column<DateOnly>(type: "TEXT", nullable: false),
                VertretungLogin = table.Column<string>(type: "TEXT", nullable: true),
                VertretungName = table.Column<string>(type: "TEXT", nullable: true),
                Grund = table.Column<string>(type: "TEXT", nullable: true),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErfasstVon = table.Column<string>(type: "TEXT", nullable: false),
                BeendetAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Abwesenheiten", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Abwesenheiten_Login",
            table: "Abwesenheiten",
            column: "Login");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Abwesenheiten");
    }
}
