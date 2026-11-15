using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115054452_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Personen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Geburtsdatum = table.Column<DateOnly>(type: "TEXT", nullable: true),
                EMail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                Sprache = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                AngelegtAm = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Personen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Postausgang",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Kanal = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Empfaenger = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                EmpfaengerAnzeige = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                Betreff = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                Inhalt = table.Column<string>(type: "TEXT", nullable: false),
                Sprache = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                Uebersetzungslink = table.Column<string>(type: "TEXT", nullable: true),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false),
                AusgeloestVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Versuche = table.Column<int>(type: "INTEGER", nullable: false),
                ZugestelltAm = table.Column<long>(type: "INTEGER", nullable: true),
                Rueckmeldung = table.Column<string>(type: "TEXT", nullable: true),
                Fehler = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Postausgang", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Protokoll",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Zeitpunkt = table.Column<long>(type: "INTEGER", nullable: false),
                BenutzerKennung = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Benutzer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Rolle = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                Aktion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                ObjektTyp = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                ObjektId = table.Column<Guid>(type: "TEXT", nullable: true),
                Beschreibung = table.Column<string>(type: "TEXT", nullable: false),
                Erfolgreich = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Protokoll", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PersonNamen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Vorname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Nachname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                GueltigVon = table.Column<DateOnly>(type: "TEXT", nullable: false),
                GueltigBis = table.Column<DateOnly>(type: "TEXT", nullable: true),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErsetztAm = table.Column<long>(type: "INTEGER", nullable: true),
                Grund = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PersonNamen", x => x.Id);
                table.ForeignKey(
                    name: "FK_PersonNamen_Personen_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Personen",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PersonNamen_Nachname",
            table: "PersonNamen",
            column: "Nachname");

        migrationBuilder.CreateIndex(
            name: "IX_PersonNamen_PersonId_GueltigVon",
            table: "PersonNamen",
            columns: new[] { "PersonId", "GueltigVon" });

        migrationBuilder.CreateIndex(
            name: "IX_Postausgang_PersonId",
            table: "Postausgang",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Postausgang_Status_ErstelltAm",
            table: "Postausgang",
            columns: new[] { "Status", "ErstelltAm" });

        migrationBuilder.CreateIndex(
            name: "IX_Protokoll_ObjektTyp_ObjektId",
            table: "Protokoll",
            columns: new[] { "ObjektTyp", "ObjektId" });

        migrationBuilder.CreateIndex(
            name: "IX_Protokoll_Zeitpunkt",
            table: "Protokoll",
            column: "Zeitpunkt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PersonNamen");

        migrationBuilder.DropTable(
            name: "Postausgang");

        migrationBuilder.DropTable(
            name: "Protokoll");

        migrationBuilder.DropTable(
            name: "Personen");
    }
}
