using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115071848_Datenschutz : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ArchiviertAm",
            table: "Semester",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "AnonymisiertAm",
            table: "Personen",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "ArchiviertAm",
            table: "Personen",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Datenschutz",
            table: "Personen",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "Aktiv");

        migrationBuilder.AddColumn<long>(
            name: "GesperrtAm",
            table: "Personen",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "GesperrtBis",
            table: "Personen",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Sperrgrund",
            table: "Personen",
            type: "TEXT",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StatistikAltersgruppe",
            table: "Personen",
            type: "TEXT",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StatistikGebiet",
            table: "Personen",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StatistikGeschlecht",
            table: "Personen",
            type: "TEXT",
            maxLength: 30,
            nullable: false,
            defaultValue: "nicht erfasst");

        migrationBuilder.AddColumn<long>(
            name: "ArchiviertAm",
            table: "Kurse",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Aufbewahrungsfristen",
            columns: table => new
            {
                Schluessel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Bezeichnung = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Jahre = table.Column<int>(type: "INTEGER", nullable: false),
                AbJahresende = table.Column<bool>(type: "INTEGER", nullable: false),
                Rechtsgrundlage = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                Hinweis = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Aufbewahrungsfristen", x => x.Schluessel);
            });

        migrationBuilder.CreateTable(
            name: "Loeschlaeufe",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Regel = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErstelltVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                PersonIds = table.Column<string>(type: "TEXT", nullable: false),
                ZuAnonymisieren = table.Column<int>(type: "INTEGER", nullable: false),
                ZuSperren = table.Column<int>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Freigabe1Kennung = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Freigabe1Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Freigabe1Am = table.Column<long>(type: "INTEGER", nullable: true),
                Freigabe2Kennung = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Freigabe2Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Freigabe2Am = table.Column<long>(type: "INTEGER", nullable: true),
                AusgefuehrtAm = table.Column<long>(type: "INTEGER", nullable: true),
                Anonymisiert = table.Column<int>(type: "INTEGER", nullable: false),
                Gesperrt = table.Column<int>(type: "INTEGER", nullable: false),
                Uebersprungen = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Loeschlaeufe", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Personen_Datenschutz",
            table: "Personen",
            column: "Datenschutz");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Aufbewahrungsfristen");

        migrationBuilder.DropTable(
            name: "Loeschlaeufe");

        migrationBuilder.DropIndex(
            name: "IX_Personen_Datenschutz",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "ArchiviertAm",
            table: "Semester");

        migrationBuilder.DropColumn(
            name: "AnonymisiertAm",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "ArchiviertAm",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Datenschutz",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "GesperrtAm",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "GesperrtBis",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Sperrgrund",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "StatistikAltersgruppe",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "StatistikGebiet",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "StatistikGeschlecht",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "ArchiviertAm",
            table: "Kurse");
    }
}
