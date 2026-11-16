using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116200342_BenutzerkennungEntfaellt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Bestandserhalt: Die Verknüpfung Konto ↔ Person steht künftig nur am Konto. Fehlt sie dort, wird sie aus Personen.Benutzerkennung übernommen.
        migrationBuilder.Sql("""
            UPDATE "Benutzerkonten" SET "PersonId" = (SELECT p."Id" FROM "Personen" p WHERE lower(p."Benutzerkennung") = lower("Benutzerkonten"."Login"))
            WHERE "PersonId" IS NULL AND EXISTS (SELECT 1 FROM "Personen" p WHERE lower(p."Benutzerkennung") = lower("Benutzerkonten"."Login"));
            """);

        // Widersprüche (Konto zeigt auf eine andere Person) nicht raten, sondern im Protokoll festhalten (nur Pseudonyme).
        var jetzt = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter().ConvertToProvider(DateTimeOffset.UtcNow);
        migrationBuilder.Sql($"""
            INSERT INTO "Protokoll" ("Id", "Zeitpunkt", "BenutzerKennung", "Benutzer", "Rolle", "Aktion", "ObjektTyp", "ObjektId", "Beschreibung", "Erfolgreich")
            SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))), {jetzt}, 'automatik', 'Migration', NULL, 'Migration.Benutzerkennung', 'Person', p."Id",
                   'Benutzerkennung „' || p."Benutzerkennung" || '“ an Person #' || substr(replace(p."Id", '-', ''), 1, 8)
                   || ', das Konto ist aber mit Person #' || substr(replace(k."PersonId", '-', ''), 1, 8) || ' verknüpft – bitte prüfen', 0
            FROM "Personen" p JOIN "Benutzerkonten" k ON lower(k."Login") = lower(p."Benutzerkennung")
            WHERE k."PersonId" IS NOT NULL AND upper(k."PersonId") <> upper(p."Id");
            """);

        migrationBuilder.DropIndex(
            name: "IX_Personen_Benutzerkennung",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Benutzerkennung",
            table: "Personen");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Benutzerkennung",
            table: "Personen",
            type: "TEXT",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Personen_Benutzerkennung",
            table: "Personen",
            column: "Benutzerkennung",
            unique: true);
    }
}
