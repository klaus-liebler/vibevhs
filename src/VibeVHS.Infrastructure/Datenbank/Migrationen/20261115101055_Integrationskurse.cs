using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115101055_Integrationskurse : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "Vorlaeufig",
            table: "Anwesenheiten",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "VorlaeufigGrund",
            table: "Anwesenheiten",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Identitaetspruefungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Dokument = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                StimmtUeberein = table.Column<bool>(type: "INTEGER", nullable: false),
                Bemerkung = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                GeprueftAm = table.Column<long>(type: "INTEGER", nullable: false),
                GeprueftVon = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Identitaetspruefungen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "IkFahrtkosten",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                Jahr = table.Column<int>(type: "INTEGER", nullable: false),
                Monat = table.Column<int>(type: "INTEGER", nullable: false),
                EntfernungKm = table.Column<decimal>(type: "TEXT", nullable: false),
                BetragCent = table.Column<long>(type: "INTEGER", nullable: false),
                Begruendung = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                BeantragtAm = table.Column<long>(type: "INTEGER", nullable: false),
                GutschriftId = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IkFahrtkosten", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Integrationskurse",
            columns: table => new
            {
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                BamfKursnummer = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Abschnitte = table.Column<string>(type: "TEXT", nullable: false),
                KostenerstattungCentJeUe = table.Column<long>(type: "INTEGER", nullable: false),
                KostenbeitragCentJeUe = table.Column<long>(type: "INTEGER", nullable: false),
                KursbeginnGemeldetAm = table.Column<long>(type: "INTEGER", nullable: true),
                KursbeginnVorgang = table.Column<string>(type: "TEXT", nullable: true),
                Abrechnungen = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Integrationskurse", x => x.KursId);
            });

        migrationBuilder.CreateTable(
            name: "Lichtbilder",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                Inhalt = table.Column<byte[]>(type: "BLOB", nullable: false),
                Typ = table.Column<string>(type: "TEXT", nullable: false),
                LoeschenAb = table.Column<DateOnly>(type: "TEXT", nullable: false),
                AufgenommenAm = table.Column<long>(type: "INTEGER", nullable: false),
                AufgenommenVon = table.Column<string>(type: "TEXT", nullable: false),
                GeloeschtAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Lichtbilder", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Pruefungsanmeldungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PruefungId = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                AngemeldetAm = table.Column<long>(type: "INTEGER", nullable: false),
                Ergebnis = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Punkte = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Pruefungsanmeldungen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Pruefungstermine",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Beginn = table.Column<DateTime>(type: "TEXT", nullable: false),
                Ort = table.Column<string>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: true),
                GemeldetAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Pruefungstermine", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Teilnahmeberechtigungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Aktenzeichen = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Stelle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Ausgestellt = table.Column<DateOnly>(type: "TEXT", nullable: false),
                GueltigBis = table.Column<DateOnly>(type: "TEXT", nullable: true),
                UeUmfang = table.Column<int>(type: "INTEGER", nullable: false),
                Kostenbefreit = table.Column<bool>(type: "INTEGER", nullable: false),
                Befreiungsgrund = table.Column<string>(type: "TEXT", nullable: true),
                Fahrtkosten = table.Column<bool>(type: "INTEGER", nullable: false),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErfasstVon = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Teilnahmeberechtigungen", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Identitaetspruefungen_PersonId",
            table: "Identitaetspruefungen",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_IkFahrtkosten_KursId",
            table: "IkFahrtkosten",
            column: "KursId");

        migrationBuilder.CreateIndex(
            name: "IX_Lichtbilder_PersonId_KursId",
            table: "Lichtbilder",
            columns: new[] { "PersonId", "KursId" });

        migrationBuilder.CreateIndex(
            name: "IX_Pruefungsanmeldungen_PersonId",
            table: "Pruefungsanmeldungen",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Pruefungsanmeldungen_PruefungId",
            table: "Pruefungsanmeldungen",
            column: "PruefungId");

        migrationBuilder.CreateIndex(
            name: "IX_Teilnahmeberechtigungen_PersonId",
            table: "Teilnahmeberechtigungen",
            column: "PersonId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Identitaetspruefungen");

        migrationBuilder.DropTable(
            name: "IkFahrtkosten");

        migrationBuilder.DropTable(
            name: "Integrationskurse");

        migrationBuilder.DropTable(
            name: "Lichtbilder");

        migrationBuilder.DropTable(
            name: "Pruefungsanmeldungen");

        migrationBuilder.DropTable(
            name: "Pruefungstermine");

        migrationBuilder.DropTable(
            name: "Teilnahmeberechtigungen");

        migrationBuilder.DropColumn(
            name: "Vorlaeufig",
            table: "Anwesenheiten");

        migrationBuilder.DropColumn(
            name: "VorlaeufigGrund",
            table: "Anwesenheiten");
    }
}
