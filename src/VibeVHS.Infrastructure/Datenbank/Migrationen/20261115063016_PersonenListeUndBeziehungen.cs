using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115063016_PersonenListeUndBeziehungen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Anzeigename",
            table: "Personen",
            type: "TEXT",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Fachbereiche",
            table: "Personen",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "Sortiername",
            table: "Personen",
            type: "TEXT",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Sortierschluessel",
            table: "Personen",
            type: "TEXT",
            maxLength: 340,
            nullable: false,
            defaultValue: "");

        // Vorhandene Zeilen bekommen einen eindeutigen Übergangsschlüssel; der App-Start berechnet ihn neu.
        migrationBuilder.Sql("UPDATE Personen SET Sortierschluessel = Id;");

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "Personen",
            type: "TEXT",
            maxLength: 30,
            nullable: false,
            defaultValue: "Aktiv");

        migrationBuilder.AddColumn<string>(
            name: "Wohnort",
            table: "Personen",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ZusammengefuehrtIn",
            table: "Personen",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "DublettenAusschluesse",
            columns: table => new
            {
                PersonA = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonB = table.Column<Guid>(type: "TEXT", nullable: false),
                Am = table.Column<long>(type: "INTEGER", nullable: false),
                Von = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DublettenAusschluesse", x => new { x.PersonA, x.PersonB });
            });

        migrationBuilder.CreateTable(
            name: "Organisationen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Ort = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Organisationen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PersonEinwilligungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Kanal = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Zweck = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Erteilt = table.Column<bool>(type: "INTEGER", nullable: false),
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
                table.PrimaryKey("PK_PersonEinwilligungen", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonEinwilligungen_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PersonOrganisationen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                OrganisationName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Funktion = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
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
                table.PrimaryKey("PK_PersonOrganisationen", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonOrganisationen_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PersonVollmachten",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                VollmachtId = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                BevollmaechtigterId = table.Column<Guid>(type: "TEXT", nullable: false),
                BevollmaechtigterName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Nachweis = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
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
                table.PrimaryKey("PK_PersonVollmachten", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonVollmachten_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Personen_Geburtsdatum",
            table: "Personen",
            column: "Geburtsdatum");

        migrationBuilder.CreateIndex(
            name: "IX_Personen_Sortierschluessel",
            table: "Personen",
            column: "Sortierschluessel",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Personen_Status",
            table: "Personen",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_PersonEinwilligungen_PersonId_GueltigVon",
            table: "PersonEinwilligungen",
            columns: new[] { "PersonId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_PersonOrganisationen_OrganisationId",
            table: "PersonOrganisationen",
            column: "OrganisationId");

        migrationBuilder.CreateIndex(
            name: "IX_PersonOrganisationen_PersonId_GueltigVon",
            table: "PersonOrganisationen",
            columns: new[] { "PersonId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_PersonVollmachten_BevollmaechtigterId",
            table: "PersonVollmachten",
            column: "BevollmaechtigterId");

        migrationBuilder.CreateIndex(
            name: "IX_PersonVollmachten_PersonId_GueltigVon",
            table: "PersonVollmachten",
            columns: new[] { "PersonId", "GueltigVon" });
    
        migrationBuilder.Sql(VibeVHS.Infrastructure.Personen.PersonenSuchindex.TabelleAnlegen);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS PersonSuche;");
        migrationBuilder.DropTable(
            name: "DublettenAusschluesse");

        migrationBuilder.DropTable(
            name: "Organisationen");

        migrationBuilder.DropTable(
            name: "PersonEinwilligungen");

        migrationBuilder.DropTable(
            name: "PersonOrganisationen");

        migrationBuilder.DropTable(
            name: "PersonVollmachten");

        migrationBuilder.DropIndex(
            name: "IX_Personen_Geburtsdatum",
            table: "Personen");

        migrationBuilder.DropIndex(
            name: "IX_Personen_Sortierschluessel",
            table: "Personen");

        migrationBuilder.DropIndex(
            name: "IX_Personen_Status",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Anzeigename",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Fachbereiche",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Sortiername",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Sortierschluessel",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Wohnort",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "ZusammengefuehrtIn",
            table: "Personen");
    }
}
