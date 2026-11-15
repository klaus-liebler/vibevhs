using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115090847_Aufgaben : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ErinnertAm",
            table: "Belegungen",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BearbeiterKennung",
            table: "Aufgaben",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BearbeiterName",
            table: "Aufgaben",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Dringlichkeit",
            table: "Aufgaben",
            type: "TEXT",
            maxLength: 40,
            nullable: false,
            defaultValue: "Normal");

        migrationBuilder.AddColumn<string>(
            name: "ErledigtVon",
            table: "Aufgaben",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "EskaliertAm",
            table: "Aufgaben",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Fachbereich",
            table: "Aufgaben",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "Aufgaben",
            type: "TEXT",
            maxLength: 40,
            nullable: false,
            defaultValue: "Offen");

        migrationBuilder.AddColumn<string>(
            name: "Typ",
            table: "Aufgaben",
            type: "TEXT",
            maxLength: 60,
            nullable: false,
            defaultValue: "allgemein");

        migrationBuilder.AddColumn<long>(
            name: "UebernommenAm",
            table: "Aufgaben",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Vermerk",
            table: "Aufgaben",
            type: "TEXT",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Aufgabentypen",
            columns: table => new
            {
                Schluessel = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Beschreibung = table.Column<string>(type: "TEXT", nullable: true),
                Rolle = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                JeFachbereich = table.Column<bool>(type: "INTEGER", nullable: false),
                FestePerson = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                EskalationNachTagen = table.Column<int>(type: "INTEGER", nullable: false),
                VorlaufTage = table.Column<int>(type: "INTEGER", nullable: true),
                MenschGibtFrei = table.Column<bool>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Aufgabentypen", x => x.Schluessel);
            });

        // Bestehende Aufgaben übernehmen: Typ aus dem Schlüsselpräfix, Erledigte als erledigt, BAMF-Zulassungen eigener Typ.
        migrationBuilder.Sql("UPDATE Aufgaben SET Typ = substr(Schluessel, 1, instr(Schluessel, ':') - 1) WHERE instr(Schluessel, ':') > 1;");
        migrationBuilder.Sql("UPDATE Aufgaben SET Typ = 'zulassung-ablauf' WHERE Typ = 'merkmal-ablauf' AND Gruppe = 'IntegrationskursTeam';");
        migrationBuilder.Sql("UPDATE Aufgaben SET Status = 'Erledigt', ErledigtVon = 'Übernahme I5' WHERE ErledigtAm IS NOT NULL;");

        migrationBuilder.CreateIndex(
            name: "IX_Aufgaben_Status_Gruppe",
            table: "Aufgaben",
            columns: new[] { "Status", "Gruppe" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Aufgabentypen");

        migrationBuilder.DropIndex(
            name: "IX_Aufgaben_Status_Gruppe",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "ErinnertAm",
            table: "Belegungen");

        migrationBuilder.DropColumn(
            name: "BearbeiterKennung",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "BearbeiterName",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "Dringlichkeit",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "ErledigtVon",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "EskaliertAm",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "Fachbereich",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "Status",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "Typ",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "UebernommenAm",
            table: "Aufgaben");

        migrationBuilder.DropColumn(
            name: "Vermerk",
            table: "Aufgaben");
    }
}
