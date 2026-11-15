using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115061341_Kontaktdaten : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ErfasstVon",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            defaultValue: "System");

        migrationBuilder.AddColumn<string>(
            name: "Erfassungsart",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 40,
            nullable: false,
            defaultValue: "AenderungAbDatum");

        migrationBuilder.CreateTable(
            name: "PersonAdressen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AdressId = table.Column<Guid>(type: "TEXT", nullable: false),
                Zusatz = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Strasse = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Postleitzahl = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Ort = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Land = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
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
                table.PrimaryKey("PK_PersonAdressen", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonAdressen_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PersonAdressKennzeichen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                AdressId = table.Column<Guid>(type: "TEXT", nullable: false),
                AdressAnzeige = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
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
                table.PrimaryKey("PK_PersonAdressKennzeichen", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonAdressKennzeichen_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PersonEMails",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AdressId = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Adresse = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                Bevorzugt = table.Column<bool>(type: "INTEGER", nullable: false),
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
                table.PrimaryKey("PK_PersonEMails", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonEMails_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PersonMerkmale",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Inhalt = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
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
                table.PrimaryKey("PK_PersonMerkmale", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonMerkmale_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PersonRufnummern",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                NummerId = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Nummer = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                Dienste = table.Column<int>(type: "INTEGER", maxLength: 40, nullable: false),
                Einwilligung = table.Column<bool>(type: "INTEGER", nullable: false),
                Bevorzugt = table.Column<bool>(type: "INTEGER", nullable: false),
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
                table.PrimaryKey("PK_PersonRufnummern", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonRufnummern_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PersonAdressen_AdressId",
            table: "PersonAdressen",
            column: "AdressId");

        migrationBuilder.CreateIndex(
            name: "IX_PersonAdressen_PersonId_GueltigVon",
            table: "PersonAdressen",
            columns: new[] { "PersonId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_PersonAdressKennzeichen_PersonId_GueltigVon",
            table: "PersonAdressKennzeichen",
            columns: new[] { "PersonId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_PersonEMails_AdressId",
            table: "PersonEMails",
            column: "AdressId");

        migrationBuilder.CreateIndex(
            name: "IX_PersonEMails_PersonId_GueltigVon",
            table: "PersonEMails",
            columns: new[] { "PersonId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_PersonMerkmale_PersonId_GueltigVon",
            table: "PersonMerkmale",
            columns: new[] { "PersonId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_PersonRufnummern_NummerId",
            table: "PersonRufnummern",
            column: "NummerId");

        migrationBuilder.CreateIndex(
            name: "IX_PersonRufnummern_PersonId_GueltigVon",
            table: "PersonRufnummern",
            columns: new[] { "PersonId", "GueltigVon" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PersonAdressen");

        migrationBuilder.DropTable(
            name: "PersonAdressKennzeichen");

        migrationBuilder.DropTable(
            name: "PersonEMails");

        migrationBuilder.DropTable(
            name: "PersonMerkmale");

        migrationBuilder.DropTable(
            name: "PersonRufnummern");

        migrationBuilder.DropColumn(
            name: "ErfasstVon",
            table: "PersonNamen");

        migrationBuilder.DropColumn(
            name: "Erfassungsart",
            table: "PersonNamen");
    }
}
