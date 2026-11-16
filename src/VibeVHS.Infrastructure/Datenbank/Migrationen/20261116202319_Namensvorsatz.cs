using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116202319_Namensvorsatz : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Vorsatz",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 30,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Zusatz",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "");
        // Bestandserhalt: bekannte Namensvorsätze aus Nachnamen („von Berg“) bzw. Vornamen („Anna von“, Startdaten) herauslösen.
        // Anzeige bleibt gleich („Anna von Berg“), Sortierung künftig nach „Berg“ (DIN 5007-2). Danach die Zwischenstände der
        // betroffenen Personen beim Start neu berechnen lassen (leerer Anzeigename = „nachführen“, siehe PersonenSuchindex).
        migrationBuilder.Sql("""
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 10), "Nachname" = trim(substr("Nachname", 12))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'von und zu %' AND length(trim(substr("Nachname", 12))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 9), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 11))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% von und zu' AND length(trim(substr("Vorname", 1, length("Vorname") - 11))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 7), "Nachname" = trim(substr("Nachname", 9))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'von der %' AND length(trim(substr("Nachname", 9))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 6), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 8))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% von der' AND length(trim(substr("Vorname", 1, length("Vorname") - 8))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 7), "Nachname" = trim(substr("Nachname", 9))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'von dem %' AND length(trim(substr("Nachname", 9))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 6), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 8))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% von dem' AND length(trim(substr("Vorname", 1, length("Vorname") - 8))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 7), "Nachname" = trim(substr("Nachname", 9))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'van der %' AND length(trim(substr("Nachname", 9))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 6), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 8))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% van der' AND length(trim(substr("Vorname", 1, length("Vorname") - 8))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 7), "Nachname" = trim(substr("Nachname", 9))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'van den %' AND length(trim(substr("Nachname", 9))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 6), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 8))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% van den' AND length(trim(substr("Vorname", 1, length("Vorname") - 8))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 6), "Nachname" = trim(substr("Nachname", 8))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'van de %' AND length(trim(substr("Nachname", 8))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 5), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 7))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% van de' AND length(trim(substr("Vorname", 1, length("Vorname") - 7))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 6), "Nachname" = trim(substr("Nachname", 8))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'op den %' AND length(trim(substr("Nachname", 8))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 5), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 7))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% op den' AND length(trim(substr("Vorname", 1, length("Vorname") - 7))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 3), "Nachname" = trim(substr("Nachname", 5))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'von %' AND length(trim(substr("Nachname", 5))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 2), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 4))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% von' AND length(trim(substr("Vorname", 1, length("Vorname") - 4))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 3), "Nachname" = trim(substr("Nachname", 5))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'vom %' AND length(trim(substr("Nachname", 5))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 2), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 4))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% vom' AND length(trim(substr("Vorname", 1, length("Vorname") - 4))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 2), "Nachname" = trim(substr("Nachname", 4))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'zu %' AND length(trim(substr("Nachname", 4))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 1), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 3))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% zu' AND length(trim(substr("Vorname", 1, length("Vorname") - 3))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 3), "Nachname" = trim(substr("Nachname", 5))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'zur %' AND length(trim(substr("Nachname", 5))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 2), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 4))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% zur' AND length(trim(substr("Vorname", 1, length("Vorname") - 4))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 3), "Nachname" = trim(substr("Nachname", 5))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'van %' AND length(trim(substr("Nachname", 5))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 2), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 4))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% van' AND length(trim(substr("Vorname", 1, length("Vorname") - 4))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 3), "Nachname" = trim(substr("Nachname", 5))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'ten %' AND length(trim(substr("Nachname", 5))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 2), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 4))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% ten' AND length(trim(substr("Vorname", 1, length("Vorname") - 4))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Nachname", 1, 3), "Nachname" = trim(substr("Nachname", 5))
                WHERE "Vorsatz" = '' AND lower("Nachname") LIKE 'ter %' AND length(trim(substr("Nachname", 5))) > 0;
            UPDATE "PersonNamen" SET "Vorsatz" = substr("Vorname", length("Vorname") - 2), "Vorname" = trim(substr("Vorname", 1, length("Vorname") - 4))
                WHERE "Vorsatz" = '' AND lower("Vorname") LIKE '% ter' AND length(trim(substr("Vorname", 1, length("Vorname") - 4))) > 0;
            UPDATE "Personen" SET "Anzeigename" = '' WHERE "Id" IN (SELECT "PersonId" FROM "PersonNamen" WHERE "Vorsatz" <> '');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rückweg: Vorsatz und Zusatz wieder in den Nachnamen (Anzeige bleibt gleich).
        migrationBuilder.Sql("""
            UPDATE "PersonNamen" SET "Nachname" = trim("Vorsatz" || ' ' || "Nachname" || ' ' || "Zusatz") WHERE "Vorsatz" <> '' OR "Zusatz" <> '';
            """);

        migrationBuilder.DropColumn(
            name: "Vorsatz",
            table: "PersonNamen");

        migrationBuilder.DropColumn(
            name: "Zusatz",
            table: "PersonNamen");
    }
}
