using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115074058_Anmeldung : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AnhangInhalt",
            table: "Postausgang",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AnhangName",
            table: "Postausgang",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AnhangTyp",
            table: "Postausgang",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Belegungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                Kursnummer = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Weg = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                GebuchtAm = table.Column<long>(type: "INTEGER", nullable: false),
                GebuchtVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                GebuchtVonKennung = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                GebuchtInRolle = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                BevollmaechtigterId = table.Column<Guid>(type: "TEXT", nullable: true),
                BevollmaechtigterName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                VollmachtId = table.Column<Guid>(type: "TEXT", nullable: true),
                EntgeltCent = table.Column<long>(type: "INTEGER", nullable: false),
                Ermaessigt = table.Column<bool>(type: "INTEGER", nullable: false),
                ErmaessigungNachweis = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                Zahlart = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Belegungsart = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                WartelisteSeit = table.Column<long>(type: "INTEGER", nullable: true),
                AngebotBis = table.Column<long>(type: "INTEGER", nullable: true),
                StorniertAm = table.Column<long>(type: "INTEGER", nullable: true),
                StorniertVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                StornoGrund = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                GebuehrCent = table.Column<long>(type: "INTEGER", nullable: true),
                UmgebuchtVonId = table.Column<Guid>(type: "TEXT", nullable: true),
                UmgebuchtNachId = table.Column<Guid>(type: "TEXT", nullable: true),
                KontingentAnzahl = table.Column<int>(type: "INTEGER", nullable: true),
                KontingentGueltigBis = table.Column<DateOnly>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Belegungen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Ermaessigungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Nachweis = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                GueltigVon = table.Column<DateOnly>(type: "TEXT", nullable: false),
                GueltigBis = table.Column<DateOnly>(type: "TEXT", nullable: false),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErfasstVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Ermaessigungen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Vorlagen",
            columns: table => new
            {
                Schluessel = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Bezeichnung = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Betreff = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Text = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                GeaendertAm = table.Column<long>(type: "INTEGER", nullable: false),
                GeaendertVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Vorlagen", x => x.Schluessel);
            });

        migrationBuilder.CreateTable(
            name: "KontingentEreignisse",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                BelegungId = table.Column<Guid>(type: "TEXT", nullable: false),
                Laufnummer = table.Column<int>(type: "INTEGER", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                VeranstaltungId = table.Column<Guid>(type: "TEXT", nullable: true),
                OrtId = table.Column<Guid>(type: "TEXT", nullable: true),
                Anzahl = table.Column<int>(type: "INTEGER", nullable: false),
                Zeitpunkt = table.Column<long>(type: "INTEGER", nullable: false),
                Von = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Bemerkung = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KontingentEreignisse", x => x.Id);
                table.ForeignKey(
                    name: "FK_KontingentEreignisse_Belegungen_BelegungId",
                    column: x => x.BelegungId,
                    principalTable: "Belegungen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Belegungen_BevollmaechtigterId",
            table: "Belegungen",
            column: "BevollmaechtigterId");

        migrationBuilder.CreateIndex(
            name: "IX_Belegungen_KursId",
            table: "Belegungen",
            column: "KursId");

        migrationBuilder.CreateIndex(
            name: "IX_Belegungen_PersonId",
            table: "Belegungen",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Belegungen_Status_AngebotBis",
            table: "Belegungen",
            columns: new[] { "Status", "AngebotBis" });

        migrationBuilder.CreateIndex(
            name: "IX_Ermaessigungen_PersonId",
            table: "Ermaessigungen",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_KontingentEreignisse_BelegungId_Laufnummer",
            table: "KontingentEreignisse",
            columns: new[] { "BelegungId", "Laufnummer" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_KontingentEreignisse_VeranstaltungId",
            table: "KontingentEreignisse",
            column: "VeranstaltungId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Ermaessigungen");

        migrationBuilder.DropTable(
            name: "KontingentEreignisse");

        migrationBuilder.DropTable(
            name: "Vorlagen");

        migrationBuilder.DropTable(
            name: "Belegungen");

        migrationBuilder.DropColumn(
            name: "AnhangInhalt",
            table: "Postausgang");

        migrationBuilder.DropColumn(
            name: "AnhangName",
            table: "Postausgang");

        migrationBuilder.DropColumn(
            name: "AnhangTyp",
            table: "Postausgang");
    }
}
