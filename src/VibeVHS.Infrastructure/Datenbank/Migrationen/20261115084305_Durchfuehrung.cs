using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115084305_Durchfuehrung : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "WeitereAnhaenge",
            table: "Postausgang",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "Bildungsurlaub",
            table: "Kurse",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Anwesenheiten",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                VeranstaltungId = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                BelegungId = table.Column<Guid>(type: "TEXT", nullable: false),
                Datum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                OrtId = table.Column<Guid>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Minuten = table.Column<int>(type: "INTEGER", nullable: false),
                Weg = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                ErstErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErfasstVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Bemerkung = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Anwesenheiten", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Dokumente",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: true),
                BelegungId = table.Column<Guid>(type: "TEXT", nullable: true),
                Titel = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Pdf = table.Column<byte[]>(type: "BLOB", nullable: false),
                DatenJson = table.Column<string>(type: "TEXT", nullable: false),
                Klartext = table.Column<string>(type: "TEXT", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                UebersetzungsToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                UebersetzbarBis = table.Column<long>(type: "INTEGER", nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErstelltVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                NeuausstellungVon = table.Column<Guid>(type: "TEXT", nullable: true),
                NeuausstellungGrund = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                ErsetztDurch = table.Column<Guid>(type: "TEXT", nullable: true),
                AnonymisiertAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Dokumente", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Listenabschluesse",
            columns: table => new
            {
                VeranstaltungId = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                Am = table.Column<long>(type: "INTEGER", nullable: false),
                Von = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Listenabschluesse", x => x.VeranstaltungId);
            });

        migrationBuilder.CreateTable(
            name: "Termincodes",
            columns: table => new
            {
                Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                VeranstaltungId = table.Column<Guid>(type: "TEXT", nullable: false),
                OrtId = table.Column<Guid>(type: "TEXT", nullable: false),
                GueltigVon = table.Column<long>(type: "INTEGER", nullable: false),
                GueltigBis = table.Column<long>(type: "INTEGER", nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Termincodes", x => x.Code);
            });

        migrationBuilder.CreateTable(
            name: "Zugangstokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false),
                ZuletztGenutzt = table.Column<long>(type: "INTEGER", nullable: true),
                WiderrufenAm = table.Column<long>(type: "INTEGER", nullable: true),
                WiderrufGrund = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                Ursprung = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Zugangstokens", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Anwesenheiten_KursId",
            table: "Anwesenheiten",
            column: "KursId");

        migrationBuilder.CreateIndex(
            name: "IX_Anwesenheiten_PersonId",
            table: "Anwesenheiten",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Anwesenheiten_VeranstaltungId_BelegungId",
            table: "Anwesenheiten",
            columns: new[] { "VeranstaltungId", "BelegungId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Dokumente_BelegungId",
            table: "Dokumente",
            column: "BelegungId");

        migrationBuilder.CreateIndex(
            name: "IX_Dokumente_KursId",
            table: "Dokumente",
            column: "KursId");

        migrationBuilder.CreateIndex(
            name: "IX_Dokumente_PersonId",
            table: "Dokumente",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Dokumente_UebersetzungsToken",
            table: "Dokumente",
            column: "UebersetzungsToken",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Termincodes_VeranstaltungId_OrtId",
            table: "Termincodes",
            columns: new[] { "VeranstaltungId", "OrtId" });

        migrationBuilder.CreateIndex(
            name: "IX_Zugangstokens_Hash",
            table: "Zugangstokens",
            column: "Hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Zugangstokens_PersonId",
            table: "Zugangstokens",
            column: "PersonId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Anwesenheiten");

        migrationBuilder.DropTable(
            name: "Dokumente");

        migrationBuilder.DropTable(
            name: "Listenabschluesse");

        migrationBuilder.DropTable(
            name: "Termincodes");

        migrationBuilder.DropTable(
            name: "Zugangstokens");

        migrationBuilder.DropColumn(
            name: "WeitereAnhaenge",
            table: "Postausgang");

        migrationBuilder.DropColumn(
            name: "Bildungsurlaub",
            table: "Kurse");
    }
}
