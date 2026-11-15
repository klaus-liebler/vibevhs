using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115104637_Shop : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Kursstaende",
            columns: table => new
            {
                KursId = table.Column<Guid>(type: "TEXT", nullable: false),
                Kursnummer = table.Column<string>(type: "TEXT", nullable: false),
                Hash = table.Column<string>(type: "TEXT", nullable: false),
                GeaendertAm = table.Column<long>(type: "INTEGER", nullable: false),
                Entfernt = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Kursstaende", x => x.KursId);
            });

        migrationBuilder.CreateTable(
            name: "Shopbuchungen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Idempotenzschluessel = table.Column<string>(type: "TEXT", nullable: false),
                AnfrageHash = table.Column<string>(type: "TEXT", nullable: false),
                WarenkorbId = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                KontoId = table.Column<Guid>(type: "TEXT", nullable: true),
                Zahlart = table.Column<string>(type: "TEXT", nullable: false),
                MandatId = table.Column<Guid>(type: "TEXT", nullable: true),
                Einwilligungen = table.Column<string>(type: "TEXT", nullable: false),
                Positionen = table.Column<string>(type: "TEXT", nullable: false),
                Zeitpunkt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shopbuchungen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Shopkonten",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                EMail = table.Column<string>(type: "TEXT", nullable: false),
                Vorname = table.Column<string>(type: "TEXT", nullable: false),
                Nachname = table.Column<string>(type: "TEXT", nullable: false),
                Geburtsdatum = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Sprache = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                TokenGueltigBis = table.Column<long>(type: "INTEGER", nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false),
                BestaetigtAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shopkonten", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Warenkoerbe",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false),
                ReserviertBis = table.Column<long>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                KontoId = table.Column<Guid>(type: "TEXT", nullable: true),
                Positionen = table.Column<string>(type: "TEXT", nullable: false),
                AbgeschlossenAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Warenkoerbe", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Shopbuchungen_Idempotenzschluessel",
            table: "Shopbuchungen",
            column: "Idempotenzschluessel",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shopbuchungen_WarenkorbId",
            table: "Shopbuchungen",
            column: "WarenkorbId");

        migrationBuilder.CreateIndex(
            name: "IX_Shopkonten_EMail",
            table: "Shopkonten",
            column: "EMail",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shopkonten_TokenHash",
            table: "Shopkonten",
            column: "TokenHash");

        migrationBuilder.CreateIndex(
            name: "IX_Warenkoerbe_Status",
            table: "Warenkoerbe",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Kursstaende");

        migrationBuilder.DropTable(
            name: "Shopbuchungen");

        migrationBuilder.DropTable(
            name: "Shopkonten");

        migrationBuilder.DropTable(
            name: "Warenkoerbe");
    }
}
