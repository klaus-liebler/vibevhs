using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115080227_Dozierendenmerkmale : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Bezug",
            table: "Merkmale",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "Ort");

        migrationBuilder.AddColumn<bool>(
            name: "Gesetzlich",
            table: "Merkmale",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "VorlaufTage",
            table: "Merkmale",
            type: "INTEGER",
            nullable: false,
            defaultValue: 60);

        migrationBuilder.AddColumn<string>(
            name: "Werte",
            table: "Merkmale",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "BenoetigteDozierendenmerkmale",
            table: "Kurse",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "BenoetigteDozierendenmerkmale",
            table: "Kursarten",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.CreateTable(
            name: "PersonQualifikationen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Schluessel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Bezeichnung = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                WertText = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Nachweis = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                AusstellendeStelle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                GueltigVon = table.Column<DateOnly>(type: "TEXT", nullable: false),
                GueltigBis = table.Column<DateOnly>(type: "TEXT", nullable: true),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErfasstVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Erfassungsart = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                ErsetztAm = table.Column<long>(type: "INTEGER", nullable: true),
                Grund = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PersonQualifikationen", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonQualifikationen_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Qualifikationsausnahmen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Schluessel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Begruendung = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                ErteiltVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                ErteiltAm = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Qualifikationsausnahmen", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PersonQualifikationen_PersonId_GueltigVon",
            table: "PersonQualifikationen",
            columns: new[] { "PersonId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_Qualifikationsausnahmen_KursId",
            table: "Qualifikationsausnahmen",
            column: "KursId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PersonQualifikationen");

        migrationBuilder.DropTable(
            name: "Qualifikationsausnahmen");

        migrationBuilder.DropColumn(
            name: "Bezug",
            table: "Merkmale");

        migrationBuilder.DropColumn(
            name: "Gesetzlich",
            table: "Merkmale");

        migrationBuilder.DropColumn(
            name: "VorlaufTage",
            table: "Merkmale");

        migrationBuilder.DropColumn(
            name: "Werte",
            table: "Merkmale");

        migrationBuilder.DropColumn(
            name: "BenoetigteDozierendenmerkmale",
            table: "Kurse");

        migrationBuilder.DropColumn(
            name: "BenoetigteDozierendenmerkmale",
            table: "Kursarten");
    }
}
