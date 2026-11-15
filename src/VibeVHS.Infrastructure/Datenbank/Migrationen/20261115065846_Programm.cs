using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115065846_Programm : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Aufgaben",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Schluessel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Titel = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Beschreibung = table.Column<string>(type: "TEXT", nullable: true),
                Faellig = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Gruppe = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Ziel = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                AngelegtAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErledigtAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Aufgaben", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Ferien",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Von = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Bis = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Bundesland = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Ferien", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Kursarten",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Nutzungsart = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                BenoetigteMerkmale = table.Column<string>(type: "TEXT", nullable: false),
                Richtlinie = table.Column<string>(type: "TEXT", nullable: false),
                Sichtbarkeit = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Kursarten", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Kurse",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Kursnummer = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                Titel = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Beschreibung = table.Column<string>(type: "TEXT", nullable: true),
                SemesterId = table.Column<Guid>(type: "TEXT", nullable: false),
                Fachbereich = table.Column<int>(type: "INTEGER", nullable: false),
                KursartId = table.Column<Guid>(type: "TEXT", nullable: false),
                Nutzungsart = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                BenoetigteMerkmale = table.Column<string>(type: "TEXT", nullable: false),
                Sichtbarkeit = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                AuftraggeberId = table.Column<Guid>(type: "TEXT", nullable: true),
                AuftraggeberName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                Richtlinie = table.Column<string>(type: "TEXT", nullable: false),
                MinTeilnehmende = table.Column<int>(type: "INTEGER", nullable: false),
                MaxTeilnehmende = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Umfang = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Serie = table.Column<string>(type: "TEXT", nullable: true),
                ErzeugtBis = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Team = table.Column<string>(type: "TEXT", nullable: false),
                Suchtext = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Kurse", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Merkmale",
            columns: table => new
            {
                Schluessel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Gruppe = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Vererbbar = table.Column<bool>(type: "INTEGER", nullable: false),
                NachweisPflicht = table.Column<bool>(type: "INTEGER", nullable: false),
                Ausschlussgruppe = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Merkmale", x => x.Schluessel);
            });

        migrationBuilder.CreateTable(
            name: "Orte",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Kurzname = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                ElternId = table.Column<Guid>(type: "TEXT", nullable: true),
                Strasse = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Postleitzahl = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                Stadt = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Breitengrad = table.Column<double>(type: "REAL", nullable: true),
                Laengengrad = table.Column<double>(type: "REAL", nullable: true),
                Lage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                Zugang = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                Aktiv = table.Column<bool>(type: "INTEGER", nullable: false),
                Kapazitaeten = table.Column<string>(type: "TEXT", nullable: false),
                Suchtext = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orte", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Semester",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Kuerzel = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                Beginn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Ende = table.Column<DateOnly>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Semester", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "KursEntgelte",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Cent = table.Column<long>(type: "INTEGER", nullable: false),
                ErmaessigtCent = table.Column<long>(type: "INTEGER", nullable: true),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                table.PrimaryKey("PK_KursEntgelte", x => x.Id);
                table.ForeignKey(
                    name: "FK_KursEntgelte_Kurse_KursId",
                    column: x => x.KursId,
                    principalTable: "Kurse",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Veranstaltungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                Nummer = table.Column<int>(type: "INTEGER", nullable: false),
                Beginn = table.Column<DateTime>(type: "TEXT", nullable: false),
                Ende = table.Column<DateTime>(type: "TEXT", nullable: false),
                Orte = table.Column<string>(type: "TEXT", nullable: false),
                DozentId = table.Column<Guid>(type: "TEXT", nullable: true),
                DozentName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                AusSerie = table.Column<bool>(type: "INTEGER", nullable: false),
                Ausnahme = table.Column<bool>(type: "INTEGER", nullable: false),
                Ausgefallen = table.Column<bool>(type: "INTEGER", nullable: false),
                Bemerkung = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Veranstaltungen", x => x.Id);
                table.ForeignKey(
                    name: "FK_Veranstaltungen_Kurse_KursId",
                    column: x => x.KursId,
                    principalTable: "Kurse",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "OrtMerkmale",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Schluessel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                WertText = table.Column<string>(type: "TEXT", nullable: true),
                Nachweis = table.Column<string>(type: "TEXT", nullable: true),
                OrtId = table.Column<Guid>(type: "TEXT", nullable: false),
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
                table.PrimaryKey("PK_OrtMerkmale", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrtMerkmale_Orte_OrtId",
                    column: x => x.OrtId,
                    principalTable: "Orte",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Aufgaben_Schluessel",
            table: "Aufgaben",
            column: "Schluessel",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Kurse_Kursnummer",
            table: "Kurse",
            column: "Kursnummer",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Kurse_SemesterId",
            table: "Kurse",
            column: "SemesterId");

        migrationBuilder.CreateIndex(
            name: "IX_KursEntgelte_KursId_GueltigVon",
            table: "KursEntgelte",
            columns: new[] { "KursId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_Orte_ElternId",
            table: "Orte",
            column: "ElternId");

        migrationBuilder.CreateIndex(
            name: "IX_OrtMerkmale_OrtId_GueltigVon",
            table: "OrtMerkmale",
            columns: new[] { "OrtId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_Veranstaltungen_Beginn",
            table: "Veranstaltungen",
            column: "Beginn");

        migrationBuilder.CreateIndex(
            name: "IX_Veranstaltungen_KursId",
            table: "Veranstaltungen",
            column: "KursId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Aufgaben");

        migrationBuilder.DropTable(
            name: "Ferien");

        migrationBuilder.DropTable(
            name: "Kursarten");

        migrationBuilder.DropTable(
            name: "KursEntgelte");

        migrationBuilder.DropTable(
            name: "Merkmale");

        migrationBuilder.DropTable(
            name: "OrtMerkmale");

        migrationBuilder.DropTable(
            name: "Semester");

        migrationBuilder.DropTable(
            name: "Veranstaltungen");

        migrationBuilder.DropTable(
            name: "Orte");

        migrationBuilder.DropTable(
            name: "Kurse");
    }
}
