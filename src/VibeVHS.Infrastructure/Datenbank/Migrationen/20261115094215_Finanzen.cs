using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115094215_Finanzen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "AusgefallenAm",
            table: "Veranstaltungen",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LeitwegId",
            table: "Organisationen",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RechnungsEMail",
            table: "Organisationen",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Rechnungsanschrift",
            table: "Organisationen",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Bankverbindungen",
            columns: table => new
            {
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Kontoinhaber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                Bic = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Bankverbindungen", x => x.PersonId);
            });

        migrationBuilder.CreateTable(
            name: "Belege",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Nummer = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                Jahr = table.Column<int>(type: "INTEGER", nullable: true),
                Laufnummer = table.Column<int>(type: "INTEGER", nullable: true),
                Belegdatum = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Faellig = table.Column<DateOnly>(type: "TEXT", nullable: true),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErfasstVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                FestgeschriebenAm = table.Column<long>(type: "INTEGER", nullable: true),
                FestgeschriebenVon = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                OrganisationId = table.Column<Guid>(type: "TEXT", nullable: true),
                EmpfaengerName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                EmpfaengerAnschrift = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                BetragCent = table.Column<long>(type: "INTEGER", nullable: false),
                BezugBelegId = table.Column<Guid>(type: "TEXT", nullable: true),
                StorniertDurchId = table.Column<Guid>(type: "TEXT", nullable: true),
                Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                DokumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                Xml = table.Column<string>(type: "TEXT", nullable: true),
                UebergabeId = table.Column<Guid>(type: "TEXT", nullable: true),
                Grundlage = table.Column<string>(type: "TEXT", nullable: true),
                AnonymisiertAm = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Belege", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Finanzeinstellungen",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                Kontoinhaber = table.Column<string>(type: "TEXT", nullable: false),
                Anschrift = table.Column<string>(type: "TEXT", nullable: false),
                Iban = table.Column<string>(type: "TEXT", nullable: false),
                Bic = table.Column<string>(type: "TEXT", nullable: true),
                GlaeubigerId = table.Column<string>(type: "TEXT", nullable: false),
                Steuernummer = table.Column<string>(type: "TEXT", nullable: true),
                UstId = table.Column<string>(type: "TEXT", nullable: true),
                Steuerhinweis = table.Column<string>(type: "TEXT", nullable: false),
                ZahlungszielTage = table.Column<int>(type: "INTEGER", nullable: false),
                VorabankuendigungTage = table.Column<int>(type: "INTEGER", nullable: false),
                RuecklastschriftGebuehrCent = table.Column<long>(type: "INTEGER", nullable: false),
                Mahnstufen = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Finanzeinstellungen", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Finanzprotokoll",
            columns: table => new
            {
                Nr = table.Column<long>(type: "INTEGER", nullable: false),
                Zeitpunkt = table.Column<long>(type: "INTEGER", nullable: false),
                BenutzerKennung = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Aktion = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                ObjektTyp = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                ObjektId = table.Column<Guid>(type: "TEXT", nullable: true),
                Vorher = table.Column<string>(type: "TEXT", nullable: true),
                Nachher = table.Column<string>(type: "TEXT", nullable: true),
                VorigerHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Finanzprotokoll", x => x.Nr);
            });

        migrationBuilder.CreateTable(
            name: "Honorarvertraege",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                KursId = table.Column<Guid>(type: "TEXT", nullable: true),
                Bezeichnung = table.Column<string>(type: "TEXT", nullable: false),
                Modell = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                Parameter = table.Column<string>(type: "TEXT", nullable: false),
                GueltigAb = table.Column<DateOnly>(type: "TEXT", nullable: false),
                GueltigBis = table.Column<DateOnly>(type: "TEXT", nullable: true),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErfasstVon = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Honorarvertraege", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Kontoauszuege",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Datei = table.Column<string>(type: "TEXT", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                AuszugId = table.Column<string>(type: "TEXT", nullable: false),
                Iban = table.Column<string>(type: "TEXT", nullable: true),
                Von = table.Column<DateOnly>(type: "TEXT", nullable: true),
                Bis = table.Column<DateOnly>(type: "TEXT", nullable: true),
                EndsaldoCent = table.Column<long>(type: "INTEGER", nullable: true),
                EingelesenAm = table.Column<long>(type: "INTEGER", nullable: false),
                EingelesenVon = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Kontoauszuege", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Mahnlaeufe",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Posten = table.Column<string>(type: "TEXT", nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErstelltVon = table.Column<string>(type: "TEXT", nullable: false),
                FreigegebenAm = table.Column<long>(type: "INTEGER", nullable: true),
                FreigegebenVon = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Mahnlaeufe", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Nummernkreise",
            columns: table => new
            {
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Jahr = table.Column<int>(type: "INTEGER", nullable: false),
                Letzte = table.Column<int>(type: "INTEGER", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Nummernkreise", x => new { x.Art, x.Jahr });
            });

        migrationBuilder.CreateTable(
            name: "SepaMandate",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                Referenz = table.Column<string>(type: "TEXT", maxLength: 35, nullable: false),
                Kontoinhaber = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                Bic = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true),
                UnterschriebenAm = table.Column<DateOnly>(type: "TEXT", nullable: false),
                ErfasstAm = table.Column<long>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                WiderrufenAm = table.Column<DateOnly>(type: "TEXT", nullable: true),
                ErsteNutzungAm = table.Column<DateOnly>(type: "TEXT", nullable: true),
                LetzteNutzungAm = table.Column<DateOnly>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SepaMandate", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Uebergaben",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Anzahl = table.Column<int>(type: "INTEGER", nullable: false),
                SummeCent = table.Column<long>(type: "INTEGER", nullable: false),
                Datei = table.Column<string>(type: "TEXT", nullable: false),
                Inhalt = table.Column<string>(type: "TEXT", nullable: false),
                Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                Am = table.Column<long>(type: "INTEGER", nullable: false),
                Von = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Uebergaben", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Umsaetze",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                KontoauszugId = table.Column<Guid>(type: "TEXT", nullable: false),
                Nr = table.Column<int>(type: "INTEGER", nullable: false),
                Buchungstag = table.Column<DateOnly>(type: "TEXT", nullable: false),
                Valuta = table.Column<DateOnly>(type: "TEXT", nullable: true),
                BetragCent = table.Column<long>(type: "INTEGER", nullable: false),
                GegenName = table.Column<string>(type: "TEXT", nullable: true),
                GegenIban = table.Column<string>(type: "TEXT", nullable: true),
                Verwendungszweck = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                EndToEndId = table.Column<string>(type: "TEXT", nullable: true),
                Rueckgabegrund = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                VorschlagBelegId = table.Column<Guid>(type: "TEXT", nullable: true),
                Sicherheit = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                VorschlagText = table.Column<string>(type: "TEXT", nullable: true),
                ZugeordnetBelegId = table.Column<Guid>(type: "TEXT", nullable: true),
                ErzeugterBelegId = table.Column<Guid>(type: "TEXT", nullable: true),
                ZugeordnetAm = table.Column<long>(type: "INTEGER", nullable: true),
                ZugeordnetVon = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Umsaetze", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Zahlungslaeufe",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Ausfuehrung = table.Column<DateOnly>(type: "TEXT", nullable: false),
                NachrichtId = table.Column<string>(type: "TEXT", maxLength: 35, nullable: false),
                Posten = table.Column<string>(type: "TEXT", nullable: false),
                ErstelltAm = table.Column<long>(type: "INTEGER", nullable: false),
                ErstelltVon = table.Column<string>(type: "TEXT", nullable: false),
                FreigegebenAm = table.Column<long>(type: "INTEGER", nullable: true),
                FreigegebenVon = table.Column<string>(type: "TEXT", nullable: true),
                ExportiertAm = table.Column<long>(type: "INTEGER", nullable: true),
                Xml = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Zahlungslaeufe", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Belegpositionen",
            columns: table => new
            {
                Nr = table.Column<int>(type: "INTEGER", nullable: false),
                BelegId = table.Column<Guid>(type: "TEXT", nullable: false),
                Art = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                Menge = table.Column<decimal>(type: "TEXT", nullable: false),
                EinzelCent = table.Column<long>(type: "INTEGER", nullable: false),
                BetragCent = table.Column<long>(type: "INTEGER", nullable: false),
                BelegungId = table.Column<Guid>(type: "TEXT", nullable: true),
                KursId = table.Column<Guid>(type: "TEXT", nullable: true),
                VeranstaltungId = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Belegpositionen", x => new { x.BelegId, x.Nr });
                table.ForeignKey(
                    name: "FK_Belegpositionen_Belege_BelegId",
                    column: x => x.BelegId,
                    principalTable: "Belege",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Belege_BezugBelegId",
            table: "Belege",
            column: "BezugBelegId");

        migrationBuilder.CreateIndex(
            name: "IX_Belege_Nummer",
            table: "Belege",
            column: "Nummer",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Belege_OrganisationId",
            table: "Belege",
            column: "OrganisationId");

        migrationBuilder.CreateIndex(
            name: "IX_Belege_PersonId",
            table: "Belege",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Belegpositionen_BelegungId",
            table: "Belegpositionen",
            column: "BelegungId");

        migrationBuilder.CreateIndex(
            name: "IX_Belegpositionen_VeranstaltungId",
            table: "Belegpositionen",
            column: "VeranstaltungId");

        migrationBuilder.CreateIndex(
            name: "IX_Honorarvertraege_PersonId",
            table: "Honorarvertraege",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_Kontoauszuege_AuszugId",
            table: "Kontoauszuege",
            column: "AuszugId");

        migrationBuilder.CreateIndex(
            name: "IX_Kontoauszuege_Sha256",
            table: "Kontoauszuege",
            column: "Sha256",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SepaMandate_PersonId",
            table: "SepaMandate",
            column: "PersonId");

        migrationBuilder.CreateIndex(
            name: "IX_SepaMandate_Referenz",
            table: "SepaMandate",
            column: "Referenz",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Umsaetze_EndToEndId",
            table: "Umsaetze",
            column: "EndToEndId");

        migrationBuilder.CreateIndex(
            name: "IX_Umsaetze_KontoauszugId",
            table: "Umsaetze",
            column: "KontoauszugId");

        // GoBD seit 2025: Buchungsbelege 8 statt 10 Jahre (nur, wenn die Frist noch auf dem alten Vorschlag steht).
        migrationBuilder.Sql("UPDATE Aufbewahrungsfristen SET Jahre = 8 WHERE Schluessel = 'buchhaltung' AND Jahre = 10;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Bankverbindungen");

        migrationBuilder.DropTable(
            name: "Belegpositionen");

        migrationBuilder.DropTable(
            name: "Finanzeinstellungen");

        migrationBuilder.DropTable(
            name: "Finanzprotokoll");

        migrationBuilder.DropTable(
            name: "Honorarvertraege");

        migrationBuilder.DropTable(
            name: "Kontoauszuege");

        migrationBuilder.DropTable(
            name: "Mahnlaeufe");

        migrationBuilder.DropTable(
            name: "Nummernkreise");

        migrationBuilder.DropTable(
            name: "SepaMandate");

        migrationBuilder.DropTable(
            name: "Uebergaben");

        migrationBuilder.DropTable(
            name: "Umsaetze");

        migrationBuilder.DropTable(
            name: "Zahlungslaeufe");

        migrationBuilder.DropTable(
            name: "Belege");

        migrationBuilder.DropColumn(
            name: "AusgefallenAm",
            table: "Veranstaltungen");

        migrationBuilder.DropColumn(
            name: "LeitwegId",
            table: "Organisationen");

        migrationBuilder.DropColumn(
            name: "RechnungsEMail",
            table: "Organisationen");

        migrationBuilder.DropColumn(
            name: "Rechnungsanschrift",
            table: "Organisationen");
    }
}
