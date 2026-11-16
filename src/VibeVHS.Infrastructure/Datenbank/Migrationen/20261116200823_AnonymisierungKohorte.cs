using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116200823_AnonymisierungKohorte : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AnonymisiertGeburtskohorte",
            table: "Personen",
            type: "TEXT",
            maxLength: 20,
            nullable: true);

        // Bestandserhalt: Aus der eingefrorenen Altersgruppe und dem Jahr der Anonymisierung eine Geburtsjahr-Kohorte schätzen
        // (Mitte der Altersgruppe). AnonymisiertAm ist binär gespeichert (DateTimeOffsetToBinaryConverter: UtcTicks/1000 << 11 | Versatz).
        migrationBuilder.Sql("""
            UPDATE "Personen" SET "AnonymisiertGeburtskohorte" =
                (((1 + ((("AnonymisiertAm" >> 11) * 1000) / 315569520000000)) - CASE "StatistikAltersgruppe"
                    WHEN 'unter 18' THEN 12 WHEN '18–24' THEN 21 WHEN '25–34' THEN 30 WHEN '35–49' THEN 42 WHEN '50–64' THEN 57 WHEN '65 und älter' THEN 72 END) / 5 * 5) || '–' || (((1 + ((("AnonymisiertAm" >> 11) * 1000) / 315569520000000)) - CASE "StatistikAltersgruppe"
                    WHEN 'unter 18' THEN 12 WHEN '18–24' THEN 21 WHEN '25–34' THEN 30 WHEN '35–49' THEN 42 WHEN '50–64' THEN 57 WHEN '65 und älter' THEN 72 END) / 5 * 5 + 4)
            WHERE "AnonymisiertAm" IS NOT NULL AND "StatistikAltersgruppe" IN ('unter 18', '18–24', '25–34', '35–49', '50–64', '65 und älter');
            """);

        migrationBuilder.DropColumn(
            name: "StatistikAltersgruppe",
            table: "Personen");

        migrationBuilder.RenameColumn(
            name: "StatistikGebiet",
            table: "Personen",
            newName: "AnonymisiertGebiet");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AnonymisiertGeburtskohorte",
            table: "Personen");

        migrationBuilder.RenameColumn(
            name: "AnonymisiertGebiet",
            table: "Personen",
            newName: "StatistikGebiet");

        migrationBuilder.AddColumn<string>(
            name: "StatistikAltersgruppe",
            table: "Personen",
            type: "TEXT",
            maxLength: 30,
            nullable: true);
    }
}
