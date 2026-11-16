using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116211822_KopienEntfallen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Sortierschluessel",
            table: "PersonNamen",
            type: "TEXT",
            maxLength: 400,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "Benutzerkonten",
            type: "TEXT",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200);

        migrationBuilder.CreateTable(
            name: "KursTeam",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Leitung = table.Column<bool>(type: "INTEGER", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KursTeam", x => x.Id);
                table.ForeignKey(
                    name: "FK_KursTeam_Kurse_KursId",
                    column: x => x.KursId,
                    principalTable: "Kurse",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "VeranstaltungOrte",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OrtId = table.Column<Guid>(type: "TEXT", nullable: false),
                Hoechstzahl = table.Column<int>(type: "INTEGER", nullable: true),
                VeranstaltungId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VeranstaltungOrte", x => x.Id);
                table.ForeignKey(
                    name: "FK_VeranstaltungOrte_Veranstaltungen_VeranstaltungId",
                    column: x => x.VeranstaltungId,
                    principalTable: "Veranstaltungen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Veranstaltungen_DozentId",
            table: "Veranstaltungen",
            column: "DozentId");

        migrationBuilder.CreateIndex(
            name: "IX_PersonNamen_Sortierschluessel",
            table: "PersonNamen",
            column: "Sortierschluessel");

        migrationBuilder.CreateIndex(
            name: "IX_KursTeam_KursId",
            table: "KursTeam",
            column: "KursId");

        migrationBuilder.CreateIndex(
            name: "IX_KursTeam_PersonId",
            table: "KursTeam",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_VeranstaltungOrte_OrtId",
            table: "VeranstaltungOrte",
            column: "OrtId");

        migrationBuilder.CreateIndex(
            name: "IX_VeranstaltungOrte_VeranstaltungId",
            table: "VeranstaltungOrte",
            column: "VeranstaltungId");

        // Datenübernahme: Werte, die nur noch in den entfallenden Spalten stehen, werden zu Merkmalen (nichts geht verloren).
        migrationBuilder.Sql("""
            INSERT INTO "PersonMerkmale" ("Id", "PersonId", "Art", "Inhalt", "GueltigVon", "GueltigBis", "ErfasstAm", "ErfasstVon", "Erfassungsart", "Grund", "ErsetztAm")
            SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))), p."Id", 'Geburtsdatum', p."Geburtsdatum",
                   COALESCE((SELECT MIN(n."GueltigVon") FROM "PersonNamen" n WHERE n."PersonId" = p."Id"), '0001-01-01'),
                   NULL, p."AngelegtAm", 'Datenübernahme', 'Ersterfassung', 'Geburtsdatum aus der entfallenden Spalte übernommen', NULL
            FROM "Personen" p
            WHERE p."Geburtsdatum" IS NOT NULL AND p."Geburtsdatum" <> ''
              AND NOT EXISTS (SELECT 1 FROM "PersonMerkmale" m WHERE m."PersonId" = p."Id" AND m."Art" = 'Geburtsdatum');
            """);
        migrationBuilder.Sql("""
            INSERT INTO "PersonMerkmale" ("Id", "PersonId", "Art", "Inhalt", "GueltigVon", "GueltigBis", "ErfasstAm", "ErfasstVon", "Erfassungsart", "Grund", "ErsetztAm")
            SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))), p."Id", 'Sprache', trim(p."Sprache"),
                   COALESCE((SELECT MIN(n."GueltigVon") FROM "PersonNamen" n WHERE n."PersonId" = p."Id"), '0001-01-01'),
                   NULL, p."AngelegtAm", 'Datenübernahme', 'Ersterfassung', 'Sprache aus der entfallenden Spalte übernommen', NULL
            FROM "Personen" p
            WHERE p."Sprache" IS NOT NULL AND trim(p."Sprache") <> ''
              AND NOT EXISTS (SELECT 1 FROM "PersonMerkmale" m WHERE m."PersonId" = p."Id" AND m."Art" = 'Sprache');
            """);
        migrationBuilder.Sql("""
            INSERT INTO "PersonMerkmale" ("Id", "PersonId", "Art", "Inhalt", "GueltigVon", "GueltigBis", "ErfasstAm", "ErfasstVon", "Erfassungsart", "Grund", "ErsetztAm")
            SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))), p."Id", 'Rollen', CAST(p."Rollen" AS TEXT),
                   COALESCE((SELECT MIN(n."GueltigVon") FROM "PersonNamen" n WHERE n."PersonId" = p."Id"), '0001-01-01'),
                   NULL, p."AngelegtAm", 'Datenübernahme', 'Ersterfassung', 'Rollen aus der entfallenden Spalte übernommen', NULL
            FROM "Personen" p
            WHERE p."Rollen" <> 0
              AND NOT EXISTS (SELECT 1 FROM "PersonMerkmale" m WHERE m."PersonId" = p."Id" AND m."Art" = 'Rollen');
            """);

        // Kursteam und Orte der Termine: aus den JSON-Spalten in eigene Tabellen (Namen werden nicht übernommen, sie kommen zur Laufzeit).
        migrationBuilder.Sql("""
            INSERT INTO "KursTeam" ("Id", "KursId", "PersonId", "Leitung")
            SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))), k."Id", upper(json_extract(j.value, '$.PersonId')), CASE WHEN json_extract(j.value, '$.Leitung') THEN 1 ELSE 0 END
            FROM "Kurse" k, json_each(k."Team") j
            WHERE k."Team" IS NOT NULL AND json_valid(k."Team");
            """);
        migrationBuilder.Sql("""
            INSERT INTO "VeranstaltungOrte" ("Id", "VeranstaltungId", "OrtId", "Hoechstzahl")
            SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))), v."Id", upper(json_extract(j.value, '$.OrtId')), json_extract(j.value, '$.Hoechstzahl')
            FROM "Veranstaltungen" v, json_each(v."Orte") j
            WHERE v."Orte" IS NOT NULL AND json_valid(v."Orte");
            """);
        // Konten mit Person: der Name kommt aus der Person – geleert wird in der Folgemigration „KontonameLeeren“
        // (SQLite baut die Tabelle für „Name darf leer sein“ erst am Ende dieser Migration um).

        migrationBuilder.DropIndex(
            name: "IX_Personen_Geburtsdatum",
            table: "Personen");

        migrationBuilder.DropIndex(
            name: "IX_Personen_Sortierschluessel",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "DozentName",
            table: "Veranstaltungen");

        migrationBuilder.DropColumn(
            name: "Orte",
            table: "Veranstaltungen");

        migrationBuilder.DropColumn(
            name: "Anzeigename",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "EMail",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Geburtsdatum",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Rollen",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Sortiername",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Sortierschluessel",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Sprache",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Titel",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Wohnort",
            table: "Personen");

        migrationBuilder.DropColumn(
            name: "Suchtext",
            table: "Orte");

        migrationBuilder.DropColumn(
            name: "AuftraggeberName",
            table: "Kurse");

        migrationBuilder.DropColumn(
            name: "Suchtext",
            table: "Kurse");

        migrationBuilder.DropColumn(
            name: "Team",
            table: "Kurse");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Rückweg nur für das Schema; die entfernten Kopien werden nicht wiederhergestellt (dafür die Sicherung vor dem Update einspielen).
        migrationBuilder.Sql("""UPDATE "Benutzerkonten" SET "Name" = '' WHERE "Name" IS NULL;""");

        migrationBuilder.DropTable(
            name: "KursTeam");

        migrationBuilder.DropTable(
            name: "VeranstaltungOrte");

        migrationBuilder.DropIndex(
            name: "IX_Veranstaltungen_DozentId",
            table: "Veranstaltungen");

        migrationBuilder.DropIndex(
            name: "IX_PersonNamen_Sortierschluessel",
            table: "PersonNamen");

        migrationBuilder.DropColumn(
            name: "Sortierschluessel",
            table: "PersonNamen");

        migrationBuilder.AddColumn<string>(
            name: "DozentName",
            table: "Veranstaltungen",
            type: "TEXT",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Orte",
            table: "Veranstaltungen",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Anzeigename",
            table: "Personen",
            type: "TEXT",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "EMail",
            table: "Personen",
            type: "TEXT",
            maxLength: 320,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "Geburtsdatum",
            table: "Personen",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Rollen",
            table: "Personen",
            type: "INTEGER",
            maxLength: 40,
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "Sortiername",
            table: "Personen",
            type: "TEXT",
            maxLength: 300,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Sortierschluessel",
            table: "Personen",
            type: "TEXT",
            maxLength: 340,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Sprache",
            table: "Personen",
            type: "TEXT",
            maxLength: 10,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Titel",
            table: "Personen",
            type: "TEXT",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Wohnort",
            table: "Personen",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Suchtext",
            table: "Orte",
            type: "TEXT",
            maxLength: 500,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "AuftraggeberName",
            table: "Kurse",
            type: "TEXT",
            maxLength: 300,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Suchtext",
            table: "Kurse",
            type: "TEXT",
            maxLength: 400,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Team",
            table: "Kurse",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "Benutzerkonten",
            type: "TEXT",
            maxLength: 200,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "TEXT",
            oldMaxLength: 200,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Personen_Geburtsdatum",
            table: "Personen",
            column: "Geburtsdatum");

        migrationBuilder.CreateIndex(
            name: "IX_Personen_Sortierschluessel",
            table: "Personen",
            column: "Sortierschluessel",
            unique: true);
    }
}
