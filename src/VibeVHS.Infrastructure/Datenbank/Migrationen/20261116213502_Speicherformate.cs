using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116213502_Speicherformate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<byte[]>(
            name: "Ursprung",
            table: "Zugangstokens",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Zugangstokens",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Zugangstokens",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Ausfuehrung",
            table: "Zahlungslaeufe",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Zahlungslaeufe",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KontoId",
            table: "Warenkoerbe",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Warenkoerbe",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "VeranstaltungId",
            table: "VeranstaltungOrte",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "OrtId",
            table: "VeranstaltungOrte",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "VeranstaltungOrte",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Veranstaltungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<long>(
            name: "Ende",
            table: "Veranstaltungen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "DozentId",
            table: "Veranstaltungen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<long>(
            name: "Beginn",
            table: "Veranstaltungen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Veranstaltungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "ZugeordnetBelegId",
            table: "Umsaetze",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "VorschlagBelegId",
            table: "Umsaetze",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "Valuta",
            table: "Umsaetze",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "KontoauszugId",
            table: "Umsaetze",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "ErzeugterBelegId",
            table: "Umsaetze",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "Buchungstag",
            table: "Umsaetze",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Umsaetze",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Uebergaben",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "VeranstaltungId",
            table: "Termincodes",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "OrtId",
            table: "Termincodes",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Teilnahmeberechtigungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "Teilnahmeberechtigungen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "Ausgestellt",
            table: "Teilnahmeberechtigungen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Teilnahmeberechtigungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Shopkonten",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "Geburtsdatum",
            table: "Shopkonten",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Shopkonten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "WarenkorbId",
            table: "Shopbuchungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Shopbuchungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "MandatId",
            table: "Shopbuchungen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "KontoId",
            table: "Shopbuchungen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Shopbuchungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "WiderrufenAm",
            table: "SepaMandate",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "UnterschriebenAm",
            table: "SepaMandate",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "SepaMandate",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "LetzteNutzungAm",
            table: "SepaMandate",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "ErsteNutzungAm",
            table: "SepaMandate",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "SepaMandate",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Ende",
            table: "Semester",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Beginn",
            table: "Semester",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Semester",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Schnellanmeldungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "BelegungId",
            table: "Schnellanmeldungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Schnellanmeldungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Qualifikationsausnahmen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Qualifikationsausnahmen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Qualifikationsausnahmen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Pruefungstermine",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<long>(
            name: "Beginn",
            table: "Pruefungstermine",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Pruefungstermine",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PruefungId",
            table: "Pruefungsanmeldungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Pruefungsanmeldungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Pruefungsanmeldungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "ObjektId",
            table: "Protokoll",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Protokoll",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Postausgang",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Postausgang",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Portalanliegen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "DateiId",
            table: "Portalanliegen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "BezugId",
            table: "Portalanliegen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Portalanliegen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "VollmachtId",
            table: "PersonVollmachten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonVollmachten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonVollmachten",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonVollmachten",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "BevollmaechtigterId",
            table: "PersonVollmachten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonVollmachten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonRufnummern",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "NummerId",
            table: "PersonRufnummern",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonRufnummern",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonRufnummern",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonRufnummern",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonQualifikationen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonQualifikationen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonQualifikationen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonQualifikationen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonOrganisationen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "OrganisationId",
            table: "PersonOrganisationen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonOrganisationen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonOrganisationen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonOrganisationen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonNamen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonNamen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonNamen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonNamen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonMerkmale",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonMerkmale",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonMerkmale",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonMerkmale",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "ZusammengefuehrtIn",
            table: "Personen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "GesperrtBis",
            table: "Personen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Personen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonEMails",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonEMails",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonEMails",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "AdressId",
            table: "PersonEMails",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonEMails",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonEinwilligungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonEinwilligungen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonEinwilligungen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonEinwilligungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonAdressKennzeichen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonAdressKennzeichen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonAdressKennzeichen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "AdressId",
            table: "PersonAdressKennzeichen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonAdressKennzeichen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "PersonAdressen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "PersonAdressen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "PersonAdressen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "AdressId",
            table: "PersonAdressen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "PersonAdressen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "OrtId",
            table: "OrtMerkmale",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "OrtMerkmale",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "OrtMerkmale",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "OrtMerkmale",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "ElternId",
            table: "Orte",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Orte",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Organisationen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Version",
            table: "Nummernkreise",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Mahnlaeufe",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Loeschlaeufe",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Listenabschluesse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "VeranstaltungId",
            table: "Listenabschluesse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Lichtbilder",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "LoeschenAb",
            table: "Lichtbilder",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Lichtbilder",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Lichtbilder",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Kursunterlagen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "HochgeladenVon",
            table: "Kursunterlagen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "DateiId",
            table: "Kursunterlagen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Kursunterlagen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "KursTeam",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "KursTeam",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "KursTeam",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Kursstaende",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "KursEntgelte",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "KursEntgelte",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "KursEntgelte",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "KursEntgelte",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "SemesterId",
            table: "Kurse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursartId",
            table: "Kurse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "ErzeugtBis",
            table: "Kurse",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "AuftraggeberId",
            table: "Kurse",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Kurse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Kursarten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Von",
            table: "Kontoauszuege",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "Bis",
            table: "Kontoauszuege",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Kontoauszuege",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "VeranstaltungId",
            table: "KontingentEreignisse",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "OrtId",
            table: "KontingentEreignisse",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "BelegungId",
            table: "KontingentEreignisse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "KontingentEreignisse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Integrationskurse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Importlaeufe",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "IkFahrtkosten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "IkFahrtkosten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "GutschriftId",
            table: "IkFahrtkosten",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "IkFahrtkosten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Identitaetspruefungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Identitaetspruefungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Honorarvertraege",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Honorarvertraege",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "Honorarvertraege",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "GueltigAb",
            table: "Honorarvertraege",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Honorarvertraege",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "ObjektId",
            table: "Finanzprotokoll",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "Von",
            table: "Ferien",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Bis",
            table: "Ferien",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Ferien",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Ermaessigungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigVon",
            table: "Ermaessigungen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "GueltigBis",
            table: "Ermaessigungen",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Ermaessigungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonB",
            table: "DublettenAusschluesse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonA",
            table: "DublettenAusschluesse",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Dokumente",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "NeuausstellungVon",
            table: "Dokumente",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Dokumente",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "ErsetztDurch",
            table: "Dokumente",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "BelegungId",
            table: "Dokumente",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Dokumente",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Dateien",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Benutzerkonten",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Benutzerkonten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "VollmachtId",
            table: "Belegungen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "UmgebuchtVonId",
            table: "Belegungen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "UmgebuchtNachId",
            table: "Belegungen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Belegungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Belegungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "KontingentGueltigBis",
            table: "Belegungen",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "BevollmaechtigterId",
            table: "Belegungen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Belegungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "VeranstaltungId",
            table: "Belegpositionen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Belegpositionen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "BelegungId",
            table: "Belegpositionen",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "BelegId",
            table: "Belegpositionen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "UebergabeId",
            table: "Belege",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "StorniertDurchId",
            table: "Belege",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Belege",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "OrganisationId",
            table: "Belege",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "Faellig",
            table: "Belege",
            type: "INTEGER",
            nullable: true,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "DokumentId",
            table: "Belege",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "BezugBelegId",
            table: "Belege",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "Belegdatum",
            table: "Belege",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Belege",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Bankverbindungen",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Faellig",
            table: "Aufgaben",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Aufgaben",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "VeranstaltungId",
            table: "Anwesenheiten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "PersonId",
            table: "Anwesenheiten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "OrtId",
            table: "Anwesenheiten",
            type: "BLOB",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "TEXT",
            oldNullable: true);

        migrationBuilder.AlterColumn<byte[]>(
            name: "KursId",
            table: "Anwesenheiten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Datum",
            table: "Anwesenheiten",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "BelegungId",
            table: "Anwesenheiten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Anwesenheiten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Von",
            table: "Abwesenheiten",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<int>(
            name: "Bis",
            table: "Abwesenheiten",
            type: "INTEGER",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "TEXT");

        migrationBuilder.AlterColumn<byte[]>(
            name: "Id",
            table: "Abwesenheiten",
            type: "BLOB",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "TEXT");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "Ursprung",
            table: "Zugangstokens",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Zugangstokens",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Zugangstokens",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Ausfuehrung",
            table: "Zahlungslaeufe",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Zahlungslaeufe",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KontoId",
            table: "Warenkoerbe",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Warenkoerbe",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "VeranstaltungId",
            table: "VeranstaltungOrte",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "OrtId",
            table: "VeranstaltungOrte",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "VeranstaltungOrte",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Veranstaltungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateTime>(
            name: "Ende",
            table: "Veranstaltungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "DozentId",
            table: "Veranstaltungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "Beginn",
            table: "Veranstaltungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Veranstaltungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "ZugeordnetBelegId",
            table: "Umsaetze",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "VorschlagBelegId",
            table: "Umsaetze",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Valuta",
            table: "Umsaetze",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "KontoauszugId",
            table: "Umsaetze",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "ErzeugterBelegId",
            table: "Umsaetze",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Buchungstag",
            table: "Umsaetze",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Umsaetze",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Uebergaben",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "VeranstaltungId",
            table: "Termincodes",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "OrtId",
            table: "Termincodes",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Teilnahmeberechtigungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "Teilnahmeberechtigungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Ausgestellt",
            table: "Teilnahmeberechtigungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Teilnahmeberechtigungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Shopkonten",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Geburtsdatum",
            table: "Shopkonten",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Shopkonten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "WarenkorbId",
            table: "Shopbuchungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Shopbuchungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "MandatId",
            table: "Shopbuchungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "KontoId",
            table: "Shopbuchungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Shopbuchungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "WiderrufenAm",
            table: "SepaMandate",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "UnterschriebenAm",
            table: "SepaMandate",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "SepaMandate",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "LetzteNutzungAm",
            table: "SepaMandate",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "ErsteNutzungAm",
            table: "SepaMandate",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "SepaMandate",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Ende",
            table: "Semester",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Beginn",
            table: "Semester",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Semester",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Schnellanmeldungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "BelegungId",
            table: "Schnellanmeldungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Schnellanmeldungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Qualifikationsausnahmen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Qualifikationsausnahmen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Qualifikationsausnahmen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Pruefungstermine",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "Beginn",
            table: "Pruefungstermine",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(long),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Pruefungstermine",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PruefungId",
            table: "Pruefungsanmeldungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Pruefungsanmeldungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Pruefungsanmeldungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "ObjektId",
            table: "Protokoll",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Protokoll",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Postausgang",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Postausgang",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Portalanliegen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "DateiId",
            table: "Portalanliegen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "BezugId",
            table: "Portalanliegen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Portalanliegen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "VollmachtId",
            table: "PersonVollmachten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonVollmachten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonVollmachten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonVollmachten",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "BevollmaechtigterId",
            table: "PersonVollmachten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonVollmachten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonRufnummern",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "NummerId",
            table: "PersonRufnummern",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonRufnummern",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonRufnummern",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonRufnummern",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonQualifikationen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonQualifikationen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonQualifikationen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonQualifikationen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonOrganisationen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "OrganisationId",
            table: "PersonOrganisationen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonOrganisationen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonOrganisationen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonOrganisationen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonNamen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonNamen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonNamen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonNamen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonMerkmale",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonMerkmale",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonMerkmale",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonMerkmale",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "ZusammengefuehrtIn",
            table: "Personen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GesperrtBis",
            table: "Personen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Personen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonEMails",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonEMails",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonEMails",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "AdressId",
            table: "PersonEMails",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonEMails",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonEinwilligungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonEinwilligungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonEinwilligungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonEinwilligungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonAdressKennzeichen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonAdressKennzeichen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonAdressKennzeichen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "AdressId",
            table: "PersonAdressKennzeichen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonAdressKennzeichen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "PersonAdressen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "PersonAdressen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "PersonAdressen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "AdressId",
            table: "PersonAdressen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "PersonAdressen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "OrtId",
            table: "OrtMerkmale",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "OrtMerkmale",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "OrtMerkmale",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "OrtMerkmale",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "ElternId",
            table: "Orte",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Orte",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Organisationen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Version",
            table: "Nummernkreise",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Mahnlaeufe",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Loeschlaeufe",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Listenabschluesse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "VeranstaltungId",
            table: "Listenabschluesse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Lichtbilder",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "LoeschenAb",
            table: "Lichtbilder",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Lichtbilder",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Lichtbilder",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Kursunterlagen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "HochgeladenVon",
            table: "Kursunterlagen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "DateiId",
            table: "Kursunterlagen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Kursunterlagen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "KursTeam",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "KursTeam",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "KursTeam",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Kursstaende",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "KursEntgelte",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "KursEntgelte",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "KursEntgelte",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "KursEntgelte",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "SemesterId",
            table: "Kurse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursartId",
            table: "Kurse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "ErzeugtBis",
            table: "Kurse",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "AuftraggeberId",
            table: "Kurse",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Kurse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Kursarten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Von",
            table: "Kontoauszuege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Bis",
            table: "Kontoauszuege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Kontoauszuege",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "VeranstaltungId",
            table: "KontingentEreignisse",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "OrtId",
            table: "KontingentEreignisse",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "BelegungId",
            table: "KontingentEreignisse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "KontingentEreignisse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Integrationskurse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Importlaeufe",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "IkFahrtkosten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "IkFahrtkosten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "GutschriftId",
            table: "IkFahrtkosten",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "IkFahrtkosten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Identitaetspruefungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Identitaetspruefungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Honorarvertraege",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Honorarvertraege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "Honorarvertraege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigAb",
            table: "Honorarvertraege",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Honorarvertraege",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "ObjektId",
            table: "Finanzprotokoll",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Von",
            table: "Ferien",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Bis",
            table: "Ferien",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Ferien",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Ermaessigungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigVon",
            table: "Ermaessigungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "GueltigBis",
            table: "Ermaessigungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Ermaessigungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonB",
            table: "DublettenAusschluesse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonA",
            table: "DublettenAusschluesse",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Dokumente",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "NeuausstellungVon",
            table: "Dokumente",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Dokumente",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "ErsetztDurch",
            table: "Dokumente",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "BelegungId",
            table: "Dokumente",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Dokumente",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Dateien",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Benutzerkonten",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Benutzerkonten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "VollmachtId",
            table: "Belegungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "UmgebuchtVonId",
            table: "Belegungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "UmgebuchtNachId",
            table: "Belegungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Belegungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Belegungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "KontingentGueltigBis",
            table: "Belegungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "BevollmaechtigterId",
            table: "Belegungen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Belegungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "VeranstaltungId",
            table: "Belegpositionen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Belegpositionen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "BelegungId",
            table: "Belegpositionen",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "BelegId",
            table: "Belegpositionen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "UebergabeId",
            table: "Belege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "StorniertDurchId",
            table: "Belege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Belege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "OrganisationId",
            table: "Belege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Faellig",
            table: "Belege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "INTEGER",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "DokumentId",
            table: "Belege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "BezugBelegId",
            table: "Belege",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Belegdatum",
            table: "Belege",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Belege",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Bankverbindungen",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Faellig",
            table: "Aufgaben",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Aufgaben",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "VeranstaltungId",
            table: "Anwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "PersonId",
            table: "Anwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "OrtId",
            table: "Anwesenheiten",
            type: "TEXT",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "BLOB",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "KursId",
            table: "Anwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Datum",
            table: "Anwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "BelegungId",
            table: "Anwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Anwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Von",
            table: "Abwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<DateOnly>(
            name: "Bis",
            table: "Abwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "INTEGER");

        migrationBuilder.AlterColumn<Guid>(
            name: "Id",
            table: "Abwesenheiten",
            type: "TEXT",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "BLOB");
    }
}
