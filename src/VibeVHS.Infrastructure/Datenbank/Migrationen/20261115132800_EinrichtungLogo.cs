using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261115132800_EinrichtungLogo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "Logo",
            table: "Einrichtung",
            type: "BLOB",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Website",
            table: "Einrichtung",
            type: "TEXT",
            maxLength: 200,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Logo",
            table: "Einrichtung");

        migrationBuilder.DropColumn(
            name: "Website",
            table: "Einrichtung");
    }
}
