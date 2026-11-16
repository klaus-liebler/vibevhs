using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116195630_TitelUndGeschlechtVersioniert : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Bestandserhalt: Titel und Geschlecht aus den Spalten als versionierte Merkmale übernehmen (gültig ab Beginn des ersten Namens),
        // nur wo noch kein Merkmal existiert. Danach ist Personen.Titel nur noch Lese-Cache, StatistikGeschlecht entfällt.
        migrationBuilder.Sql("""
            INSERT INTO "PersonMerkmale" ("Id", "PersonId", "Art", "Inhalt", "GueltigVon", "GueltigBis", "ErfasstAm", "ErfasstVon", "Erfassungsart", "Grund", "ErsetztAm")
            SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))), p."Id", 'Titel', trim(p."Titel"),
                   COALESCE((SELECT MIN(n."GueltigVon") FROM "PersonNamen" n WHERE n."PersonId" = p."Id"), '0001-01-01'),
                   NULL, p."AngelegtAm", 'Datenübernahme', 'Ersterfassung', 'Titel aus der bisherigen Spalte übernommen', NULL
            FROM "Personen" p
            WHERE p."Titel" IS NOT NULL AND trim(p."Titel") <> ''
              AND NOT EXISTS (SELECT 1 FROM "PersonMerkmale" m WHERE m."PersonId" = p."Id" AND m."Art" = 'Titel');
            """);
        migrationBuilder.Sql("""
            INSERT INTO "PersonMerkmale" ("Id", "PersonId", "Art", "Inhalt", "GueltigVon", "GueltigBis", "ErfasstAm", "ErfasstVon", "Erfassungsart", "Grund", "ErsetztAm")
            SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))), p."Id", 'Geschlecht', p."StatistikGeschlecht",
                   COALESCE((SELECT MIN(n."GueltigVon") FROM "PersonNamen" n WHERE n."PersonId" = p."Id"), '0001-01-01'),
                   NULL, p."AngelegtAm", 'Datenübernahme', 'Ersterfassung', 'Geschlecht aus der bisherigen Statistikspalte übernommen', NULL
            FROM "Personen" p
            WHERE p."StatistikGeschlecht" IS NOT NULL AND p."StatistikGeschlecht" NOT IN ('', 'nicht erfasst')
              AND NOT EXISTS (SELECT 1 FROM "PersonMerkmale" m WHERE m."PersonId" = p."Id" AND m."Art" = 'Geschlecht');
            """);

        migrationBuilder.DropColumn(
            name: "StatistikGeschlecht",
            table: "Personen");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StatistikGeschlecht",
            table: "Personen",
            type: "TEXT",
            maxLength: 30,
            nullable: false,
            defaultValue: "nicht erfasst");

        // Rückweg: Geschlecht wieder in die Spalte (aktueller Stand), übernommene Merkmale bleiben (schaden nicht).
        migrationBuilder.Sql("""
            UPDATE "Personen" SET "StatistikGeschlecht" = COALESCE((SELECT m."Inhalt" FROM "PersonMerkmale" m
                WHERE m."PersonId" = "Personen"."Id" AND m."Art" = 'Geschlecht' AND m."GueltigBis" IS NULL AND m."ErsetztAm" IS NULL AND m."Inhalt" <> ''
                ORDER BY m."GueltigVon" DESC LIMIT 1), 'nicht erfasst');
            """);
    }
}
